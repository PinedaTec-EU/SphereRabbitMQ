using Microsoft.Extensions.DependencyInjection;
using SphereRabbitMQ.IaC.Application.Normalization;
using SphereRabbitMQ.IaC.Application.Normalization.Interfaces;
using SphereRabbitMQ.IaC.Application.Planning;
using SphereRabbitMQ.IaC.Application.Planning.Interfaces;
using SphereRabbitMQ.IaC.Application.Validation;
using SphereRabbitMQ.IaC.Application.Validation.Interfaces;
using SphereRabbitMQ.IaC.Application.Variables;
using SphereRabbitMQ.IaC.Application.Variables.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime.Interfaces;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.DependencyInjection;

/// <summary>
/// Dependency injection extensions for shared RabbitMQ IaC services.
/// </summary>
public static class RabbitMqServiceCollectionExtensions
{
    /// <summary>
    /// Registers shared non-connection-bound services used by RabbitMQ workflows.
    /// </summary>
    public static IServiceCollection AddRabbitMqInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IVariableResolver, EnvironmentVariableResolver>();
        services.AddSingleton<ITopologyNormalizer, TopologyNormalizationService>();
        services.AddSingleton<ITopologyValidator, TopologyValidationService>();
        services.AddSingleton<ITopologyPlanner, TopologyPlannerService>();
        services.AddSingleton<IRabbitMqRuntimeServiceFactory, RabbitMqRuntimeServiceFactory>();
        return services;
    }
}
