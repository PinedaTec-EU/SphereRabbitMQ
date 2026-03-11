using System.Diagnostics.CodeAnalysis;

namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

/// <summary>
/// YAML contract root for topology definitions.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record TopologyYamlDocument
{
    public BrokerYamlDocument? Broker { get; init; }

    public List<VirtualHostYamlDocument> VirtualHosts { get; init; } = new();

    public DebugQueuesYamlDocument? DebugQueues { get; init; }

    public NamingConventionYamlDocument? Naming { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);

    public Dictionary<string, string?> Variables { get; init; } = new(StringComparer.Ordinal);
}
