using Moq;
using SphereRabbitMQ.IaC.Application.Broker.Interfaces;
using SphereRabbitMQ.IaC.Domain.Topology;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Models;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Read;

namespace SphereRabbitMQ.IaC.Tests.Unit.Infrastructure.RabbitMQ;

public sealed class RabbitMqManagementTopologyReaderTests
{
    [Fact]
    public async Task ReadAsync_MapsManagementPayloads_IntoNormalizedTopology()
    {
        var apiClientMock = new Mock<IRabbitMqManagementApiClient>(MockBehavior.Strict);
        apiClientMock.Setup(client => client.GetVirtualHostsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ManagementVirtualHostModel { Name = "sales" }]);
        apiClientMock.Setup(client => client.GetExchangesAsync("sales", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ManagementExchangeModel { Name = "orders", Type = "topic", Durable = true }]);
        apiClientMock.Setup(client => client.GetQueuesAsync("sales", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ManagementQueueModel
            {
                Name = "orders.created",
                Durable = true,
                Arguments = new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
            }]);
        apiClientMock.Setup(client => client.GetBindingsAsync("sales", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ManagementBindingModel
            {
                Source = "orders",
                Destination = "orders.created",
                DestinationType = "queue",
                RoutingKey = "orders.created",
                PropertiesKey = "orders.created",
            }]);

        IBrokerTopologyReader brokerTopologyReader = new RabbitMqManagementTopologyReader(
            apiClientMock.Object,
            new RabbitMqManagementOptions
            {
                BaseUri = new Uri("http://localhost:15672/api/"),
                Username = "guest",
                Password = "guest",
            });

        var topologyDefinition = await brokerTopologyReader.ReadAsync();

        Assert.Single(topologyDefinition.VirtualHosts);
        Assert.Equal(QueueType.Quorum, topologyDefinition.VirtualHosts.Single().Queues.Single().Type);
        Assert.Single(topologyDefinition.VirtualHosts.Single().Bindings);
    }

    [Fact]
    public async Task ReadAsync_SkipsManagedVirtualHost_WhenItDoesNotExist()
    {
        var apiClientMock = new Mock<IRabbitMqManagementApiClient>(MockBehavior.Strict);
        apiClientMock.Setup(client => client.GetVirtualHostsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ManagementVirtualHostModel>());

        IBrokerTopologyReader brokerTopologyReader = new RabbitMqManagementTopologyReader(
            apiClientMock.Object,
            new RabbitMqManagementOptions
            {
                BaseUri = new Uri("http://localhost:15672/api/"),
                Username = "guest",
                Password = "guest",
                ManagedVirtualHosts = ["sales"],
            });

        var topologyDefinition = await brokerTopologyReader.ReadAsync();

        Assert.Empty(topologyDefinition.VirtualHosts);
        apiClientMock.Verify(client => client.GetExchangesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        apiClientMock.Verify(client => client.GetQueuesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        apiClientMock.Verify(client => client.GetBindingsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
