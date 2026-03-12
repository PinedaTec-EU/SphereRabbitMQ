using SphereRabbitMQ.IaC.Application.Apply;
using SphereRabbitMQ.IaC.Domain.Topology;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Apply;

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

    [RabbitMqConfiguredFact]
    public async Task Apply_WithMigrate_RecreatesIncompatibleExchangeAndRestoresBindingsAsync()
    {
        await _fixture.ResetVirtualHostAsync();

        if (!_fixture.SupportsRuntimeQueueMigration)
        {
            return;
        }

        var initialTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Direct)],
                queues:
                [
                    new QueueDefinition(
                        "orders.created",
                        QueueType.Classic,
                        arguments: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["x-queue-type"] = "classic",
                        }),
                ],
                bindings: [new BindingDefinition("orders", "orders.created", routingKey: "orders.created")]),
        ]);
        var initialPlan = await _fixture.TopologyPlanner.PlanAsync(initialTopology, new TopologyDefinition([]));
        await _fixture.TopologyApplier.ApplyAsync(initialTopology, initialPlan);

        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues:
                [
                    new QueueDefinition(
                        "orders.created",
                        QueueType.Classic,
                        arguments: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["x-queue-type"] = "classic",
                        }),
                ],
                bindings: [new BindingDefinition("orders", "orders.created", routingKey: "orders.created")]),
        ]);
        var actualTopology = await _fixture.BrokerTopologyReader.ReadAsync();
        var plan = await _fixture.TopologyPlanner.PlanAsync(desiredTopology, actualTopology);

        await _fixture.TopologyApplier.ApplyAsync(desiredTopology, plan, new TopologyApplyOptions { AllowMigrations = true });

        var actualAfterMigration = await _fixture.BrokerTopologyReader.ReadAsync();
        var virtualHost = actualAfterMigration.VirtualHosts.Single();

        Assert.Contains(virtualHost.Exchanges, exchange => exchange.Name == "orders" && exchange.Type == ExchangeType.Topic);
        Assert.Contains(virtualHost.Bindings, binding => binding.SourceExchange == "orders" && binding.Destination == "orders.created" && binding.RoutingKey == "orders.created");
    }

    [RabbitMqConfiguredFact]
    public async Task Apply_WithMigrate_RecreatesGeneratedQueueAsync()
    {
        await _fixture.ResetVirtualHostAsync();

        if (!_fixture.SupportsRuntimeQueueMigration)
        {
            return;
        }

        var generatedMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["generated-by"] = "SphereRabbitMQ.IaC",
            ["source-exchange"] = "orders",
        };

        var initialTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues:
                [
                    new QueueDefinition(
                        "orders.debug",
                        QueueType.Classic,
                        arguments: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["x-queue-type"] = "classic",
                        },
                        metadata: generatedMetadata),
                ],
                bindings:
                [
                    new BindingDefinition("orders", "orders.debug", routingKey: "#", metadata: generatedMetadata),
                ]),
        ]);
        var initialPlan = await _fixture.TopologyPlanner.PlanAsync(initialTopology, new TopologyDefinition([]));
        await _fixture.TopologyApplier.ApplyAsync(initialTopology, initialPlan);

        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues:
                [
                    new QueueDefinition(
                        "orders.debug",
                        QueueType.Quorum,
                        arguments: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["x-queue-type"] = "quorum",
                        },
                        metadata: generatedMetadata),
                ],
                bindings:
                [
                    new BindingDefinition("orders", "orders.debug", routingKey: "#", metadata: generatedMetadata),
                ]),
        ]);
        var actualTopology = await _fixture.BrokerTopologyReader.ReadAsync();
        var plan = await _fixture.TopologyPlanner.PlanAsync(desiredTopology, actualTopology);

        await _fixture.TopologyApplier.ApplyAsync(desiredTopology, plan, new TopologyApplyOptions { AllowMigrations = true });

        var actualAfterMigration = await _fixture.BrokerTopologyReader.ReadAsync();
        var virtualHost = actualAfterMigration.VirtualHosts.Single();

        Assert.Contains(virtualHost.Queues, queue => queue.Name == "orders.debug" && queue.Type == QueueType.Quorum);
        Assert.Contains(virtualHost.Bindings, binding => binding.SourceExchange == "orders" && binding.Destination == "orders.debug" && binding.RoutingKey == "#");
    }

    [RabbitMqConfiguredFact]
    public async Task Apply_WithMigrate_MainstreamQueueMovesExistingMessagesAsync()
    {
        await _fixture.ResetVirtualHostAsync();

        if (!_fixture.SupportsRuntimeQueueMigration)
        {
            return;
        }

        var initialTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues:
                [
                    new QueueDefinition(
                        "orders.created",
                        QueueType.Classic,
                        arguments: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["x-queue-type"] = "classic",
                        }),
                ],
                bindings: [new BindingDefinition("orders", "orders.created", routingKey: "orders.created")]),
        ]);
        var initialPlan = await _fixture.TopologyPlanner.PlanAsync(initialTopology, new TopologyDefinition([]));
        await _fixture.TopologyApplier.ApplyAsync(initialTopology, initialPlan);

        await _fixture.ApiClient.PublishMessageAsync(_fixture.VirtualHostName, "orders", "orders.created", "order-1", "string", null);
        await _fixture.ApiClient.PublishMessageAsync(_fixture.VirtualHostName, "orders", "orders.created", "order-2", "string", null);
        await _fixture.ApiClient.PublishMessageAsync(_fixture.VirtualHostName, "orders", "orders.created", "order-3", "string", null);

        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues:
                [
                    new QueueDefinition(
                        "orders.created",
                        QueueType.Quorum,
                        arguments: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["x-queue-type"] = "quorum",
                        }),
                ],
                bindings: [new BindingDefinition("orders", "orders.created", routingKey: "orders.created")]),
        ]);
        var actualTopology = await _fixture.BrokerTopologyReader.ReadAsync();
        var plan = await _fixture.TopologyPlanner.PlanAsync(desiredTopology, actualTopology);

        await _fixture.TopologyApplier.ApplyAsync(desiredTopology, plan, new TopologyApplyOptions { AllowMigrations = true });

        var actualAfterMigration = await _fixture.BrokerTopologyReader.ReadAsync();
        var virtualHost = actualAfterMigration.VirtualHosts.Single();
        var movedMessages = await _fixture.ApiClient.GetMessagesAsync(_fixture.VirtualHostName, "orders.created", 10);

        Assert.Contains(virtualHost.Queues, queue => queue.Name == "orders.created" && queue.Type == QueueType.Quorum);
        Assert.DoesNotContain(virtualHost.Queues, queue => queue.Name == "orders.created.sprmq-migration-temp");
        Assert.Contains(virtualHost.Bindings, binding => binding.SourceExchange == "orders" && binding.Destination == "orders.created" && binding.RoutingKey == "orders.created");
        Assert.Collection(
            movedMessages,
            message => Assert.Equal("order-1", message.Payload),
            message => Assert.Equal("order-2", message.Payload),
            message => Assert.Equal("order-3", message.Payload));
    }

    [RabbitMqConfiguredFact]
    public async Task Apply_WithMigrate_UsesLockQueueToSerializeConcurrentMigrationsAsync()
    {
        await _fixture.ResetVirtualHostAsync();

        if (!_fixture.SupportsRuntimeQueueMigration)
        {
            return;
        }

        var initialTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues:
                [
                    new QueueDefinition(
                        "orders.created",
                        QueueType.Classic,
                        arguments: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["x-queue-type"] = "classic",
                        }),
                ],
                bindings: [new BindingDefinition("orders", "orders.created", routingKey: "orders.created")]),
        ]);
        var initialPlan = await _fixture.TopologyPlanner.PlanAsync(initialTopology, new TopologyDefinition([]));
        await _fixture.TopologyApplier.ApplyAsync(initialTopology, initialPlan);

        var desiredTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues:
                [
                    new QueueDefinition(
                        "orders.created",
                        QueueType.Quorum,
                        arguments: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["x-queue-type"] = "quorum",
                        }),
                ],
                bindings: [new BindingDefinition("orders", "orders.created", routingKey: "orders.created")]),
        ]);
        var actualTopology = await _fixture.BrokerTopologyReader.ReadAsync();
        var plan = await _fixture.TopologyPlanner.PlanAsync(desiredTopology, actualTopology);
        var blockingMover = new BlockingQueueMigrationMessageMover();
        var applier = new RabbitMqManagementTopologyApplier(_fixture.ApiClient, blockingMover, new RabbitMqRuntimeTopologyMigrationLock(_fixture.Options));

        var firstApplyTask = applier.ApplyAsync(desiredTopology, plan, new TopologyApplyOptions { AllowMigrations = true }).AsTask();
        await blockingMover.WaitUntilFirstMoveStartsAsync();

        var lockQueue = await _fixture.ApiClient.GetQueueAsync(_fixture.VirtualHostName, "sprmq.migration.lock");
        Assert.NotNull(lockQueue);
        Assert.False(lockQueue!.Durable);
        Assert.True(lockQueue.AutoDelete);
        Assert.Equal(0, lockQueue!.Messages);

        var secondApplyTask = applier.ApplyAsync(desiredTopology, plan, new TopologyApplyOptions { AllowMigrations = true }).AsTask();
        await Task.Delay(500);

        Assert.False(secondApplyTask.IsCompleted);

        blockingMover.ReleaseFirstMove();
        await firstApplyTask;
        await secondApplyTask;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (await _fixture.ApiClient.GetQueueAsync(_fixture.VirtualHostName, "sprmq.migration.lock") is null)
            {
                break;
            }

            await Task.Delay(100);
        }

        var actualAfterMigration = await _fixture.BrokerTopologyReader.ReadAsync();
        Assert.Contains(actualAfterMigration.VirtualHosts.Single().Queues, queue => queue.Name == "orders.created" && queue.Type == QueueType.Quorum);
        Assert.Null(await _fixture.ApiClient.GetQueueAsync(_fixture.VirtualHostName, "sprmq.migration.lock"));
    }

    [RabbitMqConfiguredFact]
    public async Task Apply_WithMigrate_SerializesConcurrentQueueMigrationsAcrossDifferentTopologiesAsync()
    {
        await _fixture.ResetVirtualHostAsync();

        if (!_fixture.SupportsRuntimeQueueMigration)
        {
            return;
        }

        var initialTopology = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues:
                [
                    new QueueDefinition(
                        "orders.created.eu",
                        QueueType.Classic,
                        arguments: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["x-queue-type"] = "classic",
                        }),
                    new QueueDefinition(
                        "orders.created.us",
                        QueueType.Classic,
                        arguments: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["x-queue-type"] = "classic",
                        }),
                ],
                bindings:
                [
                    new BindingDefinition("orders", "orders.created.eu", routingKey: "orders.created.eu"),
                    new BindingDefinition("orders", "orders.created.us", routingKey: "orders.created.us"),
                ]),
        ]);
        var initialPlan = await _fixture.TopologyPlanner.PlanAsync(initialTopology, new TopologyDefinition([]));
        await _fixture.TopologyApplier.ApplyAsync(initialTopology, initialPlan);

        await _fixture.ApiClient.PublishMessageAsync(_fixture.VirtualHostName, "orders", "orders.created.eu", "order-eu-1", "string", null);
        await _fixture.ApiClient.PublishMessageAsync(_fixture.VirtualHostName, "orders", "orders.created.us", "order-us-1", "string", null);

        var desiredTopologyFirst = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues:
                [
                    new QueueDefinition(
                        "orders.created.eu",
                        QueueType.Quorum,
                        arguments: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["x-queue-type"] = "quorum",
                        }),
                    new QueueDefinition(
                        "orders.created.us",
                        QueueType.Classic,
                        arguments: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["x-queue-type"] = "classic",
                        }),
                ],
                bindings:
                [
                    new BindingDefinition("orders", "orders.created.eu", routingKey: "orders.created.eu"),
                    new BindingDefinition("orders", "orders.created.us", routingKey: "orders.created.us"),
                ]),
        ]);

        var desiredTopologySecond = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                _fixture.VirtualHostName,
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues:
                [
                    new QueueDefinition(
                        "orders.created.eu",
                        QueueType.Classic,
                        arguments: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["x-queue-type"] = "classic",
                        }),
                    new QueueDefinition(
                        "orders.created.us",
                        QueueType.Quorum,
                        arguments: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["x-queue-type"] = "quorum",
                        }),
                ],
                bindings:
                [
                    new BindingDefinition("orders", "orders.created.eu", routingKey: "orders.created.eu"),
                    new BindingDefinition("orders", "orders.created.us", routingKey: "orders.created.us"),
                ]),
        ]);

        var actualTopology = await _fixture.BrokerTopologyReader.ReadAsync();
        var firstPlan = await _fixture.TopologyPlanner.PlanAsync(desiredTopologyFirst, actualTopology);
        var secondPlan = await _fixture.TopologyPlanner.PlanAsync(desiredTopologySecond, actualTopology);
        var blockingMover = new BlockingQueueMigrationMessageMover();
        var applier = new RabbitMqManagementTopologyApplier(_fixture.ApiClient, blockingMover, new RabbitMqRuntimeTopologyMigrationLock(_fixture.Options));

        var firstApplyTask = applier.ApplyAsync(desiredTopologyFirst, firstPlan, new TopologyApplyOptions { AllowMigrations = true }).AsTask();
        await blockingMover.WaitUntilFirstMoveStartsAsync();

        var secondApplyTask = applier.ApplyAsync(desiredTopologySecond, secondPlan, new TopologyApplyOptions { AllowMigrations = true }).AsTask();
        await Task.Delay(500);

        Assert.False(secondApplyTask.IsCompleted);

        blockingMover.ReleaseFirstMove();
        await firstApplyTask;
        await secondApplyTask;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (await _fixture.ApiClient.GetQueueAsync(_fixture.VirtualHostName, "sprmq.migration.lock") is null)
            {
                break;
            }

            await Task.Delay(100);
        }

        var actualAfterMigration = await _fixture.BrokerTopologyReader.ReadAsync();
        var virtualHost = actualAfterMigration.VirtualHosts.Single();

        Assert.Contains(virtualHost.Queues, queue => queue.Name == "orders.created.eu" && queue.Type == QueueType.Quorum);
        Assert.Contains(virtualHost.Queues, queue => queue.Name == "orders.created.us" && queue.Type == QueueType.Quorum);
        Assert.Null(await _fixture.ApiClient.GetQueueAsync(_fixture.VirtualHostName, "sprmq.migration.lock"));
    }

    private sealed class BlockingQueueMigrationMessageMover : IQueueMigrationMessageMover
    {
        private readonly TaskCompletionSource _firstMoveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstMove = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _moveCalls;

        public async ValueTask MoveAsync(string sourceQueueName, string destinationQueueName, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _moveCalls) == 1)
            {
                _firstMoveStarted.TrySetResult();
                await _releaseFirstMove.Task.WaitAsync(cancellationToken);
            }
        }

        public Task WaitUntilFirstMoveStartsAsync()
            => _firstMoveStarted.Task;

        public void ReleaseFirstMove()
            => _releaseFirstMove.TrySetResult();
    }
}
