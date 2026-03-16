using System.Collections.Concurrent;
using System.Text;

using Microsoft.Extensions.Logging;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System.Globalization;

using SphereRabbitMQ.Abstractions.Publishing;
using SphereRabbitMQ.Abstractions.Serialization;
using SphereRabbitMQ.Abstractions.Subscribers;
using SphereRabbitMQ.Application.Serialization;
using SphereRabbitMQ.Domain.Messaging;
using SphereRabbitMQ.Domain.Retry;
using SphereRabbitMQ.Domain.Subscribers;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Connection;

namespace SphereRabbitMQ.Infrastructure.RabbitMQ.Subscribers;

internal sealed class RabbitMqSubscriber : IRabbitMQSubscriber
{
    private readonly ISubscriberErrorStrategy _subscriberErrorStrategy;
    private readonly ISubscriberInfrastructureRouteResolver _subscriberInfrastructureRouteResolver;
    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly ILogger<RabbitMqSubscriber> _logger;
    private readonly IRabbitMQPublisher _publisher;
    private readonly IRetryPolicyResolver _retryPolicyResolver;
    private readonly RetryHeaderAccessor _retryHeaderAccessor;
    private readonly IMessageSerializer _serializer;

    public RabbitMqSubscriber(
        RabbitMqConnectionProvider connectionProvider,
        IMessageSerializer serializer,
        IRetryPolicyResolver retryPolicyResolver,
        ISubscriberErrorStrategy subscriberErrorStrategy,
        ISubscriberInfrastructureRouteResolver subscriberInfrastructureRouteResolver,
        RetryHeaderAccessor retryHeaderAccessor,
        IRabbitMQPublisher publisher,
        ILogger<RabbitMqSubscriber> logger)
    {
        _connectionProvider = connectionProvider;
        _serializer = serializer;
        _retryPolicyResolver = retryPolicyResolver;
        _subscriberErrorStrategy = subscriberErrorStrategy;
        _subscriberInfrastructureRouteResolver = subscriberInfrastructureRouteResolver;
        _retryHeaderAccessor = retryHeaderAccessor;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task SubscribeAsync<TMessage>(SubscriberDefinition<TMessage> definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition = ResolveInfrastructureRoutes(definition);
        EnsureSubscriberSettings(definition);

        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        using var processingLimiter = new SemaphoreSlim(definition.MaxConcurrency, definition.MaxConcurrency);
        using var channelOperationLock = new SemaphoreSlim(1, 1);
        var inFlightMessages = new ConcurrentDictionary<Task, byte>();

        await EnsureSubscriberTopologyAsync(channel, definition, cancellationToken);

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
        SubscriberDefinition<TMessage> definition,
        IChannel channel,
        BasicDeliverEventArgs args,
        SemaphoreSlim processingLimiter,
        SemaphoreSlim channelOperationLock,
        CancellationToken cancellationToken)
    {
        try
        {
            var rawEnvelope = CreateRawEnvelope(args);
            MessageEnvelope<TMessage> envelope;

            try
            {
                envelope = CreateEnvelope<TMessage>(args, rawEnvelope);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Subscriber component failure while deserializing a message for queue {QueueName}.", definition.QueueName);
                await HandleComponentFailureAsync(definition, rawEnvelope, args, exception, SubscriberComponentFailureStage.Deserialize, channel, channelOperationLock, cancellationToken);
                return;
            }

            try
            {
                await definition.Handler(envelope, cancellationToken);
                await AcknowledgeAsync(definition, rawEnvelope, args, channel, channelOperationLock, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Subscriber execution failed for queue {QueueName}.", definition.QueueName);
                await HandleFailureAsync(definition, envelope, rawEnvelope, args, exception, channel, channelOperationLock, cancellationToken);
            }
        }
        finally
        {
            processingLimiter.Release();
        }
    }

    private async Task HandleFailureAsync<TMessage>(
        SubscriberDefinition<TMessage> definition,
        MessageEnvelope<TMessage> envelope,
        MessageEnvelope<ReadOnlyMemory<byte>> rawEnvelope,
        BasicDeliverEventArgs args,
        Exception exception,
        IChannel channel,
        SemaphoreSlim channelOperationLock,
        CancellationToken cancellationToken)
    {
        var retryMetadata = _retryHeaderAccessor.Read(envelope.Headers);
        var retryDecision = _retryPolicyResolver.Resolve(definition.ErrorHandling, retryMetadata, exception);
        var failureDecision = _subscriberErrorStrategy.Resolve(envelope, definition.ErrorHandling, exception, retryDecision);

        switch (failureDecision.Disposition)
        {
            case SubscriberFailureDisposition.Retry:
                _logger.LogWarning("Retrying message {MessageId} with retry count {RetryCount}.", envelope.MessageId, failureDecision.RetryCount);
                try
                {
                    var retryDelay = ResolveRetryDelay(definition, envelope, exception, failureDecision.RetryCount);
                    await PublishForwardAsync(
                        failureDecision.RetryRoute?.Exchange ?? envelope.Exchange,
                        failureDecision.RetryRoute?.RoutingKey ?? envelope.RoutingKey,
                        envelope.Body,
                        envelope,
                        failureDecision.RetryCount,
                        retryDelay,
                        cancellationToken);
                }
                catch (Exception publishException)
                {
                    await NotifyComponentFailureAsync(definition, rawEnvelope, publishException, SubscriberComponentFailureStage.RetryForward, cancellationToken);
                    throw;
                }
                break;
            case SubscriberFailureDisposition.DeadLetter:
                _logger.LogWarning("Routing message {MessageId} to dead-letter path.", envelope.MessageId);
                try
                {
                    await PublishForwardAsync(
                        failureDecision.DeadLetterRoute?.Exchange ?? envelope.Exchange,
                        failureDecision.DeadLetterRoute?.RoutingKey ?? envelope.RoutingKey,
                        envelope.Body,
                        envelope,
                        retryMetadata.RetryCount,
                        null,
                        cancellationToken);
                }
                catch (Exception publishException)
                {
                    await NotifyComponentFailureAsync(definition, rawEnvelope, publishException, SubscriberComponentFailureStage.DeadLetterForward, cancellationToken);
                    throw;
                }

                await NotifyDeadLetterAsync(definition, envelope, exception, failureDecision, cancellationToken);
                break;
            case SubscriberFailureDisposition.Discard:
                _logger.LogWarning("Discarding message {MessageId} after subscriber failure.", envelope.MessageId);
                break;
            default:
                throw new InvalidOperationException($"Failure disposition '{failureDecision.Disposition}' is not supported.");
        }

        await AcknowledgeAsync(definition, rawEnvelope, args, channel, channelOperationLock, cancellationToken);
    }

    private async Task PublishForwardAsync<TMessage>(
        string exchange,
        string routingKey,
        TMessage message,
        MessageEnvelope<TMessage> envelope,
        int retryCount,
        TimeSpan? retryDelay,
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
                TimeToLive = retryDelay,
            },
            cancellationToken);
    }

    private async Task NotifyDeadLetterAsync<TMessage>(
        SubscriberDefinition<TMessage> definition,
        MessageEnvelope<TMessage> envelope,
        Exception exception,
        SubscriberFailureDecision failureDecision,
        CancellationToken cancellationToken)
    {
        if (definition.DeadLetterNotificationHandler is null)
        {
            return;
        }

        try
        {
            await definition.DeadLetterNotificationHandler(
                new SubscriberDeadLetterNotification<TMessage>(envelope, exception, failureDecision),
                cancellationToken);
        }
        catch (Exception notificationException)
        {
            _logger.LogError(
                notificationException,
                "Dead-letter notification handler failed for queue {QueueName} and message {MessageId}.",
                definition.QueueName,
                envelope.MessageId);
        }
    }

    private MessageEnvelope<TMessage> CreateEnvelope<TMessage>(BasicDeliverEventArgs args, MessageEnvelope<ReadOnlyMemory<byte>> rawEnvelope)
        => new(
            _serializer.Deserialize<TMessage>(rawEnvelope.Body),
            rawEnvelope.Headers,
            rawEnvelope.RoutingKey,
            rawEnvelope.Exchange,
            rawEnvelope.MessageId,
            rawEnvelope.CorrelationId,
            rawEnvelope.Timestamp);

    private MessageEnvelope<ReadOnlyMemory<byte>> CreateRawEnvelope(BasicDeliverEventArgs args)
    {
        var headers = args.BasicProperties.Headers?
            .ToDictionary(pair => pair.Key, pair => NormalizeHeaderValue(pair.Value), StringComparer.Ordinal)
            ?? new Dictionary<string, object?>(StringComparer.Ordinal);

        return new MessageEnvelope<ReadOnlyMemory<byte>>(
            args.Body,
            headers,
            args.RoutingKey,
            args.Exchange,
            args.BasicProperties.MessageId,
            args.BasicProperties.CorrelationId,
            args.BasicProperties.Timestamp.UnixTime > 0
                ? DateTimeOffset.FromUnixTimeSeconds(args.BasicProperties.Timestamp.UnixTime)
                : null);
    }

    private async Task HandleComponentFailureAsync<TMessage>(
        SubscriberDefinition<TMessage> definition,
        MessageEnvelope<ReadOnlyMemory<byte>> rawEnvelope,
        BasicDeliverEventArgs args,
        Exception exception,
        SubscriberComponentFailureStage stage,
        IChannel channel,
        SemaphoreSlim channelOperationLock,
        CancellationToken cancellationToken)
    {
        exception = NormalizeComponentFailureException(stage, exception);
        var retryMetadata = _retryHeaderAccessor.Read(rawEnvelope.Headers);
        var retryDecision = _retryPolicyResolver.Resolve(definition.ErrorHandling, retryMetadata, exception);
        var failureDecision = ResolveDefaultComponentFailureDecision(rawEnvelope, definition, exception, retryDecision);
        var handlingResult = await ResolveComponentFailureHandlingResultAsync(definition, rawEnvelope, exception, retryMetadata, stage, cancellationToken);
        failureDecision = OverrideComponentFailureDecision(failureDecision, definition.ErrorHandling, handlingResult, retryDecision);

        switch (failureDecision.Disposition)
        {
            case SubscriberFailureDisposition.Retry:
                var retryDelay = ResolveDefaultRetryDelay(failureDecision.RetryCount);
                await PublishRawForwardAsync(
                    channel,
                    channelOperationLock,
                    failureDecision.RetryRoute?.Exchange ?? rawEnvelope.Exchange,
                    failureDecision.RetryRoute?.RoutingKey ?? rawEnvelope.RoutingKey,
                    args,
                    rawEnvelope,
                    failureDecision.RetryCount,
                    retryDelay,
                    cancellationToken);
                await AcknowledgeAsync(definition, rawEnvelope, args, channel, channelOperationLock, cancellationToken);
                break;
            case SubscriberFailureDisposition.DeadLetter:
                await PublishRawForwardAsync(
                    channel,
                    channelOperationLock,
                    failureDecision.DeadLetterRoute?.Exchange ?? rawEnvelope.Exchange,
                    failureDecision.DeadLetterRoute?.RoutingKey ?? rawEnvelope.RoutingKey,
                    args,
                    rawEnvelope,
                    failureDecision.RetryCount,
                    null,
                    cancellationToken);
                await AcknowledgeAsync(definition, rawEnvelope, args, channel, channelOperationLock, cancellationToken);
                break;
            case SubscriberFailureDisposition.Discard:
                await AcknowledgeAsync(definition, rawEnvelope, args, channel, channelOperationLock, cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Failure disposition '{failureDecision.Disposition}' is not supported.");
        }
    }

    private async Task<SubscriberComponentFailureHandlingResult> ResolveComponentFailureHandlingResultAsync<TMessage>(
        SubscriberDefinition<TMessage> definition,
        MessageEnvelope<ReadOnlyMemory<byte>> rawEnvelope,
        Exception exception,
        RetryMetadata retryMetadata,
        SubscriberComponentFailureStage stage,
        CancellationToken cancellationToken)
    {
        return definition.ComponentFailureHandler is null
            ? SubscriberComponentFailureHandlingResult.UseDefault
            : await definition.ComponentFailureHandler(
                new SubscriberComponentFailureContext(
                    definition.QueueName,
                    rawEnvelope,
                    definition.ErrorHandling,
                    retryMetadata,
                    stage,
                    exception),
                cancellationToken);
    }

    private SubscriberFailureDecision ResolveDefaultComponentFailureDecision<TMessage>(
        MessageEnvelope<ReadOnlyMemory<byte>> rawEnvelope,
        SubscriberDefinition<TMessage> definition,
        Exception exception,
        RetryDecision retryDecision)
        => _subscriberErrorStrategy.Resolve(rawEnvelope, definition.ErrorHandling, exception, retryDecision);

    private static SubscriberFailureDecision OverrideComponentFailureDecision(
        SubscriberFailureDecision defaultDecision,
        SubscriberErrorHandlingSettings settings,
        SubscriberComponentFailureHandlingResult handlingResult,
        RetryDecision retryDecision)
    {
        return handlingResult.Action switch
        {
            SubscriberComponentFailureAction.UseDefault => defaultDecision,
            SubscriberComponentFailureAction.Retry when settings.RetryRoute is not null
                => new SubscriberFailureDecision(SubscriberFailureDisposition.Retry, retryDecision.NextRetryCount, settings.RetryRoute, settings.DeadLetterRoute),
            SubscriberComponentFailureAction.DeadLetter when settings.DeadLetterRoute is not null
                => new SubscriberFailureDecision(SubscriberFailureDisposition.DeadLetter, retryDecision.NextRetryCount, settings.RetryRoute, settings.DeadLetterRoute),
            SubscriberComponentFailureAction.Discard
                => new SubscriberFailureDecision(SubscriberFailureDisposition.Discard, retryDecision.NextRetryCount, settings.RetryRoute, settings.DeadLetterRoute),
            _ => defaultDecision,
        };
    }

    private async Task PublishRawForwardAsync(
        IChannel channel,
        SemaphoreSlim channelOperationLock,
        string exchange,
        string routingKey,
        BasicDeliverEventArgs args,
        MessageEnvelope<ReadOnlyMemory<byte>> rawEnvelope,
        int retryCount,
        TimeSpan? retryDelay,
        CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            ContentType = args.BasicProperties.ContentType,
            Persistent = args.BasicProperties.Persistent,
            CorrelationId = rawEnvelope.CorrelationId,
            MessageId = rawEnvelope.MessageId,
            Timestamp = args.BasicProperties.Timestamp,
            Headers = new Dictionary<string, object?>(_retryHeaderAccessor.Write(rawEnvelope.Headers, retryCount), StringComparer.Ordinal),
            Expiration = retryDelay is null ? args.BasicProperties.Expiration : FormatExpiration(retryDelay.Value),
        };

        await ExecuteChannelOperationAsync(
            channelOperationLock,
            async ct => await channel.BasicPublishAsync(exchange, routingKey, true, properties, rawEnvelope.Body, ct),
            cancellationToken);
    }

    private static TimeSpan ResolveRetryDelay<TMessage>(
        SubscriberDefinition<TMessage> definition,
        MessageEnvelope<TMessage> envelope,
        Exception exception,
        int attemptNumber)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(exception);

        if (attemptNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Retry attempt number must be greater than zero.");
        }

        if (definition.RetryDelayResolver is null)
        {
            return ResolveDefaultRetryDelay(attemptNumber);
        }

        var retryDelay = definition.RetryDelayResolver(
            new SubscriberRetryDelayContext<TMessage>(
                definition.QueueName,
                envelope,
                exception,
                attemptNumber));

        if (retryDelay <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"Retry delay resolver returned a non-positive delay for queue '{definition.QueueName}' and attempt '{attemptNumber}'.");
        }

        return retryDelay;
    }

    private static TimeSpan ResolveDefaultRetryDelay(int attemptNumber)
    {
        if (attemptNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Retry attempt number must be greater than zero.");
        }

        return TimeSpan.FromMilliseconds(attemptNumber * 250);
    }

    private static Exception NormalizeComponentFailureException(SubscriberComponentFailureStage stage, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return stage is SubscriberComponentFailureStage.Deserialize
            ? new NonRetriableMessageException("Message deserialization failed.", exception)
            : exception;
    }

    private static string FormatExpiration(TimeSpan timeToLive)
        => Convert.ToInt64(Math.Ceiling(timeToLive.TotalMilliseconds), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    private async Task AcknowledgeAsync<TMessage>(
        SubscriberDefinition<TMessage> definition,
        MessageEnvelope<ReadOnlyMemory<byte>> rawEnvelope,
        BasicDeliverEventArgs args,
        IChannel channel,
        SemaphoreSlim channelOperationLock,
        CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteChannelOperationAsync(channelOperationLock, async ct => await channel.BasicAckAsync(args.DeliveryTag, false, ct), cancellationToken);
        }
        catch (Exception exception)
        {
            await NotifyComponentFailureAsync(definition, rawEnvelope, exception, SubscriberComponentFailureStage.Acknowledge, cancellationToken);
            throw;
        }
    }

    private async Task NotifyComponentFailureAsync<TMessage>(
        SubscriberDefinition<TMessage> definition,
        MessageEnvelope<ReadOnlyMemory<byte>> rawEnvelope,
        Exception exception,
        SubscriberComponentFailureStage stage,
        CancellationToken cancellationToken)
    {
        if (definition.ComponentFailureHandler is null)
        {
            return;
        }

        try
        {
            await definition.ComponentFailureHandler(
                new SubscriberComponentFailureContext(
                    definition.QueueName,
                    rawEnvelope,
                    definition.ErrorHandling,
                    _retryHeaderAccessor.Read(rawEnvelope.Headers),
                    stage,
                    exception),
                cancellationToken);
        }
        catch (Exception handlerException)
        {
            _logger.LogError(handlerException, "Subscriber component failure handler failed for queue {QueueName}.", definition.QueueName);
        }
    }

    private static object? NormalizeHeaderValue(object? headerValue)
        => headerValue switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            _ => headerValue,
        };

    private static void EnsureSubscriberSettings<TMessage>(SubscriberDefinition<TMessage> definition)
    {
        if (definition.MaxConcurrency <= 0)
        {
            throw new InvalidOperationException($"Subscriber '{definition.QueueName}' requires MaxConcurrency greater than zero.");
        }
    }

    private SubscriberDefinition<TMessage> ResolveInfrastructureRoutes<TMessage>(SubscriberDefinition<TMessage> definition)
    {
        var errorHandling = definition.ErrorHandling with
        {
            RetryRoute = definition.ErrorHandling.RetryRoute ?? ResolveRetryRoute(definition.QueueName, definition.ErrorHandling.Strategy),
            DeadLetterRoute = definition.ErrorHandling.DeadLetterRoute ?? ResolveDeadLetterRoute(definition.QueueName, definition.ErrorHandling.Strategy),
        };

        return definition with
        {
            ErrorHandling = errorHandling,
        };
    }

    private RetryRouteDefinition? ResolveRetryRoute(string queueName, SubscriberErrorStrategyKind strategy)
        => strategy is SubscriberErrorStrategyKind.RetryThenDeadLetter
            ? _subscriberInfrastructureRouteResolver.ResolveRetryRoute(queueName)
            : null;

    private DeadLetterRouteDefinition? ResolveDeadLetterRoute(string queueName, SubscriberErrorStrategyKind strategy)
        => strategy is SubscriberErrorStrategyKind.DeadLetterOnly or SubscriberErrorStrategyKind.RetryThenDeadLetter
            ? _subscriberInfrastructureRouteResolver.ResolveDeadLetterRoute(queueName)
            : null;

    private static async Task EnsureSubscriberTopologyAsync<TMessage>(
        IChannel channel,
        SubscriberDefinition<TMessage> definition,
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
