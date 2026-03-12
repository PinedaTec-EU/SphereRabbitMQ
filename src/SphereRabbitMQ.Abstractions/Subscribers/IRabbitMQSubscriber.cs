using SphereRabbitMQ.Domain.Subscribers;

namespace SphereRabbitMQ.Abstractions.Subscribers;

public interface IRabbitMQSubscriber
{
    Task SubscribeAsync<TMessage>(SubscriberDefinition<TMessage> definition, CancellationToken cancellationToken = default);
}
