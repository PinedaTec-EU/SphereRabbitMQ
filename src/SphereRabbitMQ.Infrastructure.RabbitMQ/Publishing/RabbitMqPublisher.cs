using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;

using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.Abstractions.Publishing;
using SphereRabbitMQ.Abstractions.Serialization;
using SphereRabbitMQ.Domain.Publishing;

namespace SphereRabbitMQ.Infrastructure.RabbitMQ.Publishing;

internal sealed class RabbitMqPublisher : IRabbitMQPublisher
{
    private readonly IEnumerable<IRabbitMQPublisherFailureHandler> _failureHandlers;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly IMessageSerializer _messageSerializer;
    private readonly SphereRabbitMqOptions _options;
    private readonly RabbitMqChannelPool _channelPool;

    public RabbitMqPublisher(
        RabbitMqChannelPool channelPool,
        IMessageSerializer messageSerializer,
        IEnumerable<IRabbitMQPublisherFailureHandler> failureHandlers,
        IOptions<SphereRabbitMqOptions> options,
        ILogger<RabbitMqPublisher> logger)
    {
        _channelPool = channelPool;
        _messageSerializer = messageSerializer;
        _failureHandlers = failureHandlers;
        _options = options.Value;
        _logger = logger;
    }

    public async Task PublishAsync<TMessage>(
        string exchange,
        string routingKey,
        TMessage message,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exchange);
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);

        options ??= new PublishOptions();
        ValidatePublishOptions(options);
        await using var channelLease = await _channelPool.RentAsync(cancellationToken);
        var channel = channelLease.Channel;

        try
        {
            await channel.ExchangeDeclarePassiveAsync(exchange, cancellationToken);
        }
        catch (OperationInterruptedException exception)
        {
            await NotifyFailureHandlersAsync(exchange, routingKey, typeof(TMessage), PublisherFailureStage.EnsureExchangeExists, exception, cancellationToken);
            throw new InvalidOperationException($"Exchange '{exchange}' does not exist. SphereRabbitMQ does not create topology automatically.", exception);
        }

        var properties = new BasicProperties
        {
            ContentType = _messageSerializer.ContentType,
            Persistent = options.Persistent,
            CorrelationId = options.CorrelationId,
            MessageId = options.MessageId,
            Timestamp = new AmqpTimestamp((options.Timestamp ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>(options.Headers, StringComparer.Ordinal),
            Expiration = options.TimeToLive is null ? null : FormatExpiration(options.TimeToLive.Value),
            Priority = options.Priority ?? 0,
        };
        ReadOnlyMemory<byte> body;

        try
        {
            body = _messageSerializer.Serialize(message);
        }
        catch (Exception exception)
        {
            await NotifyFailureHandlersAsync(exchange, routingKey, typeof(TMessage), PublisherFailureStage.Serialize, exception, cancellationToken);
            throw;
        }

        _logger.LogInformation("Publishing message to exchange {Exchange} with routing key {RoutingKey}.", exchange, routingKey);
        try
        {
            await channel.BasicPublishAsync(exchange, routingKey, mandatory: true, properties, body, cancellationToken);
        }
        catch (Exception exception)
        {
            await NotifyFailureHandlersAsync(exchange, routingKey, typeof(TMessage), PublisherFailureStage.Publish, exception, cancellationToken);
            throw;
        }
    }

    private async Task NotifyFailureHandlersAsync(
        string exchange,
        string routingKey,
        Type messageType,
        PublisherFailureStage stage,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var context = new PublisherFailureContext(exchange, routingKey, messageType, stage, exception);

        foreach (var failureHandler in _failureHandlers)
        {
            try
            {
                await failureHandler.OnPublishFailureAsync(context, cancellationToken);
            }
            catch (Exception handlerException)
            {
                _logger.LogError(handlerException, "Publisher failure handler failed for exchange {Exchange} and routing key {RoutingKey}.", exchange, routingKey);
            }
        }
    }

    private static void ValidatePublishOptions(PublishOptions options)
    {
        if (options.TimeToLive is not null && options.TimeToLive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Message ttl must be greater than zero.");
        }
    }

    private static string FormatExpiration(TimeSpan timeToLive)
        => Convert.ToInt64(Math.Ceiling(timeToLive.TotalMilliseconds), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
}
