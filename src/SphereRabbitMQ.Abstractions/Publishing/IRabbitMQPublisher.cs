using SphereRabbitMQ.Abstractions.Publishing;

namespace SphereRabbitMQ.Abstractions.Publishing;

public interface IRabbitMQPublisher
{
    Task PublishAsync<TMessage>(
        string exchange,
        string routingKey,
        TMessage message,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default);
}
