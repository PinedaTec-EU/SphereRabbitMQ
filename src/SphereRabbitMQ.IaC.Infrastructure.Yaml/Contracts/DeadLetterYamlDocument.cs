using System.Diagnostics.CodeAnalysis;

namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

/// <summary>
/// YAML contract for dead-letter configuration.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record DeadLetterYamlDocument
{
    public bool Enabled { get; init; } = true;

    public string DestinationType { get; init; } = "generated";

    public string? ExchangeName { get; init; }

    public string? QueueName { get; init; }

    public string? RoutingKey { get; init; }

    public string? Ttl { get; init; }
}
