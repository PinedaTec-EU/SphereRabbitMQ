using SphereRabbitMQ.Abstractions.Publishing;

namespace SphereRabbitMQ.DependencyInjection.Publishing;

internal sealed class PreconfiguredMessagePublisher<TMessage> : IMessagePublisher<TMessage>
{
    private readonly string _exchange;
    private readonly IRabbitMQPublisher _publisher;
    private readonly string _routingKey;

    public PreconfiguredMessagePublisher(
        IRabbitMQPublisher publisher,
        string exchange,
        string routingKey)
    {
        _publisher = publisher;
        _exchange = exchange;
        _routingKey = routingKey;
    }

    public Task PublishAsync(
        TMessage message,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(_exchange, _routingKey, message, options, cancellationToken);

    public Task PublishAsync(
        string routingKey,
        TMessage message,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(_exchange, routingKey, message, options, cancellationToken);
}
