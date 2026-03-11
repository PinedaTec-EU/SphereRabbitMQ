using System.Diagnostics.CodeAnalysis;

namespace SphereRabbitMQ.IaC.Application.Models;

/// <summary>
/// Source-neutral topology document used between parsing and normalization.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record TopologyDocument
{
    public BrokerDocument? Broker { get; init; }

    public IReadOnlyList<VirtualHostDocument> VirtualHosts { get; init; } = Array.Empty<VirtualHostDocument>();

    public DebugQueuesDocument? DebugQueues { get; init; }

    public NamingConventionDocument? Naming { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
