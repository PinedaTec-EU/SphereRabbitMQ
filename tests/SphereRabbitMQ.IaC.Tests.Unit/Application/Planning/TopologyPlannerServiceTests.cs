using SphereRabbitMQ.IaC.Application.Planning;
using SphereRabbitMQ.IaC.Application.Planning.Interfaces;
using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Tests.Unit.Application.Planning;

public sealed class TopologyPlannerServiceTests
{
    [Fact]
    public async Task PlanAsync_ReturnsCreateOperations_ForMissingResources()
    {
        ITopologyPlanner topologyPlanner = new TopologyPlannerService();
        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                "sales",
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues: [new QueueDefinition("orders.created")],
                bindings: [new BindingDefinition("orders", "orders.created", routingKey: "orders.created")]),
        ]);
        var actualTopology = new TopologyDefinition([]);

        var topologyPlan = await topologyPlanner.PlanAsync(desiredTopology, actualTopology);

        Assert.Contains(topologyPlan.Operations, operation => operation.Kind == TopologyPlanOperationKind.Create && operation.ResourceKind == TopologyResourceKind.VirtualHost);
        Assert.Contains(topologyPlan.Operations, operation => operation.Kind == TopologyPlanOperationKind.Create && operation.ResourceKind == TopologyResourceKind.Exchange);
        Assert.Contains(topologyPlan.Operations, operation => operation.Kind == TopologyPlanOperationKind.Create && operation.ResourceKind == TopologyResourceKind.Queue);
        Assert.Contains(topologyPlan.Operations, operation => operation.Kind == TopologyPlanOperationKind.Create && operation.ResourceKind == TopologyResourceKind.Binding);
    }

    [Fact]
    public async Task PlanAsync_FlagsDestructiveQueueChanges()
    {
        ITopologyPlanner topologyPlanner = new TopologyPlannerService();
        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                "sales",
                queues: [new QueueDefinition("orders", QueueType.Quorum)]),
        ]);
        var actualTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                "sales",
                queues: [new QueueDefinition("orders", QueueType.Classic)]),
        ]);

        var topologyPlan = await topologyPlanner.PlanAsync(desiredTopology, actualTopology);

        Assert.Contains(topologyPlan.Operations, operation => operation.Kind == TopologyPlanOperationKind.DestructiveChange && operation.ResourceKind == TopologyResourceKind.Queue);
        Assert.Single(topologyPlan.DestructiveChanges);
    }

    [Fact]
    public async Task PlanAsync_FlagsUnsupportedExchangeChanges()
    {
        ITopologyPlanner topologyPlanner = new TopologyPlannerService();
        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                "sales",
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)]),
        ]);
        var actualTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                "sales",
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Direct)]),
        ]);

        var topologyPlan = await topologyPlanner.PlanAsync(desiredTopology, actualTopology);

        Assert.Contains(topologyPlan.Operations, operation => operation.Kind == TopologyPlanOperationKind.UnsupportedChange && operation.ResourceKind == TopologyResourceKind.Exchange);
        Assert.Single(topologyPlan.UnsupportedChanges);
    }
}
