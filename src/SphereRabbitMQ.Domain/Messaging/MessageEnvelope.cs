namespace SphereRabbitMQ.Domain.Messaging;

/// <summary>
/// Immutable typed message envelope exposed to publishers and subscribers.
/// </summary>
public sealed record MessageEnvelope<TMessage>(
    TMessage Body,
    IReadOnlyDictionary<string, object?> Headers,
    string RoutingKey,
    string Exchange,
    string? MessageId,
    string? CorrelationId,
    DateTimeOffset? Timestamp);
