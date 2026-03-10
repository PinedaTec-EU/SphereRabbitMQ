using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Domain.Planning;

/// <summary>
/// Describes a potentially destructive mutation that must be surfaced explicitly.
/// </summary>
public sealed record DestructiveChangeWarning(string ResourcePath, string Reason)
    : TopologyIssue("destructive-change", Reason, ResourcePath, TopologyIssueSeverity.Warning);
