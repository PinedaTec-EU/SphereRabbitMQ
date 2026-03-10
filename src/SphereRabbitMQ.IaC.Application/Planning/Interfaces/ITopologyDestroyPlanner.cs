using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Application.Planning.Interfaces;

/// <summary>
/// Produces a destructive plan that removes declared topology from the broker.
/// </summary>
public interface ITopologyDestroyPlanner
{
    /// <summary>
    /// Produces an auditable destroy plan for the declared topology.
    /// </summary>
    ValueTask<TopologyPlan> PlanAsync(
        TopologyDefinition desired,
        TopologyDefinition actual,
        bool destroyVirtualHosts,
        CancellationToken cancellationToken = default);
}
