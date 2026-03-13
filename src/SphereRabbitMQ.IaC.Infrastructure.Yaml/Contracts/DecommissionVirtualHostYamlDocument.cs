using System.Diagnostics.CodeAnalysis;

namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

/// <summary>
/// YAML contract for virtual-host scoped cleanup directives.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record DecommissionVirtualHostYamlDocument
{
    public required string Name { get; init; }

    public List<string> Exchanges { get; init; } = new();

    public List<string> Queues { get; init; } = new();

    public List<BindingYamlDocument> Bindings { get; init; } = new();
}
