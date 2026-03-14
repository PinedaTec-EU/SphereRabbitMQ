using SphereRabbitMQ.Abstractions.Publishing;

namespace SphereRabbitMQ.DependencyInjection.Publishing;

internal sealed class PreconfiguredMessagePublisher<TMessage> : IMessagePublisher<TMessage>
{
    private const string RoutingKeyParameterName = "routingKey";

    private readonly string _exchange;
    private readonly IRabbitMQPublisher _publisher;
    private readonly string? _routingKey;

    public PreconfiguredMessagePublisher(
        IRabbitMQPublisher publisher,
        string exchange,
        string? routingKey)
    {
        _publisher = publisher;
        _exchange = exchange;
        _routingKey = routingKey;
    }

    public Task PublishAsync(
        TMessage message,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_routingKey))
        {
            throw new InvalidOperationException(
                $"Rabbit publisher '{typeof(TMessage).Name}' does not have a default routing key configured. Use PublishAsync(routingKey, message, ...) or configure WithRoutingKey(...).");
        }

        return _publisher.PublishAsync(_exchange, _routingKey, message, options, cancellationToken);
    }

    public Task PublishAsync(
        string routingKey,
        TMessage message,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey, RoutingKeyParameterName);
        return _publisher.PublishAsync(_exchange, routingKey, message, options, cancellationToken);
    }
}
