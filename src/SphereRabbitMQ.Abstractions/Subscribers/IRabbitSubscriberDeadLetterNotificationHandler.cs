using SphereRabbitMQ.Domain.Subscribers;

namespace SphereRabbitMQ.Abstractions.Subscribers;

public interface IRabbitSubscriberDeadLetterNotificationHandler<TMessage>
{
    Task OnDeadLetterAsync(SubscriberDeadLetterNotification<TMessage> notification, CancellationToken cancellationToken);
}
