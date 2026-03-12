using SphereRabbitMQ.Abstractions.Subscribers;
using SphereRabbitMQ.Domain.Subscribers;

namespace SphereRabbitMQ.DependencyInjection.Subscribers;

internal interface IRabbitSubscriberRegistration
{
    Task SubscribeAsync(IServiceProvider serviceProvider, IRabbitMQSubscriber subscriber, CancellationToken cancellationToken);

    string QueueName { get; }

    SubscriberDefinition BuildDefinition(IServiceProvider serviceProvider);
}
