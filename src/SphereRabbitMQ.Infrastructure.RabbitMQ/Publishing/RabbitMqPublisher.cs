using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.Abstractions.Publishing;
using SphereRabbitMQ.Abstractions.Serialization;

namespace SphereRabbitMQ.Infrastructure.RabbitMQ.Publishing;

public sealed class RabbitMqPublisher : IPublisher
{
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly IMessageSerializer _messageSerializer;
    private readonly SphereRabbitMqOptions _options;
    private readonly RabbitMqChannelPool _channelPool;

    public RabbitMqPublisher(
        RabbitMqChannelPool channelPool,
        IMessageSerializer messageSerializer,
        IOptions<SphereRabbitMqOptions> options,
        ILogger<RabbitMqPublisher> logger)
    {
        _channelPool = channelPool;
        _messageSerializer = messageSerializer;
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
        await using var channelLease = await _channelPool.RentAsync(cancellationToken);
        var channel = channelLease.Channel;

        try
        {
            await channel.ExchangeDeclarePassiveAsync(exchange, cancellationToken);
        }
        catch (OperationInterruptedException exception)
        {
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
        };
        var body = _messageSerializer.Serialize(message);

        _logger.LogInformation("Publishing message to exchange {Exchange} with routing key {RoutingKey}.", exchange, routingKey);
        await channel.BasicPublishAsync(exchange, routingKey, mandatory: true, properties, body, cancellationToken);
    }
}
