using SphereRabbitMQ.IaC.Domain.Planning;

namespace SphereRabbitMQ.IaC.Application.Apply.Interfaces;

/// <summary>
/// Applies a previously computed topology plan against the broker.
/// </summary>
public interface ITopologyApplier
{
    /// <summary>
    /// Applies the supplied reconciliation plan.
    /// </summary>
    ValueTask ApplyAsync(TopologyPlan plan, CancellationToken cancellationToken = default);
}
