namespace SphereRabbitMQ.Domain.Consumers;

public sealed record ConsumerErrorHandlingSettings
{
    public ConsumerErrorStrategyKind Strategy { get; init; } = ConsumerErrorStrategyKind.DeadLetterOnly;

    public int MaxRetryAttempts { get; init; } = 3;

    public RetryRouteDefinition? RetryRoute { get; init; }

    public DeadLetterRouteDefinition? DeadLetterRoute { get; init; }

    public IReadOnlyCollection<Type> NonRetriableExceptions { get; init; } = Array.Empty<Type>();
}
