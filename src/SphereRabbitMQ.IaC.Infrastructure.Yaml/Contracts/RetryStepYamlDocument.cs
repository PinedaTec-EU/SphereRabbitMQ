using System.Diagnostics.CodeAnalysis;

namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

/// <summary>
/// YAML contract for a single retry step.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record RetryStepYamlDocument
{
    public required string Delay { get; init; }

    public string? Name { get; init; }

    public string? QueueName { get; init; }

    public string? RoutingKey { get; init; }
}
