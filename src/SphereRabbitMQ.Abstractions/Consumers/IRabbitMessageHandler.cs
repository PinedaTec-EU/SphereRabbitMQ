using SphereRabbitMQ.Domain.Messaging;

namespace SphereRabbitMQ.Abstractions.Consumers;

public interface IRabbitMessageHandler<TMessage>
{
    Task HandleAsync(MessageEnvelope<TMessage> message, CancellationToken cancellationToken);
}
