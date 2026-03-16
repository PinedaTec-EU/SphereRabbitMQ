using SphereRabbitMQ.Application.Subscribers;

namespace SphereRabbitMQ.Tests.Unit.Application;

public sealed class DefaultSubscriberInfrastructureRouteResolverTests
{
    [Fact]
    public void ResolveRetryRoute_ReturnsConventionBasedArtifacts()
    {
        var resolver = new DefaultSubscriberInfrastructureRouteResolver();

        var route = resolver.ResolveRetryRoute("orders.created");

        Assert.Equal("orders.created.retry", route.Exchange);
        Assert.Equal("orders.created.retry.step1", route.RoutingKey);
        Assert.Equal("orders.created.retry.step1", route.QueueName);
    }

    [Fact]
    public void ResolveDeadLetterRoute_ReturnsConventionBasedArtifacts()
    {
        var resolver = new DefaultSubscriberInfrastructureRouteResolver();

        var route = resolver.ResolveDeadLetterRoute("orders.created");

        Assert.Equal("orders.created.dlx", route.Exchange);
        Assert.Equal("orders.created", route.RoutingKey);
        Assert.Equal("orders.created.dlq", route.QueueName);
    }
}
