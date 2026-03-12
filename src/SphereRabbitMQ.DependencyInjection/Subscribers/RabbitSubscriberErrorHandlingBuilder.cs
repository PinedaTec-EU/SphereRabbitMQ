using SphereRabbitMQ.Domain.Subscribers;

namespace SphereRabbitMQ.DependencyInjection.Subscribers;

public sealed class RabbitSubscriberErrorHandlingBuilder
{
    private bool _enableRetry;

    private bool _enableDeadLetter = true;

    public int MaxRetryAttempts { get; set; } = 3;

    public ICollection<Type> NonRetriableExceptions { get; } = new List<Type>();

    public RabbitSubscriberErrorHandlingBuilder UseRetryAndDeadLetter(int maxRetryAttempts = 3)
    {
        _enableRetry = true;
        _enableDeadLetter = true;
        MaxRetryAttempts = maxRetryAttempts;
        return this;
    }

    public RabbitSubscriberErrorHandlingBuilder UseRetryOnly(int maxRetryAttempts = 3)
    {
        _enableRetry = true;
        _enableDeadLetter = false;
        MaxRetryAttempts = maxRetryAttempts;
        return this;
    }

    public RabbitSubscriberErrorHandlingBuilder UseDeadLetterOnly()
    {
        _enableRetry = false;
        _enableDeadLetter = true;
        return this;
    }

    public RabbitSubscriberErrorHandlingBuilder UseDiscard()
    {
        _enableRetry = false;
        _enableDeadLetter = false;
        return this;
    }

    internal SubscriberErrorHandlingSettings Build()
        => new()
        {
            Strategy = ResolveStrategy(),
            MaxRetryAttempts = MaxRetryAttempts,
            NonRetriableExceptions = NonRetriableExceptions.ToArray(),
        };

    private SubscriberErrorStrategyKind ResolveStrategy()
    {
        if (_enableRetry && _enableDeadLetter)
        {
            return SubscriberErrorStrategyKind.RetryThenDeadLetter;
        }

        if (_enableRetry)
        {
            return SubscriberErrorStrategyKind.RetryOnly;
        }

        if (_enableDeadLetter)
        {
            return SubscriberErrorStrategyKind.DeadLetterOnly;
        }

        return SubscriberErrorStrategyKind.Discard;
    }
}
