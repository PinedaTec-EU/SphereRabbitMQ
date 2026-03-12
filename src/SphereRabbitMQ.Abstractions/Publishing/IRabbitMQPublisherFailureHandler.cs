using SphereRabbitMQ.Domain.Publishing;

namespace SphereRabbitMQ.Abstractions.Publishing;

public interface IRabbitMQPublisherFailureHandler
{
    Task OnPublishFailureAsync(PublisherFailureContext context, CancellationToken cancellationToken);
}
