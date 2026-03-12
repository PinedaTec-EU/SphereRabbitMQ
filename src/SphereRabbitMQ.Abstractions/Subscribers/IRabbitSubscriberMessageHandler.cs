using SphereRabbitMQ.Domain.Messaging;

namespace SphereRabbitMQ.Abstractions.Subscribers;

public interface IRabbitSubscriberMessageHandler<TMessage>
{
    Task HandleAsync(MessageEnvelope<TMessage> message, CancellationToken cancellationToken);
}
