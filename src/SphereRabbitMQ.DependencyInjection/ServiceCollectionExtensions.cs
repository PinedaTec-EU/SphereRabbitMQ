using Microsoft.Extensions.DependencyInjection;
using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.Abstractions.Subscribers;
using SphereRabbitMQ.Abstractions.Publishing;
using SphereRabbitMQ.Abstractions.Serialization;
using SphereRabbitMQ.Abstractions.Topology;
using SphereRabbitMQ.Application.Subscribers;
using SphereRabbitMQ.Application.Retry;
using SphereRabbitMQ.Application.Serialization;
using SphereRabbitMQ.DependencyInjection.Subscribers;
using SphereRabbitMQ.Domain.Messaging;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Connection;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Migration;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Migration.Interfaces;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Subscribers;
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
        services.AddSingleton<IRabbitMQPublisher, RabbitMqPublisher>();
        services.AddSingleton<IRabbitMQSubscriber, RabbitMqSubscriber>();
        services.AddSingleton<ISubscriberErrorStrategy, DefaultSubscriberErrorStrategy>();
        services.AddSingleton<ISubscriberInfrastructureRouteResolver, DefaultSubscriberInfrastructureRouteResolver>();
        services.AddSingleton<ISubscriberTopologyExpectationProvider, DefaultSubscriberTopologyExpectationProvider>();
        services.AddSingleton<IRetryPolicyResolver, DefaultRetryPolicyResolver>();
        services.AddSingleton<IRabbitMqTopologyValidator, RabbitMqTopologyValidator>();
        services.AddHostedService<RabbitMqTopologyValidationHostedService>();
        services.AddHostedService<RabbitMqSubscribersHostedService>();
        return services;
    }

    public static IServiceCollection AddRabbitSubscriber<TMessage, THandler>(
        this IServiceCollection services,
        Action<RabbitSubscriberRegistrationBuilder<TMessage>> configure)
        where THandler : class, IRabbitSubscriberMessageHandler<TMessage>
    {
        services.AddScoped<THandler>();
        services.AddSingleton<IRabbitSubscriberRegistration>(_ =>
        {
            return new RabbitSubscriberRegistration<TMessage>(builder =>
            {
                configure(builder);
                builder.UseHandler<THandler>();
            });
        });
        return services;
    }

    public static IServiceCollection AddRabbitSubscriber<TMessage>(
        this IServiceCollection services,
        Action<RabbitSubscriberRegistrationBuilder<TMessage>> configure,
        Func<MessageEnvelope<TMessage>, CancellationToken, Task> handler)
    {
        services.AddSingleton<IRabbitSubscriberRegistration>(_ =>
            new RabbitSubscriberRegistration<TMessage>(builder =>
            {
                configure(builder);
                builder.Handle(handler);
            }));
        return services;
    }
}
