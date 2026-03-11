using SphereRabbitMQ.Abstractions.Consumers;

namespace SphereRabbitMQ.DependencyInjection.Consumers;

internal sealed class RabbitConsumerRegistration<TMessage> : IRabbitConsumerRegistration
{
    private readonly Action<RabbitConsumerRegistrationBuilder<TMessage>> _configure;

    public RabbitConsumerRegistration(Action<RabbitConsumerRegistrationBuilder<TMessage>> configure)
    {
        _configure = configure;
    }

    public string QueueName { get; private set; } = string.Empty;

    public Task SubscribeAsync(IServiceProvider serviceProvider, ISubscriber subscriber, CancellationToken cancellationToken)
    {
        var builder = new RabbitConsumerRegistrationBuilder<TMessage>();
        _configure(builder);
        QueueName = builder.Queue;
        return subscriber.SubscribeAsync(builder.Build(serviceProvider), cancellationToken);
    }
}
