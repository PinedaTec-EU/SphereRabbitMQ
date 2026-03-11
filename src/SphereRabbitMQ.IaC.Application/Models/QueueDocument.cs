namespace SphereRabbitMQ.IaC.Application.Models;

/// <summary>
/// Source-neutral queue document.
/// </summary>
public sealed record QueueDocument
{
    public required string Name { get; init; }

    public string Type { get; init; } = "classic";

    public bool Durable { get; init; } = true;

    public bool Exclusive { get; init; }

    public bool AutoDelete { get; init; }

    public string? Ttl { get; init; }

    public IReadOnlyDictionary<string, object?> Arguments { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public DeadLetterDocument? DeadLetter { get; init; }

    public RetryDocument? Retry { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
