using SphereRabbitMQ.Application.Retry;
using SphereRabbitMQ.Domain.Consumers;
using SphereRabbitMQ.Domain.Retry;

namespace SphereRabbitMQ.Tests.Unit.Application;

public sealed class DefaultRetryPolicyResolverTests
{
    [Fact]
    public void Resolve_ReturnsRetry_WhenRetryIsEnabledAndAttemptsRemain()
    {
        var resolver = new DefaultRetryPolicyResolver();
        var settings = new ConsumerErrorHandlingSettings
        {
            Strategy = ConsumerErrorStrategyKind.RetryThenDeadLetter,
            MaxRetryAttempts = 5,
            RetryRoute = new RetryRouteDefinition("orders.retry", "orders.created.retry"),
        };

        var decision = resolver.Resolve(settings, new RetryMetadata(1), new InvalidOperationException("boom"));

        Assert.True(decision.ShouldRetry);
        Assert.Equal(2, decision.NextRetryCount);
    }

    [Fact]
    public void Resolve_ReturnsNoRetry_WhenExceptionIsNonRetriable()
    {
        var resolver = new DefaultRetryPolicyResolver();
        var settings = new ConsumerErrorHandlingSettings
        {
            Strategy = ConsumerErrorStrategyKind.RetryOnly,
            MaxRetryAttempts = 5,
            RetryRoute = new RetryRouteDefinition("orders.retry", "orders.created.retry"),
            NonRetriableExceptions = [typeof(ArgumentException)],
        };

        var decision = resolver.Resolve(settings, RetryMetadata.None, new ArgumentException("invalid"));

        Assert.False(decision.ShouldRetry);
        Assert.Equal(0, decision.NextRetryCount);
    }

    [Fact]
    public void Resolve_ReturnsNoRetry_WhenExceptionIsFrameworkNonRetriable()
    {
        var resolver = new DefaultRetryPolicyResolver();
        var settings = new ConsumerErrorHandlingSettings
        {
            Strategy = ConsumerErrorStrategyKind.RetryOnly,
            MaxRetryAttempts = 5,
            RetryRoute = new RetryRouteDefinition("orders.retry", "orders.created.retry"),
        };

        var decision = resolver.Resolve(settings, RetryMetadata.None, new NonRetriableMessageException("business failure"));

        Assert.False(decision.ShouldRetry);
        Assert.Equal(0, decision.NextRetryCount);
    }

    [Fact]
    public void Resolve_ReturnsNoRetry_WhenExceptionIsFrameworkDiscard()
    {
        var resolver = new DefaultRetryPolicyResolver();
        var settings = new ConsumerErrorHandlingSettings
        {
            Strategy = ConsumerErrorStrategyKind.RetryOnly,
            MaxRetryAttempts = 5,
            RetryRoute = new RetryRouteDefinition("orders.retry", "orders.created.retry"),
        };

        var decision = resolver.Resolve(settings, new RetryMetadata(2), new DiscardMessageException("discard"));

        Assert.False(decision.ShouldRetry);
        Assert.Equal(2, decision.NextRetryCount);
    }
}
