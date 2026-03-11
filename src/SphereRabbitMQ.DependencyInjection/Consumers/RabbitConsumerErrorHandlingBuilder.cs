using SphereRabbitMQ.Domain.Consumers;

namespace SphereRabbitMQ.DependencyInjection.Consumers;

public sealed class RabbitConsumerErrorHandlingBuilder
{
    public bool EnableRetry { get; set; }

    public bool EnableDeadLetter { get; set; } = true;

    public int MaxRetryAttempts { get; set; } = 3;

    public string? RetryExchange { get; set; }

    public string? RetryRoutingKey { get; set; }

    public string? RetryQueue { get; set; }

    public string? DeadLetterExchange { get; set; }

    public string? DeadLetterRoutingKey { get; set; }

    public string? DeadLetterQueue { get; set; }

    public ICollection<Type> NonRetriableExceptions { get; } = new List<Type>();

    internal ConsumerErrorHandlingSettings Build(string fallbackExchange, string fallbackRoutingKey)
        => new()
        {
            Strategy = ResolveStrategy(),
            MaxRetryAttempts = MaxRetryAttempts,
            RetryRoute = EnableRetry ? new RetryRouteDefinition(RetryExchange ?? fallbackExchange, RetryRoutingKey ?? fallbackRoutingKey, RetryQueue ?? RetryRoutingKey ?? fallbackRoutingKey) : null,
            DeadLetterRoute = EnableDeadLetter ? new DeadLetterRouteDefinition(DeadLetterExchange ?? fallbackExchange, DeadLetterRoutingKey ?? fallbackRoutingKey, DeadLetterQueue ?? DeadLetterRoutingKey ?? fallbackRoutingKey) : null,
            NonRetriableExceptions = NonRetriableExceptions.ToArray(),
        };

    private ConsumerErrorStrategyKind ResolveStrategy()
    {
        if (EnableRetry && EnableDeadLetter)
        {
            return ConsumerErrorStrategyKind.RetryThenDeadLetter;
        }

        if (EnableRetry)
        {
            return ConsumerErrorStrategyKind.RetryOnly;
        }

        if (EnableDeadLetter)
        {
            return ConsumerErrorStrategyKind.DeadLetterOnly;
        }

        return ConsumerErrorStrategyKind.Discard;
    }
}
