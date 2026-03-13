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

    [Fact]
    public async Task PlanAsync_TreatsEquivalentArgumentDictionaries_AsNoOp()
    {
        ITopologyPlanner topologyPlanner = new TopologyPlannerService();
        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                "sales",
                queues:
                [
                    new QueueDefinition(
                        "orders.retry",
                        QueueType.Classic,
                        arguments: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["x-queue-type"] = "classic",
                            ["x-message-ttl"] = 30000L,
                            ["x-dead-letter-exchange"] = string.Empty,
                            ["x-dead-letter-routing-key"] = "orders.created",
                        }),
                ]),
        ]);
        var actualTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                "sales",
                queues:
                [
                    new QueueDefinition(
                        "orders.retry",
                        QueueType.Classic,
                        arguments: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["x-dead-letter-exchange"] = string.Empty,
                            ["x-dead-letter-routing-key"] = "orders.created",
                            ["x-message-ttl"] = 30000L,
                            ["x-queue-type"] = "classic",
                        }),
                ]),
        ]);

        var topologyPlan = await topologyPlanner.PlanAsync(desiredTopology, actualTopology);

        Assert.All(topologyPlan.Operations, operation => Assert.Equal(TopologyPlanOperationKind.NoOp, operation.Kind));
    }

    [Fact]
    public async Task PlanAsync_CreatesExplicitDestroyOperations_ForDecommissionedResourcesWithoutWarnings()
    {
        ITopologyPlanner topologyPlanner = new TopologyPlannerService();
        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                "sales",
                exchanges: [new ExchangeDefinition("orders.current", ExchangeType.Topic)],
                queues: [new QueueDefinition("orders.created")],
                bindings: [new BindingDefinition("orders.current", "orders.created", routingKey: "orders.created")]),
        ],
        [
            new DecommissionVirtualHostDefinition(
                "sales",
                exchanges: ["orders.legacy"],
                bindings: [new BindingDefinition("orders.legacy", "orders.created", routingKey: "orders.created")]),
        ]);
        var actualTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                "sales",
                exchanges:
                [
                    new ExchangeDefinition("orders.current", ExchangeType.Topic),
                    new ExchangeDefinition("orders.legacy", ExchangeType.Topic),
                ],
                queues: [new QueueDefinition("orders.created")],
                bindings:
                [
                    new BindingDefinition("orders.current", "orders.created", routingKey: "orders.created"),
                    new BindingDefinition("orders.legacy", "orders.created", routingKey: "orders.created"),
                ]),
        ]);

        var topologyPlan = await topologyPlanner.PlanAsync(desiredTopology, actualTopology);

        Assert.Empty(topologyPlan.DestructiveChanges);
        Assert.DoesNotContain(topologyPlan.Operations, operation => operation.Kind == TopologyPlanOperationKind.DestructiveChange);
        Assert.Contains(topologyPlan.Operations, operation =>
            operation.Kind == TopologyPlanOperationKind.Destroy &&
            operation.ResourceKind == TopologyResourceKind.Exchange &&
            operation.ResourcePath == "/virtualHosts/sales/exchanges/orders.legacy");
        Assert.Contains(topologyPlan.Operations, operation =>
            operation.Kind == TopologyPlanOperationKind.Destroy &&
            operation.ResourceKind == TopologyResourceKind.Binding &&
            operation.ResourcePath == "/virtualHosts/sales/bindings/orders.legacy|Queue|orders.created|orders.created");
    }

    [Fact]
    public async Task PlanAsync_UsesNoOp_WhenDecommissionedResourceIsAlreadyAbsent()
    {
        ITopologyPlanner topologyPlanner = new TopologyPlannerService();
        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition("sales"),
        ],
        [
            new DecommissionVirtualHostDefinition(
                "sales",
                exchanges: ["orders.legacy"]),
        ]);
        var actualTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition("sales"),
        ]);

        var topologyPlan = await topologyPlanner.PlanAsync(desiredTopology, actualTopology);

        Assert.Contains(topologyPlan.Operations, operation =>
            operation.Kind == TopologyPlanOperationKind.NoOp &&
            operation.ResourceKind == TopologyResourceKind.Exchange &&
            operation.ResourcePath == "/virtualHosts/sales/exchanges/orders.legacy");
    }
}
