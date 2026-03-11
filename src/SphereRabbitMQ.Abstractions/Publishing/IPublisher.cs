using SphereRabbitMQ.Abstractions.Publishing;

namespace SphereRabbitMQ.Abstractions.Publishing;

public interface IPublisher
{
    Task PublishAsync<TMessage>(
        string exchange,
        string routingKey,
        TMessage message,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default);
}
