using SphereRabbitMQ.Domain.Messaging;

namespace SphereRabbitMQ.Tests.Unit.Domain;

public sealed class MessageEnvelopeTests
{
    [Fact]
    public void Constructor_PreservesMetadata()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var headers = new Dictionary<string, object?> { ["tenant-id"] = "sales" };

        var envelope = new MessageEnvelope<string>("body", headers, "orders.created", "orders", "message-1", "correlation-1", timestamp);

        Assert.Equal("body", envelope.Body);
        Assert.Equal("orders.created", envelope.RoutingKey);
        Assert.Equal("orders", envelope.Exchange);
        Assert.Equal("message-1", envelope.MessageId);
        Assert.Equal("correlation-1", envelope.CorrelationId);
        Assert.Equal(timestamp, envelope.Timestamp);
        Assert.Equal("sales", envelope.Headers["tenant-id"]);
    }
}
