namespace SphereRabbitMQ.IaC.Application.Models;

/// <summary>
/// Source-neutral topology document used between parsing and normalization.
/// </summary>
public sealed record TopologyDocument
{
    public IReadOnlyList<VirtualHostDocument> VirtualHosts { get; init; } = Array.Empty<VirtualHostDocument>();

    public NamingConventionDocument? Naming { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
