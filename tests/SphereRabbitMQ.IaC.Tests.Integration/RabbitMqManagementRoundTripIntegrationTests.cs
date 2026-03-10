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
        await _fixture.ResetVirtualHostAsync();

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
        Assert.Contains(actualTopology.VirtualHosts.Single().Queues, queue => queue.Name == "orders.created");
        Assert.Contains(actualTopology.VirtualHosts.Single().Bindings, binding => binding.RoutingKey == "orders.created");
        Assert.Equal(_fixture.VirtualHostName, exportedTopology.VirtualHosts.Single().Name);
    }

    [RabbitMqConfiguredFact]
    public async Task Apply_CreatesVirtualHost_WhenManagedVirtualHostDoesNotExistAsync()
    {
        await _fixture.ResetVirtualHostAsync();

        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues: [new QueueDefinition("orders.created")],
                bindings: [new BindingDefinition("orders", "orders.created", routingKey: "orders.created")]),
        ]);

        await _fixture.ApiClient.DeleteVirtualHostAsync(_fixture.VirtualHostName);

        var actualTopology = await _fixture.BrokerTopologyReader.ReadAsync();
        var topologyPlan = await _fixture.TopologyPlanner.PlanAsync(desiredTopology, actualTopology);

        Assert.Contains(
            topologyPlan.Operations,
            operation => operation.Kind == Domain.Planning.TopologyPlanOperationKind.Create &&
                         operation.ResourceKind == Domain.Planning.TopologyResourceKind.VirtualHost);

        await _fixture.TopologyApplier.ApplyAsync(desiredTopology, topologyPlan);

        var actualAfterApply = await _fixture.BrokerTopologyReader.ReadAsync();

        Assert.Equal(_fixture.VirtualHostName, actualAfterApply.VirtualHosts.Single().Name);
        Assert.Contains(actualAfterApply.VirtualHosts.Single().Exchanges, exchange => exchange.Name == "orders");
        Assert.Contains(actualAfterApply.VirtualHosts.Single().Queues, queue => queue.Name == "orders.created");
        Assert.Contains(actualAfterApply.VirtualHosts.Single().Bindings, binding => binding.RoutingKey == "orders.created");
    }

    [RabbitMqConfiguredFact]
    public async Task Apply_IsIdempotent_WhenExecutedTwiceAsync()
    {
        await _fixture.ResetVirtualHostAsync();

        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues: [new QueueDefinition(
                    "orders.created",
                    QueueType.Quorum,
                    durable: true,
                    arguments: new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["x-dead-letter-exchange"] = "orders.created.retry",
                        ["x-dead-letter-routing-key"] = "orders.created.retry.fast",
                        ["x-queue-type"] = "quorum",
                    },
                    deadLetter: new DeadLetterDefinition(enabled: true),
                    retry: new RetryDefinition(
                        enabled: true,
                        steps:
                        [
                            new RetryStepDefinition(TimeSpan.FromSeconds(30), "fast"),
                        ]))],
                bindings: [new BindingDefinition("orders", "orders.created", routingKey: "orders.created")]),
        ]);

        var firstPlan = await _fixture.TopologyPlanner.PlanAsync(desiredTopology, new TopologyDefinition([]));
        await _fixture.TopologyApplier.ApplyAsync(desiredTopology, firstPlan);

        var actualAfterFirstApply = await _fixture.BrokerTopologyReader.ReadAsync();
        var secondPlan = await _fixture.TopologyPlanner.PlanAsync(desiredTopology, actualAfterFirstApply);

        Assert.DoesNotContain(
            secondPlan.Operations,
            operation => operation.Kind is Domain.Planning.TopologyPlanOperationKind.DestructiveChange or
                         Domain.Planning.TopologyPlanOperationKind.UnsupportedChange or
                         Domain.Planning.TopologyPlanOperationKind.Create or
                         Domain.Planning.TopologyPlanOperationKind.Update);
        Assert.All(secondPlan.Operations, operation => Assert.Equal(Domain.Planning.TopologyPlanOperationKind.NoOp, operation.Kind));

        await _fixture.TopologyApplier.ApplyAsync(desiredTopology, secondPlan);

        var actualAfterSecondApply = await _fixture.BrokerTopologyReader.ReadAsync();
        Assert.Equal(_fixture.VirtualHostName, actualAfterSecondApply.VirtualHosts.Single().Name);
    }

    [RabbitMqConfiguredFact]
    public async Task DestroyDryRun_DoesNotRemoveResourcesAsync()
    {
        await _fixture.ResetVirtualHostAsync();

        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues: [new QueueDefinition("orders.created")],
                bindings: [new BindingDefinition("orders", "orders.created", routingKey: "orders.created")]),
        ]);
        var createPlan = await _fixture.TopologyPlanner.PlanAsync(desiredTopology, new TopologyDefinition([]));
        await _fixture.TopologyApplier.ApplyAsync(desiredTopology, createPlan);

        var actualBeforeDestroy = await _fixture.BrokerTopologyReader.ReadAsync();
        var destroyPlan = await _fixture.TopologyDestroyPlanner.PlanAsync(desiredTopology, actualBeforeDestroy, false);
        var actualAfterDryRun = await _fixture.BrokerTopologyReader.ReadAsync();

        Assert.Contains(destroyPlan.Operations, operation => operation.Kind == Domain.Planning.TopologyPlanOperationKind.Destroy);
        Assert.Contains(actualAfterDryRun.VirtualHosts.Single().Exchanges, exchange => exchange.Name == "orders");
        Assert.Contains(actualAfterDryRun.VirtualHosts.Single().Queues, queue => queue.Name == "orders.created");
    }

    [RabbitMqConfiguredFact]
    public async Task DestroyDeclaredResources_KeepsVirtualHostAndUnmanagedResourcesAsync()
    {
        await _fixture.ResetVirtualHostAsync();

        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues: [new QueueDefinition("orders.created")],
                bindings: [new BindingDefinition("orders", "orders.created", routingKey: "orders.created")]),
        ]);
        var createPlan = await _fixture.TopologyPlanner.PlanAsync(desiredTopology, new TopologyDefinition([]));
        await _fixture.TopologyApplier.ApplyAsync(desiredTopology, createPlan);
        await _fixture.ApiClient.UpsertQueueAsync(_fixture.VirtualHostName, new QueueDefinition("unmanaged.queue"));

        var actualBeforeDestroy = await _fixture.BrokerTopologyReader.ReadAsync();
        var destroyPlan = await _fixture.TopologyDestroyPlanner.PlanAsync(desiredTopology, actualBeforeDestroy, false);
        await _fixture.TopologyApplier.ApplyAsync(desiredTopology, destroyPlan);

        var actualAfterDestroy = await _fixture.BrokerTopologyReader.ReadAsync();
        var virtualHost = actualAfterDestroy.VirtualHosts.Single();

        Assert.Empty(virtualHost.Exchanges);
        Assert.DoesNotContain(virtualHost.Queues, queue => queue.Name == "orders.created");
        Assert.Contains(virtualHost.Queues, queue => queue.Name == "unmanaged.queue");
        Assert.Empty(virtualHost.Bindings);
    }

    [RabbitMqConfiguredFact]
    public async Task DestroyVirtualHost_RemovesEntireVirtualHostAsync()
    {
        await _fixture.ResetVirtualHostAsync();

        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues: [new QueueDefinition("orders.created")],
                bindings: [new BindingDefinition("orders", "orders.created", routingKey: "orders.created")]),
        ]);
        var createPlan = await _fixture.TopologyPlanner.PlanAsync(desiredTopology, new TopologyDefinition([]));
        await _fixture.TopologyApplier.ApplyAsync(desiredTopology, createPlan);

        var actualBeforeDestroy = await _fixture.BrokerTopologyReader.ReadAsync();
        var destroyPlan = await _fixture.TopologyDestroyPlanner.PlanAsync(desiredTopology, actualBeforeDestroy, true);
        await _fixture.TopologyApplier.ApplyAsync(desiredTopology, destroyPlan);

        var virtualHosts = await _fixture.ApiClient.GetVirtualHostsAsync();

        Assert.DoesNotContain(virtualHosts, virtualHost => virtualHost.Name == _fixture.VirtualHostName);
    }

    [RabbitMqConfiguredFact]
    public async Task Plan_FlagsDestructiveQueueTypeChangeAsync()
    {
        await _fixture.ResetVirtualHostAsync();

        var initialTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                queues: [new QueueDefinition("orders", QueueType.Classic)]),
        ]);
        var initialPlan = await _fixture.TopologyPlanner.PlanAsync(initialTopology, new TopologyDefinition([]));
        await _fixture.TopologyApplier.ApplyAsync(initialTopology, initialPlan);

        var actualTopology = await _fixture.BrokerTopologyReader.ReadAsync();
        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                queues: [new QueueDefinition("orders", QueueType.Quorum)]),
        ]);

        var plan = await _fixture.TopologyPlanner.PlanAsync(desiredTopology, actualTopology);

        Assert.Contains(plan.Operations, operation =>
            operation.Kind == Domain.Planning.TopologyPlanOperationKind.DestructiveChange &&
            operation.ResourceKind == Domain.Planning.TopologyResourceKind.Queue &&
            operation.ResourcePath == $"/virtualHosts/{_fixture.VirtualHostName}/queues/orders" &&
            operation.Diffs.Any(diff => diff.PropertyName == "type"));
    }

    [RabbitMqConfiguredFact]
    public async Task Plan_FlagsUnsupportedExchangeTypeChangeAsync()
    {
        await _fixture.ResetVirtualHostAsync();

        var initialTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Direct)]),
        ]);
        var initialPlan = await _fixture.TopologyPlanner.PlanAsync(initialTopology, new TopologyDefinition([]));
        await _fixture.TopologyApplier.ApplyAsync(initialTopology, initialPlan);

        var actualTopology = await _fixture.BrokerTopologyReader.ReadAsync();
        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)]),
        ]);

        var plan = await _fixture.TopologyPlanner.PlanAsync(desiredTopology, actualTopology);

        Assert.Contains(plan.Operations, operation =>
            operation.Kind == Domain.Planning.TopologyPlanOperationKind.UnsupportedChange &&
            operation.ResourceKind == Domain.Planning.TopologyResourceKind.Exchange &&
            operation.ResourcePath == $"/virtualHosts/{_fixture.VirtualHostName}/exchanges/orders" &&
            operation.Diffs.Any(diff => diff.PropertyName == "type"));
    }
}
