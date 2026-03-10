using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Application.Broker.Interfaces;

/// <summary>
/// Reads the current broker topology state and returns a normalized representation.
/// </summary>
public interface IBrokerTopologyReader
{
    /// <summary>
    /// Reads the current broker topology.
    /// </summary>
    ValueTask<TopologyDefinition> ReadAsync(CancellationToken cancellationToken = default);
}
