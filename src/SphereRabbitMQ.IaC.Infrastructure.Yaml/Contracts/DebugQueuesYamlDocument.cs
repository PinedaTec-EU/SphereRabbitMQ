namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

/// <summary>
/// YAML contract for generated debug queues.
/// </summary>
public sealed record DebugQueuesYamlDocument
{
    public bool Enabled { get; init; }

    public string QueueSuffix { get; init; } = "debug";

    public DebugQueueScopeYamlDocument Exchanges { get; init; } = new()
    {
        Main = true,
        Secondary = false,
    };

    public DebugQueueScopeYamlDocument Queues { get; init; } = new()
    {
        Main = false,
        Secondary = false,
    };
}
