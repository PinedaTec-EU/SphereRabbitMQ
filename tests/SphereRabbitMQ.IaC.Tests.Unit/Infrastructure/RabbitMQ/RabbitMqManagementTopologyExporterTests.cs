using Moq;
using SphereRabbitMQ.IaC.Application.Broker.Interfaces;
using SphereRabbitMQ.IaC.Application.Export.Interfaces;
using SphereRabbitMQ.IaC.Domain.Topology;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Export;

namespace SphereRabbitMQ.IaC.Tests.Unit.Infrastructure.RabbitMQ;

public sealed class RabbitMqManagementTopologyExporterTests
{
    [Fact]
    public async Task ExportAsync_MapsNormalizedTopology_IntoDocumentModel()
    {
        var brokerTopologyReaderMock = new Mock<IBrokerTopologyReader>(MockBehavior.Strict);
        brokerTopologyReaderMock.Setup(reader => reader.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TopologyDefinition(
            [
                new VirtualHostDefinition(
                    "sales",
                    exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                    queues: [new QueueDefinition("orders.created")],
                    bindings: [new BindingDefinition("orders", "orders.created", routingKey: "orders.created")]),
            ]));

        ITopologyExporter topologyExporter = new RabbitMqManagementTopologyExporter(brokerTopologyReaderMock.Object);

        var topologyDocument = await topologyExporter.ExportAsync();

        Assert.Equal("sales", topologyDocument.VirtualHosts.Single().Name);
        Assert.Equal("orders", topologyDocument.VirtualHosts.Single().Exchanges.Single().Name);
        Assert.Equal("orders.created", topologyDocument.VirtualHosts.Single().Queues.Single().Name);
    }
}
