namespace SphereRabbitMQ.Domain.Subscribers;

public sealed record SubscriberErrorHandlingSettings
{
    public SubscriberErrorStrategyKind Strategy { get; init; } = SubscriberErrorStrategyKind.DeadLetterOnly;

    public int MaxRetryAttempts { get; init; } = 3;

    public RetryRouteDefinition? RetryRoute { get; init; }

    public DeadLetterRouteDefinition? DeadLetterRoute { get; init; }

    public IReadOnlyCollection<Type> NonRetriableExceptions { get; init; } = Array.Empty<Type>();
}
