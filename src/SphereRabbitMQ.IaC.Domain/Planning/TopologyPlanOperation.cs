using SphereRabbitMQ.IaC.Domain.Internal;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Domain.Planning;

/// <summary>
/// Represents a single auditable operation within a topology execution plan.
/// </summary>
public sealed record TopologyPlanOperation
{
    public TopologyPlanOperation(
        TopologyPlanOperationKind kind,
        TopologyResourceKind resourceKind,
        string resourcePath,
        string description,
        IReadOnlyList<TopologyDiff>? diffs = null,
        IReadOnlyList<TopologyIssue>? issues = null)
    {
        Kind = kind;
        ResourceKind = resourceKind;
        ResourcePath = Guard.AgainstNullOrWhiteSpace(resourcePath, nameof(resourcePath));
        Description = Guard.AgainstNullOrWhiteSpace(description, nameof(description));
        Diffs = diffs ?? Array.Empty<TopologyDiff>();
        Issues = issues ?? Array.Empty<TopologyIssue>();
    }

    public TopologyPlanOperationKind Kind { get; }

    public TopologyResourceKind ResourceKind { get; }

    public string ResourcePath { get; }

    public string Description { get; }

    public IReadOnlyList<TopologyDiff> Diffs { get; }

    public IReadOnlyList<TopologyIssue> Issues { get; }
}
