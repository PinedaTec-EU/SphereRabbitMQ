namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

/// <summary>
/// YAML contract for generated debug queues.
/// </summary>
public sealed record DebugQueuesYamlDocument
{
    public bool Enabled { get; init; }

    public string QueueSuffix { get; init; } = "debug";
}
