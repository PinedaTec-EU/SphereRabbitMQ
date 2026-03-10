using Moq;
using SphereRabbitMQ.IaC.Application.Apply.Interfaces;
using SphereRabbitMQ.IaC.Application.Planning;
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
}
