using SphereRabbitMQ.Application.Retry;
using SphereRabbitMQ.Domain.Messaging;
using SphereRabbitMQ.Domain.Retry;

namespace SphereRabbitMQ.Tests.Unit.Application;

public sealed class DefaultSubscriberRetryDelayResolverTests
{
    [Fact]
    public void Resolve_ReturnsLinearDelay_BasedOnAttemptNumber()
    {
        var resolver = new DefaultSubscriberRetryDelayResolver<string>();
        var context = new SubscriberRetryDelayContext<string>(
            "orders.created",
            new MessageEnvelope<string>("body", new Dictionary<string, object?>(), "orders.created", "orders", "message-1", "corr-1", DateTimeOffset.UtcNow),
            new InvalidOperationException("boom"),
            3);

        var delay = resolver.Resolve(context);

        Assert.Equal(TimeSpan.FromMilliseconds(750), delay);
    }
}
