namespace SphereRabbitMQ.IaC.Domain.Planning;

/// <summary>
/// Identifies the infrastructure resource affected by a plan item.
/// </summary>
public enum TopologyResourceKind
{
    VirtualHost,
    Exchange,
    Queue,
    Binding,
}
