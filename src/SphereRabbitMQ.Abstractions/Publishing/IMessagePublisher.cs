namespace SphereRabbitMQ.Abstractions.Publishing;

public interface IMessagePublisher<TMessage>
{
    Task PublishAsync(
        TMessage message,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default);

    Task PublishAsync(
        string routingKey,
        TMessage message,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default);
}
