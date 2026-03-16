using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.DependencyInjection;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Startup;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Startup.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.Yaml.DependencyInjection;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.DependencyInjection;

/// <summary>
/// Composition helpers for applying topology YAML during host startup.
/// </summary>
public static class RabbitMqTopologyInitializationServiceCollectionExtensions
{
    private const string TopologyValidationHostedServiceFullName = "SphereRabbitMQ.DependencyInjection.Subscribers.RabbitMqTopologyValidationHostedService";
    private const string SubscribersHostedServiceFullName = "SphereRabbitMQ.DependencyInjection.Subscribers.RabbitMqSubscribersHostedService";

    /// <summary>
    /// Registers SphereRabbitMQ and enables startup topology initialization from a YAML file.
    /// </summary>
    public static IServiceCollection AddSphereRabbitMqWithTopologyInitialization(
        this IServiceCollection services,
        Action<SphereRabbitMqOptions>? configureRabbitMq = null,
        Action<RabbitMqTopologyInitializationOptions>? configureTopologyInitialization = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSphereRabbitMq(configureRabbitMq);
        return services.AddSphereRabbitMqTopologyInitialization(configureTopologyInitialization);
    }

    /// <summary>
    /// Adds YAML-driven topology initialization to an existing SphereRabbitMQ host setup.
    /// Call this after <c>AddSphereRabbitMq</c> so topology initialization runs before subscriber startup.
    /// </summary>
    public static IServiceCollection AddSphereRabbitMqTopologyInitialization(
        this IServiceCollection services,
        Action<RabbitMqTopologyInitializationOptions>? configureTopologyInitialization = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<RabbitMqTopologyInitializationOptions>();
        if (configureTopologyInitialization is not null)
        {
            services.Configure(configureTopologyInitialization);
        }

        services.AddYamlInfrastructure();
        services.AddRabbitMqInfrastructure();
        services.AddSingleton<IRuntimeTopologyYamlContractValidator, RuntimeTopologyYamlContractValidator>();

        var hostedServicesToMove = ExtractRuntimeHostedServices(services);

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(RabbitMqTopologyInitializationHostedService)))
        {
            services.AddHostedService<RabbitMqTopologyInitializationHostedService>();
        }

        foreach (var hostedService in hostedServicesToMove)
        {
            services.Add(hostedService);
        }

        return services;
    }

    private static IReadOnlyList<ServiceDescriptor> ExtractRuntimeHostedServices(IServiceCollection services)
    {
        var hostedServicesToMove = services
            .Select((descriptor, index) => new { Descriptor = descriptor, Index = index })
            .Where(entry =>
                entry.Descriptor.ServiceType == typeof(IHostedService) &&
                entry.Descriptor.ImplementationType is not null &&
                (entry.Descriptor.ImplementationType.FullName == TopologyValidationHostedServiceFullName ||
                 entry.Descriptor.ImplementationType.FullName == SubscribersHostedServiceFullName))
            .OrderBy(entry => entry.Index)
            .ToArray();

        for (var index = hostedServicesToMove.Length - 1; index >= 0; index--)
        {
            services.RemoveAt(hostedServicesToMove[index].Index);
        }

        return hostedServicesToMove.Select(entry => entry.Descriptor).ToArray();
    }
}
