using System.Diagnostics.CodeAnalysis;

namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

/// <summary>
/// YAML contract for a RabbitMQ virtual host.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record VirtualHostYamlDocument
{
    public required string Name { get; init; }

    public List<ExchangeYamlDocument> Exchanges { get; init; } = new();

    public List<QueueYamlDocument> Queues { get; init; } = new();

    public List<BindingYamlDocument> Bindings { get; init; } = new();

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);
}
