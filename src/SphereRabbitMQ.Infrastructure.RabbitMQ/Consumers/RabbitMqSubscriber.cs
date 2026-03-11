using System.Text;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using SphereRabbitMQ.Abstractions.Consumers;
using SphereRabbitMQ.Abstractions.Publishing;
using SphereRabbitMQ.Abstractions.Serialization;
using SphereRabbitMQ.Application.Serialization;
using SphereRabbitMQ.Domain.Consumers;
using SphereRabbitMQ.Domain.Messaging;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Connection;

namespace SphereRabbitMQ.Infrastructure.RabbitMQ.Consumers;

public sealed class RabbitMqSubscriber : ISubscriber
{
    private readonly IConsumerErrorStrategy _consumerErrorStrategy;
    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly ILogger<RabbitMqSubscriber> _logger;
    private readonly IPublisher _publisher;
    private readonly IRetryPolicyResolver _retryPolicyResolver;
    private readonly RetryHeaderAccessor _retryHeaderAccessor;
    private readonly IMessageSerializer _serializer;

    public RabbitMqSubscriber(
        RabbitMqConnectionProvider connectionProvider,
        IMessageSerializer serializer,
        IRetryPolicyResolver retryPolicyResolver,
        IConsumerErrorStrategy consumerErrorStrategy,
        RetryHeaderAccessor retryHeaderAccessor,
        IPublisher publisher,
        ILogger<RabbitMqSubscriber> logger)
    {
        _connectionProvider = connectionProvider;
        _serializer = serializer;
        _retryPolicyResolver = retryPolicyResolver;
        _consumerErrorStrategy = consumerErrorStrategy;
        _retryHeaderAccessor = retryHeaderAccessor;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task SubscribeAsync<TMessage>(ConsumerDefinition<TMessage> definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        EnsureConsumerSettings(definition);

        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        using var processingLimiter = new SemaphoreSlim(definition.MaxConcurrency, definition.MaxConcurrency);
        using var channelOperationLock = new SemaphoreSlim(1, 1);
        var inFlightMessages = new ConcurrentDictionary<Task, byte>();

        await EnsureConsumerTopologyAsync(channel, definition, cancellationToken);

        await channel.BasicQosAsync(0, definition.PrefetchCount, false, cancellationToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            await processingLimiter.WaitAsync(cancellationToken);

            var processingTask = ProcessDeliveryAsync(
                definition,
                channel,
                args,
                processingLimiter,
                channelOperationLock,
                cancellationToken);
            inFlightMessages.TryAdd(processingTask, 0);

            _ = processingTask.ContinueWith(
                completedTask =>
                {
                    inFlightMessages.TryRemove(completedTask, out var _);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        };

        var consumerTag = await channel.BasicConsumeAsync(definition.QueueName, autoAck: false, consumer, cancellationToken);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await ExecuteChannelOperationAsync(channelOperationLock, ct => channel.BasicCancelAsync(consumerTag, true, ct), CancellationToken.None);
            await AwaitInFlightMessagesAsync(inFlightMessages.Keys);
        }
    }

    private async Task ProcessDeliveryAsync<TMessage>(
        ConsumerDefinition<TMessage> definition,
        IChannel channel,
        BasicDeliverEventArgs args,
        SemaphoreSlim processingLimiter,
        SemaphoreSlim channelOperationLock,
        CancellationToken cancellationToken)
    {
        try
        {
            var envelope = CreateEnvelope<TMessage>(args);

            try
            {
                await definition.Handler(envelope, cancellationToken);
                await ExecuteChannelOperationAsync(channelOperationLock, async ct => await channel.BasicAckAsync(args.DeliveryTag, false, ct), cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Consumer execution failed for queue {QueueName}.", definition.QueueName);
                await HandleFailureAsync(definition, envelope, args, exception, channel, channelOperationLock, cancellationToken);
            }
        }
        finally
        {
            processingLimiter.Release();
        }
    }

    private async Task HandleFailureAsync<TMessage>(
        ConsumerDefinition<TMessage> definition,
        MessageEnvelope<TMessage> envelope,
        BasicDeliverEventArgs args,
        Exception exception,
        IChannel channel,
        SemaphoreSlim channelOperationLock,
        CancellationToken cancellationToken)
    {
        var retryMetadata = _retryHeaderAccessor.Read(envelope.Headers);
        var retryDecision = _retryPolicyResolver.Resolve(definition.ErrorHandling, retryMetadata, exception);
        var failureDecision = _consumerErrorStrategy.Resolve(envelope, definition.ErrorHandling, exception, retryDecision);

        switch (failureDecision.Disposition)
        {
            case ConsumerFailureDisposition.Retry:
                _logger.LogWarning("Retrying message {MessageId} with retry count {RetryCount}.", envelope.MessageId, failureDecision.RetryCount);
                await PublishForwardAsync(
                    failureDecision.RetryRoute?.Exchange ?? envelope.Exchange,
                    failureDecision.RetryRoute?.RoutingKey ?? envelope.RoutingKey,
                    envelope.Body,
                    envelope,
                    failureDecision.RetryCount,
                    cancellationToken);
                break;
            case ConsumerFailureDisposition.DeadLetter:
                _logger.LogWarning("Routing message {MessageId} to dead-letter path.", envelope.MessageId);
                await PublishForwardAsync(
                    failureDecision.DeadLetterRoute?.Exchange ?? envelope.Exchange,
                    failureDecision.DeadLetterRoute?.RoutingKey ?? envelope.RoutingKey,
                    envelope.Body,
                    envelope,
                    retryMetadata.RetryCount,
                    cancellationToken);
                break;
            case ConsumerFailureDisposition.Discard:
                _logger.LogWarning("Discarding message {MessageId} after consumer failure.", envelope.MessageId);
                break;
            default:
                throw new InvalidOperationException($"Failure disposition '{failureDecision.Disposition}' is not supported.");
        }

        await ExecuteChannelOperationAsync(channelOperationLock, async ct => await channel.BasicAckAsync(args.DeliveryTag, false, ct), cancellationToken);
    }

    private async Task PublishForwardAsync<TMessage>(
        string exchange,
        string routingKey,
        TMessage message,
        MessageEnvelope<TMessage> envelope,
        int retryCount,
        CancellationToken cancellationToken)
    {
        var headers = _retryHeaderAccessor.Write(envelope.Headers, retryCount);
        await _publisher.PublishAsync(
            exchange,
            routingKey,
            message,
            new PublishOptions
            {
                CorrelationId = envelope.CorrelationId,
                MessageId = envelope.MessageId,
                Timestamp = envelope.Timestamp,
                Headers = headers,
            },
            cancellationToken);
    }

    private MessageEnvelope<TMessage> CreateEnvelope<TMessage>(BasicDeliverEventArgs args)
    {
        var headers = args.BasicProperties.Headers?
            .ToDictionary(pair => pair.Key, pair => NormalizeHeaderValue(pair.Value), StringComparer.Ordinal)
            ?? new Dictionary<string, object?>(StringComparer.Ordinal);

        return new MessageEnvelope<TMessage>(
            _serializer.Deserialize<TMessage>(args.Body),
            headers,
            args.RoutingKey,
            args.Exchange,
            args.BasicProperties.MessageId,
            args.BasicProperties.CorrelationId,
            args.BasicProperties.Timestamp.UnixTime > 0
                ? DateTimeOffset.FromUnixTimeSeconds(args.BasicProperties.Timestamp.UnixTime)
                : null);
    }

    private static object? NormalizeHeaderValue(object? headerValue)
        => headerValue switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            _ => headerValue,
        };

    private static void EnsureConsumerSettings<TMessage>(ConsumerDefinition<TMessage> definition)
    {
        if (definition.PrefetchCount == 0)
        {
            throw new InvalidOperationException($"Consumer '{definition.QueueName}' requires PrefetchCount greater than zero.");
        }

        if (definition.MaxConcurrency <= 0)
        {
            throw new InvalidOperationException($"Consumer '{definition.QueueName}' requires MaxConcurrency greater than zero.");
        }
    }

    private static async Task EnsureConsumerTopologyAsync<TMessage>(
        IChannel channel,
        ConsumerDefinition<TMessage> definition,
        CancellationToken cancellationToken)
    {
        await EnsureQueueExistsAsync(channel, definition.QueueName, cancellationToken);

        if (definition.ErrorHandling.RetryRoute is not null)
        {
            await EnsureExchangeExistsAsync(channel, definition.ErrorHandling.RetryRoute.Exchange, cancellationToken);
            await EnsureQueueExistsAsync(channel, definition.ErrorHandling.RetryRoute.QueueName ?? definition.ErrorHandling.RetryRoute.RoutingKey, cancellationToken);
        }

        if (definition.ErrorHandling.DeadLetterRoute is not null)
        {
            await EnsureExchangeExistsAsync(channel, definition.ErrorHandling.DeadLetterRoute.Exchange, cancellationToken);
            await EnsureQueueExistsAsync(channel, definition.ErrorHandling.DeadLetterRoute.QueueName ?? definition.ErrorHandling.DeadLetterRoute.RoutingKey, cancellationToken);
        }
    }

    private static async Task EnsureExchangeExistsAsync(IChannel channel, string exchangeName, CancellationToken cancellationToken)
    {
        try
        {
            await channel.ExchangeDeclarePassiveAsync(exchangeName, cancellationToken);
        }
        catch (OperationInterruptedException exception)
        {
            throw new InvalidOperationException($"Exchange '{exchangeName}' does not exist. SphereRabbitMQ does not create topology automatically.", exception);
        }
    }

    private static async Task EnsureQueueExistsAsync(IChannel channel, string queueName, CancellationToken cancellationToken)
    {
        try
        {
            await channel.QueueDeclarePassiveAsync(queueName, cancellationToken);
        }
        catch (OperationInterruptedException exception)
        {
            throw new InvalidOperationException($"Queue '{queueName}' does not exist. SphereRabbitMQ does not create topology automatically.", exception);
        }
    }

    private static async Task ExecuteChannelOperationAsync(
        SemaphoreSlim channelOperationLock,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        await channelOperationLock.WaitAsync(cancellationToken);

        try
        {
            await action(cancellationToken);
        }
        finally
        {
            channelOperationLock.Release();
        }
    }

    private static Task AwaitInFlightMessagesAsync(ICollection<Task> inFlightMessages)
        => inFlightMessages.Count == 0
            ? Task.CompletedTask
            : Task.WhenAll(inFlightMessages.ToArray());
}
