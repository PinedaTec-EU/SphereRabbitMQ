namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

/// <summary>
/// YAML contract root for topology definitions.
/// </summary>
public sealed record TopologyYamlDocument
{
    public List<VirtualHostYamlDocument> VirtualHosts { get; init; } = new();

    public NamingConventionYamlDocument? Naming { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);

    public Dictionary<string, string?> Variables { get; init; } = new(StringComparer.Ordinal);
}
