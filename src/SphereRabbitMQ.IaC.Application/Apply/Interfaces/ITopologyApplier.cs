using SphereRabbitMQ.IaC.Application.Apply;
using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Application.Apply.Interfaces;

/// <summary>
/// Applies a previously computed topology plan against the broker.
/// </summary>
public interface ITopologyApplier
{
    /// <summary>
    /// Applies the supplied reconciliation plan.
    /// </summary>
    ValueTask ApplyAsync(
        TopologyDefinition desired,
        TopologyPlan plan,
        TopologyApplyOptions? options = null,
        CancellationToken cancellationToken = default);
}
