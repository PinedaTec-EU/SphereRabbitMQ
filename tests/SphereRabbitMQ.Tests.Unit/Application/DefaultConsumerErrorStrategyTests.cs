using SphereRabbitMQ.Application.Consumers;
using SphereRabbitMQ.Domain.Consumers;
using SphereRabbitMQ.Domain.Messaging;
using SphereRabbitMQ.Domain.Retry;

namespace SphereRabbitMQ.Tests.Unit.Application;

public sealed class DefaultConsumerErrorStrategyTests
{
    [Fact]
    public void Resolve_ReturnsDeadLetter_WhenRetryIsExhausted()
    {
        var strategy = new DefaultConsumerErrorStrategy();
        var envelope = new MessageEnvelope<string>("payload", new Dictionary<string, object?>(), "orders.created", "orders", "id-1", "corr-1", DateTimeOffset.UtcNow);
        var settings = new ConsumerErrorHandlingSettings
        {
            Strategy = ConsumerErrorStrategyKind.RetryThenDeadLetter,
            RetryRoute = new RetryRouteDefinition("orders.retry", "orders.created.retry"),
            DeadLetterRoute = new DeadLetterRouteDefinition("orders.dlx", "orders.created.dlq"),
        };

        var decision = strategy.Resolve(envelope, settings, new InvalidOperationException("boom"), new RetryDecision(false, 5));

        Assert.Equal(ConsumerFailureDisposition.DeadLetter, decision.Disposition);
        Assert.Equal("orders.dlx", decision.DeadLetterRoute?.Exchange);
    }

    [Fact]
    public void Resolve_ReturnsDiscard_WhenExceptionIsFrameworkDiscard()
    {
        var strategy = new DefaultConsumerErrorStrategy();
        var envelope = new MessageEnvelope<string>("payload", new Dictionary<string, object?>(), "orders.created", "orders", "id-1", "corr-1", DateTimeOffset.UtcNow);
        var settings = new ConsumerErrorHandlingSettings
        {
            Strategy = ConsumerErrorStrategyKind.RetryThenDeadLetter,
            RetryRoute = new RetryRouteDefinition("orders.retry", "orders.created.retry"),
            DeadLetterRoute = new DeadLetterRouteDefinition("orders.dlx", "orders.created.dlq"),
        };

        var decision = strategy.Resolve(envelope, settings, new DiscardMessageException("discard"), new RetryDecision(false, 1));

        Assert.Equal(ConsumerFailureDisposition.Discard, decision.Disposition);
    }
}
