using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Domain.Planning;

/// <summary>
/// Describes a change that cannot be applied safely by the tool.
/// </summary>
public sealed record UnsupportedChange(string ResourcePath, string Reason)
    : TopologyIssue("unsupported-change", Reason, ResourcePath, TopologyIssueSeverity.Error);
