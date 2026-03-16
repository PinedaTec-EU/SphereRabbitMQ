using SphereRabbitMQ.Domain.Retry;

namespace SphereRabbitMQ.Abstractions.Subscribers;

public interface ISubscriberRetryDelayResolver<TMessage>
{
    TimeSpan Resolve(SubscriberRetryDelayContext<TMessage> context);
}
