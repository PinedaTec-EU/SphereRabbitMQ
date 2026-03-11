using Microsoft.Extensions.DependencyInjection;
using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.Abstractions.Consumers;
using SphereRabbitMQ.Abstractions.Publishing;
using SphereRabbitMQ.Abstractions.Serialization;
using SphereRabbitMQ.Abstractions.Topology;
using SphereRabbitMQ.Application.Consumers;
using SphereRabbitMQ.Application.Retry;
using SphereRabbitMQ.Application.Serialization;
using SphereRabbitMQ.DependencyInjection.Consumers;
using SphereRabbitMQ.Domain.Messaging;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Connection;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Migration;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Migration.Interfaces;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Consumers;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Publishing;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Serialization;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Topology;

namespace SphereRabbitMQ.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSphereRabbitMq(
        this IServiceCollection services,
        Action<SphereRabbitMqOptions>? configure = null)
    {
        services.AddOptions<SphereRabbitMqOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<RabbitMqChannelPool>();
        services.AddSingleton<IQueueMessageMover, RabbitMqQueueMessageMover>();
        services.AddSingleton<RetryHeaderAccessor>();
        services.AddSingleton<IMessageSerializer, SystemTextJsonMessageSerializer>();
        services.AddSingleton<IPublisher, RabbitMqPublisher>();
        services.AddSingleton<ISubscriber, RabbitMqSubscriber>();
        services.AddSingleton<IConsumerErrorStrategy, DefaultConsumerErrorStrategy>();
        services.AddSingleton<IRetryPolicyResolver, DefaultRetryPolicyResolver>();
        services.AddSingleton<IRabbitMqTopologyValidator, RabbitMqTopologyValidator>();
        services.AddHostedService<RabbitMqTopologyValidationHostedService>();
        services.AddHostedService<RabbitMqConsumersHostedService>();
        return services;
    }

    public static IServiceCollection AddRabbitConsumer<TMessage, THandler>(
        this IServiceCollection services,
        Action<RabbitConsumerRegistrationBuilder<TMessage>> configure)
        where THandler : class, IRabbitMessageHandler<TMessage>
    {
        services.AddScoped<THandler>();
        services.AddSingleton<IRabbitConsumerRegistration>(_ =>
        {
            return new RabbitConsumerRegistration<TMessage>(builder =>
            {
                configure(builder);
                builder.UseHandler<THandler>();
            });
        });
        return services;
    }

    public static IServiceCollection AddRabbitConsumer<TMessage>(
        this IServiceCollection services,
        Action<RabbitConsumerRegistrationBuilder<TMessage>> configure,
        Func<MessageEnvelope<TMessage>, CancellationToken, Task> handler)
    {
        services.AddSingleton<IRabbitConsumerRegistration>(_ =>
            new RabbitConsumerRegistration<TMessage>(builder =>
            {
                configure(builder);
                builder.Handle(handler);
            }));
        return services;
    }
}
