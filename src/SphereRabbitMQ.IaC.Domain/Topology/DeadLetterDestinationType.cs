namespace SphereRabbitMQ.IaC.Domain.Topology;

/// <summary>
/// Describes how a queue dead-letter target should be resolved.
/// </summary>
public enum DeadLetterDestinationType
{
    Generated,
    Queue,
}
