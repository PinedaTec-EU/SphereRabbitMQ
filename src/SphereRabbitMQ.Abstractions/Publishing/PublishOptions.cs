namespace SphereRabbitMQ.Abstractions.Publishing;

public sealed record PublishOptions
{
    public IReadOnlyDictionary<string, object?> Headers { get; init; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    public string? CorrelationId { get; init; }

    public string? MessageId { get; init; }

    public bool Persistent { get; init; } = true;

    public DateTimeOffset? Timestamp { get; init; }

    public TimeSpan? TimeToLive { get; init; }

    /// <summary>
    /// AMQP message priority (0–255). Used by priority queues (<c>x-max-priority</c>).
    /// When <see langword="null"/> (default) the broker treats the message as priority 0.
    /// </summary>
    public byte? Priority { get; init; }
}
