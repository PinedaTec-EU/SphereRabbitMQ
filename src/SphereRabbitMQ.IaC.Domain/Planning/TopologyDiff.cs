using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Domain.Planning;

/// <summary>
/// Represents a single normalized difference between desired and actual broker state.
/// </summary>
public sealed record TopologyDiff(
    string ResourcePath,
    TopologyResourceKind ResourceKind,
    string PropertyName,
    string? DesiredValue,
    string? ActualValue);
