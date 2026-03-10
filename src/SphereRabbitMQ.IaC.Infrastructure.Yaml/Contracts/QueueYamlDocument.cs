namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

/// <summary>
/// YAML contract for a queue definition.
/// </summary>
public sealed record QueueYamlDocument
{
    public required string Name { get; init; }

    public string Type { get; init; } = "classic";

    public bool Durable { get; init; } = true;

    public bool Exclusive { get; init; }

    public bool AutoDelete { get; init; }

    public Dictionary<string, object?> Arguments { get; init; } = new(StringComparer.Ordinal);

    public DeadLetterYamlDocument? DeadLetter { get; init; }

    public RetryYamlDocument? Retry { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);
}
