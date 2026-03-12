using SphereRabbitMQ.Domain.Subscribers;

namespace SphereRabbitMQ.DependencyInjection.Subscribers;

public sealed class RabbitSubscriberErrorHandlingBuilder
{
    private SubscriberErrorStrategyKind _strategy = SubscriberErrorStrategyKind.DeadLetterOnly;

    public int MaxRetryAttempts { get; set; } = 3;

    public ICollection<Type> NonRetriableExceptions { get; } = new List<Type>();

    public RabbitSubscriberErrorHandlingBuilder UseRetryAndDeadLetter(int maxRetryAttempts = 3)
    {
        _strategy = SubscriberErrorStrategyKind.RetryThenDeadLetter;
        MaxRetryAttempts = maxRetryAttempts;
        return this;
    }

    public RabbitSubscriberErrorHandlingBuilder UseDeadLetterOnly()
    {
        _strategy = SubscriberErrorStrategyKind.DeadLetterOnly;
        return this;
    }

    public RabbitSubscriberErrorHandlingBuilder UseDiscard()
    {
        _strategy = SubscriberErrorStrategyKind.Discard;
        return this;
    }

    internal SubscriberErrorHandlingSettings Build()
        => new()
        {
            Strategy = _strategy,
            MaxRetryAttempts = MaxRetryAttempts,
            NonRetriableExceptions = NonRetriableExceptions.ToArray(),
        };
}
