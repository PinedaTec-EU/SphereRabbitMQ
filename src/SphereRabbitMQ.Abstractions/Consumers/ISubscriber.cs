using SphereRabbitMQ.Domain.Consumers;

namespace SphereRabbitMQ.Abstractions.Consumers;

public interface ISubscriber
{
    Task SubscribeAsync<TMessage>(ConsumerDefinition<TMessage> definition, CancellationToken cancellationToken = default);
}
