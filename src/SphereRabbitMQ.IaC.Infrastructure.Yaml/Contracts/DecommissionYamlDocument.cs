using System.Diagnostics.CodeAnalysis;

namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

/// <summary>
/// YAML contract for explicit cleanup directives.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record DecommissionYamlDocument
{
    public List<DecommissionVirtualHostYamlDocument> VirtualHosts { get; init; } = new();
}
