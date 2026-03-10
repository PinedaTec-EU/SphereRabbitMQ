using SphereRabbitMQ.IaC.Application.Planning;
using SphereRabbitMQ.IaC.Application.Planning.Interfaces;
using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Tests.Unit.Application.Planning;

public sealed class TopologyDestroyPlannerServiceTests
{
    [Fact]
    public async Task PlanAsync_ReturnsDestroyOperations_ForDeclaredResources()
    {
        ITopologyDestroyPlanner topologyDestroyPlanner = new TopologyDestroyPlannerService();
        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                "sales",
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues: [new QueueDefinition("orders.created")],
                bindings: [new BindingDefinition("orders", "orders.created", routingKey: "orders.created")]),
        ]);
        var actualTopology = desiredTopology;

        var topologyPlan = await topologyDestroyPlanner.PlanAsync(desiredTopology, actualTopology, false);

        Assert.Contains(topologyPlan.Operations, operation => operation.Kind == TopologyPlanOperationKind.Destroy && operation.ResourceKind == TopologyResourceKind.Binding);
        Assert.Contains(topologyPlan.Operations, operation => operation.Kind == TopologyPlanOperationKind.Destroy && operation.ResourceKind == TopologyResourceKind.Queue);
        Assert.Contains(topologyPlan.Operations, operation => operation.Kind == TopologyPlanOperationKind.Destroy && operation.ResourceKind == TopologyResourceKind.Exchange);
    }

    [Fact]
    public async Task PlanAsync_ReturnsNoOp_WhenResourcesAreMissing()
    {
        ITopologyDestroyPlanner topologyDestroyPlanner = new TopologyDestroyPlannerService();
        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                "sales",
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues: [new QueueDefinition("orders.created")],
                bindings: [new BindingDefinition("orders", "orders.created", routingKey: "orders.created")]),
        ]);
        var actualTopology = new TopologyDefinition([]);

        var topologyPlan = await topologyDestroyPlanner.PlanAsync(desiredTopology, actualTopology, false);

        Assert.All(topologyPlan.Operations, operation => Assert.Equal(TopologyPlanOperationKind.NoOp, operation.Kind));
    }

    [Fact]
    public async Task PlanAsync_IncludesGeneratedArtifacts()
    {
        ITopologyDestroyPlanner topologyDestroyPlanner = new TopologyDestroyPlannerService();
        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                "sales",
                exchanges:
                [
                    new ExchangeDefinition(
                        "orders.retry",
                        ExchangeType.Direct,
                        metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["generated-by"] = "SphereRabbitMQ.IaC",
                        }),
                ],
                queues:
                [
                    new QueueDefinition(
                        "orders.retry.30s",
                        metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["generated-by"] = "SphereRabbitMQ.IaC",
                        }),
                ],
                bindings:
                [
                    new BindingDefinition(
                        "orders.retry",
                        "orders.retry.30s",
                        routingKey: "orders.created",
                        metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["generated-by"] = "SphereRabbitMQ.IaC",
                        }),
                ]),
        ]);

        var topologyPlan = await topologyDestroyPlanner.PlanAsync(desiredTopology, desiredTopology, false);

        Assert.All(
            topologyPlan.Operations.Where(operation => operation.Kind == TopologyPlanOperationKind.Destroy),
            operation => Assert.Contains("generated", operation.Description, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanAsync_DestroyVirtualHosts_CollapsesChildOperations()
    {
        ITopologyDestroyPlanner topologyDestroyPlanner = new TopologyDestroyPlannerService();
        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                "sales",
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues: [new QueueDefinition("orders.created")],
                bindings: [new BindingDefinition("orders", "orders.created", routingKey: "orders.created")]),
        ]);

        var topologyPlan = await topologyDestroyPlanner.PlanAsync(desiredTopology, desiredTopology, true);

        Assert.Single(topologyPlan.Operations);
        Assert.Equal(TopologyPlanOperationKind.Destroy, topologyPlan.Operations[0].Kind);
        Assert.Equal(TopologyResourceKind.VirtualHost, topologyPlan.Operations[0].ResourceKind);
    }
}
