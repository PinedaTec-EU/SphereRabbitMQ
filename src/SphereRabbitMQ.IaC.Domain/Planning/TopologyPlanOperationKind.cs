namespace SphereRabbitMQ.IaC.Domain.Planning;

/// <summary>
/// Operation categories emitted by the topology planner.
/// </summary>
public enum TopologyPlanOperationKind
{
    Create,
    Update,
    Destroy,
    NoOp,
    DestructiveChange,
    UnsupportedChange,
}
