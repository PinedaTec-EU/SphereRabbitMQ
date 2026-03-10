using Microsoft.Extensions.DependencyInjection;
using SphereRabbitMQ.IaC.Cli.Commands;
using SphereRabbitMQ.IaC.Cli.Commands.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.DependencyInjection;
using SphereRabbitMQ.IaC.Infrastructure.Yaml.DependencyInjection;

namespace SphereRabbitMQ.IaC.Cli.DependencyInjection;

/// <summary>
/// Dependency injection extensions for CLI composition.
/// </summary>
public static class CliServiceCollectionExtensions
{
    /// <summary>
    /// Registers CLI services and dependencies.
    /// </summary>
    public static IServiceCollection AddTopologyCli(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddYamlInfrastructure();
        services.AddRabbitMqInfrastructure();
        services.AddSingleton<ICommandOutputWriter, CommandOutputWriter>();
        services.AddSingleton<TopologyCommandHandler>();
        return services;
    }
}
