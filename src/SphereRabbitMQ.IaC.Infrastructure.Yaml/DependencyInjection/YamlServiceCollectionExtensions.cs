using Microsoft.Extensions.DependencyInjection;
using SphereRabbitMQ.IaC.Application.Export.Interfaces;
using SphereRabbitMQ.IaC.Application.Parsing.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.Yaml.Export;
using SphereRabbitMQ.IaC.Infrastructure.Yaml.Parsing;

namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.DependencyInjection;

/// <summary>
/// Dependency injection extensions for YAML infrastructure services.
/// </summary>
public static class YamlServiceCollectionExtensions
{
    /// <summary>
    /// Registers YAML parsing and writing services.
    /// </summary>
    public static IServiceCollection AddYamlInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ITopologyParser, TopologyYamlParser>();
        services.AddSingleton<ITopologyDocumentWriter, TopologyYamlDocumentWriter>();
        return services;
    }
}
