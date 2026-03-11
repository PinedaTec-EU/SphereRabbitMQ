using Moq;
using SphereRabbitMQ.IaC.Application.Apply;
using SphereRabbitMQ.IaC.Application.Apply.Interfaces;
using SphereRabbitMQ.IaC.Application.Planning;
using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Apply;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Interfaces;

namespace SphereRabbitMQ.IaC.Tests.Unit.Infrastructure.RabbitMQ;

public sealed class RabbitMqManagementTopologyApplierTests
{
    [Fact]
    public async Task ApplyAsync_CreatesResources_ForCreateOperations()
    {
        var apiClientMock = new Mock<IRabbitMqManagementApiClient>(MockBehavior.Strict);
        apiClientMock.Setup(client => client.CreateVirtualHostAsync("sales", It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        apiClientMock.Setup(client => client.UpsertExchangeAsync("sales", It.IsAny<ExchangeDefinition>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        apiClientMock.Setup(client => client.UpsertQueueAsync("sales", It.IsAny<QueueDefinition>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        apiClientMock.Setup(client => client.CreateBindingAsync("sales", It.IsAny<BindingDefinition>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);

        ITopologyApplier topologyApplier = new RabbitMqManagementTopologyApplier(apiClientMock.Object);
        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                "sales",
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues: [new QueueDefinition("orders.created")],
                bindings: [new BindingDefinition("orders", "orders.created", routingKey: "orders.created")]),
        ]);
        var topologyPlan = await new TopologyPlannerService().PlanAsync(desiredTopology, new TopologyDefinition([]));

        await topologyApplier.ApplyAsync(desiredTopology, topologyPlan);

        apiClientMock.Verify(client => client.CreateVirtualHostAsync("sales", It.IsAny<CancellationToken>()), Times.Once);
        apiClientMock.Verify(client => client.UpsertExchangeAsync("sales", It.IsAny<ExchangeDefinition>(), It.IsAny<CancellationToken>()), Times.Once);
        apiClientMock.Verify(client => client.UpsertQueueAsync("sales", It.IsAny<QueueDefinition>(), It.IsAny<CancellationToken>()), Times.Once);
        apiClientMock.Verify(client => client.CreateBindingAsync("sales", It.IsAny<BindingDefinition>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_WithMigrate_RecreatesGeneratedQueueAndBinding()
    {
        var apiClientMock = new Mock<IRabbitMqManagementApiClient>(MockBehavior.Strict);
        apiClientMock.Setup(client => client.UpsertQueueAsync("sales", It.Is<QueueDefinition>(queue => queue.Name == "sprmq.migration.lock"), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        apiClientMock.Setup(client => client.PublishMessageAsync("sales", "", "sprmq.migration.lock", "lock-token", "string", null, It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        apiClientMock.Setup(client => client.DeleteQueueAsync("sales", "orders.debug", It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        apiClientMock.Setup(client => client.UpsertQueueAsync("sales", It.Is<QueueDefinition>(queue => queue.Name == "orders.debug"), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        apiClientMock.Setup(client => client.CreateBindingAsync("sales", It.Is<BindingDefinition>(binding => binding.Destination == "orders.debug"), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        apiClientMock.Setup(client => client.GetMessagesAsync("sales", "sprmq.migration.lock", 1, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<global::SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Models.ManagementRetrievedMessageModel>>(
                [new global::SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Models.ManagementRetrievedMessageModel { Payload = "lock-token" }]));

        ITopologyApplier topologyApplier = new RabbitMqManagementTopologyApplier(apiClientMock.Object);
        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                "sales",
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues:
                [
                    new QueueDefinition(
                        "orders.debug",
                        metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["generated-by"] = "SphereRabbitMQ.IaC",
                            ["source-exchange"] = "orders",
                        }),
                ],
                bindings:
                [
                    new BindingDefinition(
                        "orders",
                        "orders.debug",
                        routingKey: "#",
                        metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["generated-by"] = "SphereRabbitMQ.IaC",
                            ["source-exchange"] = "orders",
                        }),
                ]),
        ]);
        var topologyPlan = new TopologyPlan(
        [
            new TopologyPlanOperation(
                TopologyPlanOperationKind.DestructiveChange,
                TopologyResourceKind.Queue,
                "/virtualHosts/sales/queues/orders.debug",
                "Queue 'orders.debug' requires delete/recreate because immutable properties changed."),
        ],
        destructiveChanges:
        [
            new DestructiveChangeWarning("/virtualHosts/sales/queues/orders.debug", "Queue 'orders.debug' requires delete/recreate because immutable properties changed."),
        ]);

        await topologyApplier.ApplyAsync(desiredTopology, topologyPlan, new TopologyApplyOptions { AllowMigrations = true });

        apiClientMock.Verify(client => client.DeleteQueueAsync("sales", "orders.debug", It.IsAny<CancellationToken>()), Times.Once);
        apiClientMock.Verify(client => client.UpsertQueueAsync("sales", It.Is<QueueDefinition>(queue => queue.Name == "orders.debug"), It.IsAny<CancellationToken>()), Times.Once);
        apiClientMock.Verify(client => client.CreateBindingAsync("sales", It.Is<BindingDefinition>(binding => binding.Destination == "orders.debug"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
