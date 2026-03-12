using SphereRabbitMQ.Application.Retry;
using SphereRabbitMQ.Domain.Subscribers;
using SphereRabbitMQ.Domain.Retry;

namespace SphereRabbitMQ.Tests.Unit.Application;

public sealed class DefaultRetryPolicyResolverTests
{
    [Fact]
    public void Resolve_ReturnsRetry_WhenRetryIsEnabledAndAttemptsRemain()
    {
        var resolver = new DefaultRetryPolicyResolver();
        var settings = new SubscriberErrorHandlingSettings
        {
            Strategy = SubscriberErrorStrategyKind.RetryThenDeadLetter,
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
        var settings = new SubscriberErrorHandlingSettings
        {
            Strategy = SubscriberErrorStrategyKind.RetryThenDeadLetter,
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
        var settings = new SubscriberErrorHandlingSettings
        {
            Strategy = SubscriberErrorStrategyKind.RetryThenDeadLetter,
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
        var settings = new SubscriberErrorHandlingSettings
        {
            Strategy = SubscriberErrorStrategyKind.RetryThenDeadLetter,
            MaxRetryAttempts = 5,
            RetryRoute = new RetryRouteDefinition("orders.retry", "orders.created.retry"),
        };

        var decision = resolver.Resolve(settings, new RetryMetadata(2), new DiscardMessageException("discard"));

        Assert.False(decision.ShouldRetry);
        Assert.Equal(2, decision.NextRetryCount);
    }

    [Fact]
    public void Resolve_ReturnsNoRetry_WhenRetryRouteIsMissing()
    {
        var resolver = new DefaultRetryPolicyResolver();
        var settings = new SubscriberErrorHandlingSettings
        {
            Strategy = SubscriberErrorStrategyKind.RetryThenDeadLetter,
            MaxRetryAttempts = 5,
            RetryRoute = null,
        };

        var decision = resolver.Resolve(settings, RetryMetadata.None, new InvalidOperationException("boom"));

        Assert.False(decision.ShouldRetry);
        Assert.Equal(1, decision.NextRetryCount);
    }

    [Fact]
    public void Resolve_ReturnsNoRetry_WhenMaxAttemptsIsReached()
    {
        var resolver = new DefaultRetryPolicyResolver();
        var settings = new SubscriberErrorHandlingSettings
        {
            Strategy = SubscriberErrorStrategyKind.RetryThenDeadLetter,
            MaxRetryAttempts = 2,
            RetryRoute = new RetryRouteDefinition("orders.retry", "orders.created.retry"),
        };

        var decision = resolver.Resolve(settings, new RetryMetadata(2), new InvalidOperationException("boom"));

        Assert.False(decision.ShouldRetry);
        Assert.Equal(3, decision.NextRetryCount);
    }

    [Theory]
    [InlineData(SubscriberErrorStrategyKind.DeadLetterOnly)]
    [InlineData(SubscriberErrorStrategyKind.Discard)]
    public void Resolve_ReturnsNoRetry_WhenStrategyDoesNotAllowRetry(SubscriberErrorStrategyKind strategy)
    {
        var resolver = new DefaultRetryPolicyResolver();
        var settings = new SubscriberErrorHandlingSettings
        {
            Strategy = strategy,
            MaxRetryAttempts = 5,
            RetryRoute = new RetryRouteDefinition("orders.retry", "orders.created.retry"),
        };

        var decision = resolver.Resolve(settings, RetryMetadata.None, new InvalidOperationException("boom"));

        Assert.False(decision.ShouldRetry);
        Assert.Equal(1, decision.NextRetryCount);
    }
}
