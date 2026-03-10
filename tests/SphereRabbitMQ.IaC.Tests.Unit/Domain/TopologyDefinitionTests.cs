using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Tests.Unit.Domain;

public sealed class TopologyDefinitionTests
{
    [Fact]
    public void Validate_ReturnsError_WhenBindingReferencesMissingDestination()
    {
        var topology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                "sales",
                exchanges:
                [
                    new ExchangeDefinition("orders", ExchangeType.Topic),
                ],
                bindings:
                [
                    new BindingDefinition("orders", "missing-queue", BindingDestinationType.Queue, "orders.created"),
                ]),
        ]);

        var result = topology.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "missing-binding-destination");
    }

    [Fact]
    public void Validate_ReturnsError_WhenGeneratedArtifactsCollide()
    {
        var queueOne = new QueueDefinition(
            "orders",
            retry: new RetryDefinition(
            [
                new RetryStepDefinition(TimeSpan.FromMinutes(1), name: "attempt"),
            ],
            exchangeName: "shared.retry"));

        var queueTwo = new QueueDefinition(
            "payments",
            retry: new RetryDefinition(
            [
                new RetryStepDefinition(TimeSpan.FromMinutes(2), name: "attempt"),
            ],
            exchangeName: "shared.retry"));

        var topology = new TopologyDefinition(
        [
            new VirtualHostDefinition("sales", queues: [queueOne, queueTwo]),
        ]);

        var result = topology.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "generated-name-collision");
    }
}
