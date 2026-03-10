namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

/// <summary>
/// YAML contract for a single retry step.
/// </summary>
public sealed record RetryStepYamlDocument
{
    public required string Delay { get; init; }

    public string? Name { get; init; }

    public string? QueueName { get; init; }

    public string? RoutingKey { get; init; }
}
