using SphereRabbitMQ.Domain.Subscribers;

namespace SphereRabbitMQ.Abstractions.Subscribers;

public interface IRabbitSubscriberComponentFailureHandler<TMessage>
{
    Task<SubscriberComponentFailureHandlingResult> OnComponentFailureAsync(
        SubscriberComponentFailureContext context,
        CancellationToken cancellationToken);
}
