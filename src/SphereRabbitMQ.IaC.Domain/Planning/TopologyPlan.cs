using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Domain.Planning;

/// <summary>
/// Represents the full execution plan required to reconcile broker topology.
/// </summary>
public sealed record TopologyPlan
{
    public TopologyPlan(
        IReadOnlyList<TopologyPlanOperation> operations,
        IReadOnlyList<UnsupportedChange>? unsupportedChanges = null,
        IReadOnlyList<DestructiveChangeWarning>? destructiveChanges = null)
    {
        Operations = operations;
        UnsupportedChanges = unsupportedChanges ?? Array.Empty<UnsupportedChange>();
        DestructiveChanges = destructiveChanges ?? Array.Empty<DestructiveChangeWarning>();
    }

    public IReadOnlyList<TopologyPlanOperation> Operations { get; }

    public IReadOnlyList<UnsupportedChange> UnsupportedChanges { get; }

    public IReadOnlyList<DestructiveChangeWarning> DestructiveChanges { get; }

    public bool CanApply => UnsupportedChanges.Count == 0;
}
