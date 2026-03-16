namespace SphereRabbitMQ.Abstractions.Publishing;

public sealed record PublishOptions
{
    public IReadOnlyDictionary<string, object?> Headers { get; init; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    public string? CorrelationId { get; init; }

    public string? MessageId { get; init; }

    public bool Persistent { get; init; } = true;

    public DateTimeOffset? Timestamp { get; init; }

    public TimeSpan? TimeToLive { get; init; }
}
