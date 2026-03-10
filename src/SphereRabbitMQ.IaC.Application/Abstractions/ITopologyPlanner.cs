using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Application.Abstractions;

/// <summary>
/// Produces a reconciliation plan between desired and actual topology.
/// </summary>
public interface ITopologyPlanner
{
    /// <summary>
    /// Produces an auditable plan that reconciles the broker to the desired topology.
    /// </summary>
    ValueTask<TopologyPlan> PlanAsync(
        TopologyDefinition desired,
        TopologyDefinition actual,
        CancellationToken cancellationToken = default);
}
