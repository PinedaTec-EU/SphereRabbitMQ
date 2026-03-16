namespace SphereRabbitMQ.IaC.Application.Models;

/// <summary>
/// Source-neutral document describing generated debug queues for exchanges.
/// </summary>
public sealed record DebugQueuesDocument
{
    public bool Enabled { get; init; }

    public string QueueSuffix { get; init; } = "debug";
}
