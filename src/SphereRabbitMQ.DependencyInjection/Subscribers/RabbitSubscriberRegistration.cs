using SphereRabbitMQ.Abstractions.Subscribers;
using SphereRabbitMQ.Domain.Subscribers;

namespace SphereRabbitMQ.DependencyInjection.Subscribers;

internal sealed class RabbitSubscriberRegistration<TMessage> : IRabbitSubscriberRegistration
{
    private readonly Action<RabbitSubscriberRegistrationBuilder<TMessage>> _configure;

    public RabbitSubscriberRegistration(Action<RabbitSubscriberRegistrationBuilder<TMessage>> configure)
    {
        _configure = configure;
    }

    public string QueueName { get; private set; } = string.Empty;

    public Task SubscribeAsync(IServiceProvider serviceProvider, IRabbitMQSubscriber subscriber, CancellationToken cancellationToken)
    {
        var builder = new RabbitSubscriberRegistrationBuilder<TMessage>();
        _configure(builder);
        QueueName = builder.Queue;
        return subscriber.SubscribeAsync(builder.Build(serviceProvider), cancellationToken);
    }

    public SubscriberDefinition BuildDefinition(IServiceProvider serviceProvider)
    {
        var builder = new RabbitSubscriberRegistrationBuilder<TMessage>();
        _configure(builder);
        QueueName = builder.Queue;
        return builder.Build(serviceProvider);
    }
}
