namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

/// <summary>
/// YAML contract for selecting main vs secondary debug targets.
/// </summary>
public sealed record DebugQueueScopeYamlDocument
{
    public bool Main { get; init; }

    public bool Secondary { get; init; }
}
