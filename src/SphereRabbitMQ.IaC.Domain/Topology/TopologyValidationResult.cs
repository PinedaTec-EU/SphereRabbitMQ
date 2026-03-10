namespace SphereRabbitMQ.IaC.Domain.Topology;

/// <summary>
/// Contains the result of validating a topology definition.
/// </summary>
public sealed record TopologyValidationResult
{
    public static TopologyValidationResult Success { get; } = new(Array.Empty<TopologyIssue>());

    public TopologyValidationResult(IReadOnlyList<TopologyIssue> issues)
    {
        Issues = issues;
    }

    public IReadOnlyList<TopologyIssue> Issues { get; }

    public bool IsValid => Issues.All(issue => issue.Severity != TopologyIssueSeverity.Error);
}
