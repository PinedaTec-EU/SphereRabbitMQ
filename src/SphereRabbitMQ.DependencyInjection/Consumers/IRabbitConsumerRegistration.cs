using SphereRabbitMQ.Abstractions.Consumers;

namespace SphereRabbitMQ.DependencyInjection.Consumers;

internal interface IRabbitConsumerRegistration
{
    Task SubscribeAsync(IServiceProvider serviceProvider, ISubscriber subscriber, CancellationToken cancellationToken);

    string QueueName { get; }
}
