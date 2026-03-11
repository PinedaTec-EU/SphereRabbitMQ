namespace SphereRabbitMQ.IaC.Application.Models;

/// <summary>
/// Source-neutral exchange document.
/// </summary>
public sealed record ExchangeDocument
{
    public required string Name { get; init; }

    public string Type { get; init; } = "topic";

    public bool Durable { get; init; } = true;

    public bool AutoDelete { get; init; }

    public bool Internal { get; init; }

    public IReadOnlyDictionary<string, object?> Arguments { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
