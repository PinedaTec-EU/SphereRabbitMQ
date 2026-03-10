using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Tests.Integration;

[Collection(RabbitMqManagementCollection.CollectionName)]
public sealed class RabbitMqManagementRoundTripIntegrationTests
{
    private readonly RabbitMqManagementIntegrationFixture _fixture;

    public RabbitMqManagementRoundTripIntegrationTests(RabbitMqManagementIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [RabbitMqConfiguredFact]
    public async Task PlanApplyReadAndExportAsync()
    {
        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues: [new QueueDefinition("orders.created")],
                bindings: [new BindingDefinition("orders", "orders.created", routingKey: "orders.created")]),
        ]);
        var topologyPlan = await _fixture.TopologyPlanner.PlanAsync(desiredTopology, new TopologyDefinition([]));

        await _fixture.TopologyApplier.ApplyAsync(desiredTopology, topologyPlan);

        var actualTopology = await _fixture.BrokerTopologyReader.ReadAsync();
        var exportedTopology = await _fixture.TopologyExporter.ExportAsync();

        Assert.Equal(_fixture.VirtualHostName, actualTopology.VirtualHosts.Single().Name);
        Assert.Equal("orders", actualTopology.VirtualHosts.Single().Exchanges.Single().Name);
        Assert.Equal("orders.created", actualTopology.VirtualHosts.Single().Queues.Single().Name);
        Assert.Equal("orders.created", actualTopology.VirtualHosts.Single().Bindings.Single().RoutingKey);
        Assert.Equal(_fixture.VirtualHostName, exportedTopology.VirtualHosts.Single().Name);
    }
}
