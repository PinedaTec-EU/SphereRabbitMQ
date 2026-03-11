using SphereRabbitMQ.Domain.Routing;

namespace SphereRabbitMQ.Tests.Unit.Domain;

public sealed class RoutingKeyBuilderTests
{
    [Fact]
    public void Build_ReturnsDotSeparatedRoutingKey()
    {
        var builder = new RoutingKeyBuilder();

        var routingKey = builder.Build("orders", "created", "v1");

        Assert.Equal("orders.created.v1", routingKey);
    }

    [Fact]
    public void Build_Throws_WhenAWhitespaceSegmentProducesInvalidKey()
    {
        var builder = new RoutingKeyBuilder();

        Assert.Throws<ArgumentException>(() => builder.Build("orders", "", "created"));
    }
}
