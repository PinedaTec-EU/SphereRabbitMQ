namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

/// <summary>
/// YAML contract for an exchange definition.
/// </summary>
public sealed record ExchangeYamlDocument
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public bool Durable { get; init; } = true;

    public bool AutoDelete { get; init; }

    public bool Internal { get; init; }

    public Dictionary<string, object?> Arguments { get; init; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);
}
