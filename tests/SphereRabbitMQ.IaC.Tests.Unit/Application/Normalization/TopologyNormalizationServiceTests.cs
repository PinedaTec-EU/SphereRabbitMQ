using SphereRabbitMQ.IaC.Application.Models;
using SphereRabbitMQ.IaC.Application.Normalization;
using SphereRabbitMQ.IaC.Application.Normalization.Interfaces;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Tests.Unit.Application.Normalization;

public sealed class TopologyNormalizationServiceTests
{
    [Fact]
    public async Task NormalizeAsync_ProducesDeterministicOrdering_AndGeneratesRetryArtifacts()
    {
        ITopologyNormalizer topologyNormalizer = new TopologyNormalizationService();
        var topologyDocument = new TopologyDocument
        {
            DebugQueues = new DebugQueuesDocument
            {
                Enabled = true,
            },
            VirtualHosts =
            [
                new VirtualHostDocument
                {
                    Name = "z-vhost",
                    Exchanges =
                    [
                        new ExchangeDocument { Name = "events", Type = "topic" },
                        new ExchangeDocument { Name = "audit", Type = "fanout" },
                    ],
                    Queues =
                    [
                        new QueueDocument
                        {
                            Name = "orders",
                            Type = "quorum",
                            Ttl = "00:10:00",
                            Retry = new RetryDocument
                            {
                                Steps =
                                [
                                    new RetryStepDocument { Delay = "00:05:00", Name = "slow" },
                                    new RetryStepDocument { Delay = "00:01:00", Name = "fast" },
                                ],
                            },
                        },
                    ],
                },
                new VirtualHostDocument { Name = "a-vhost" },
            ],
        };

        var topologyDefinition = await topologyNormalizer.NormalizeAsync(topologyDocument);

        Assert.Equal(["a-vhost", "z-vhost"], topologyDefinition.VirtualHosts.Select(vhost => vhost.Name).ToArray());
        Assert.Equal(["audit", "events", "orders.retry"], topologyDefinition.VirtualHosts[1].Exchanges.Select(exchange => exchange.Name).ToArray());
        Assert.Contains(topologyDefinition.VirtualHosts[1].Queues, queue => queue.Name == "audit.debug");
        Assert.Contains(topologyDefinition.VirtualHosts[1].Queues, queue => queue.Name == "events.debug");
        Assert.Contains(topologyDefinition.VirtualHosts[1].Bindings, binding => binding.Destination == "events.debug" && binding.RoutingKey == "#");
        Assert.Equal(
            ExchangeType.Topic,
            topologyDefinition.VirtualHosts[1].Exchanges.Single(exchange => exchange.Name == "events").Type);
        Assert.True(topologyDefinition.VirtualHosts[1].Queues.Single(queue => queue.Name == "events.debug").Durable);
        Assert.Contains(topologyDefinition.VirtualHosts[1].Queues, queue => queue.Name == "orders.retry.fast");
        Assert.Contains(topologyDefinition.VirtualHosts[1].Queues, queue => queue.Name == "orders.retry.slow");
        Assert.Equal(QueueType.Quorum, topologyDefinition.VirtualHosts[1].Queues.Single(queue => queue.Name == "orders").Type);
        Assert.Equal(600000L, topologyDefinition.VirtualHosts[1].Queues.Single(queue => queue.Name == "orders").Arguments["x-message-ttl"]);
    }

    [Fact]
    public async Task NormalizeAsync_Throws_WhenExchangeTypeIsInvalid()
    {
        ITopologyNormalizer topologyNormalizer = new TopologyNormalizationService();
        var topologyDocument = new TopologyDocument
        {
            VirtualHosts =
            [
                new VirtualHostDocument
                {
                    Name = "sales",
                    Exchanges =
                    [
                        new ExchangeDocument { Name = "orders", Type = "invalid" },
                    ],
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<TopologyNormalizationException>(() => topologyNormalizer.NormalizeAsync(topologyDocument).AsTask());

        Assert.Contains(exception.Issues, issue => issue.Code == "invalid-exchange-type");
    }

    [Fact]
    public async Task NormalizeAsync_Throws_WhenQueueTtlIsInvalid()
    {
        ITopologyNormalizer topologyNormalizer = new TopologyNormalizationService();
        var topologyDocument = new TopologyDocument
        {
            VirtualHosts =
            [
                new VirtualHostDocument
                {
                    Name = "sales",
                    Queues =
                    [
                        new QueueDocument
                        {
                            Name = "orders",
                            Ttl = "-00:00:01",
                        },
                    ],
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<TopologyNormalizationException>(() => topologyNormalizer.NormalizeAsync(topologyDocument).AsTask());

        Assert.Contains(exception.Issues, issue => issue.Code == "invalid-queue-ttl");
    }

    [Fact]
    public async Task NormalizeAsync_Throws_WhenDerivedArgumentsConflict()
    {
        ITopologyNormalizer topologyNormalizer = new TopologyNormalizationService();
        var topologyDocument = new TopologyDocument
        {
            VirtualHosts =
            [
                new VirtualHostDocument
                {
                    Name = "sales",
                    Queues =
                    [
                        new QueueDocument
                        {
                            Name = "orders",
                            DeadLetter = new DeadLetterDocument { Enabled = true },
                            Arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["x-dead-letter-exchange"] = "custom.exchange",
                            },
                        },
                    ],
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<TopologyNormalizationException>(() => topologyNormalizer.NormalizeAsync(topologyDocument).AsTask());

        Assert.Contains(exception.Issues, issue => issue.Code == "conflicting-derived-argument");
    }

    [Fact]
    public async Task NormalizeAsync_GeneratesDeadLetterArtifacts_WhenDeadLetterEnabledWithoutRetry()
    {
        ITopologyNormalizer topologyNormalizer = new TopologyNormalizationService();
        var topologyDocument = new TopologyDocument
        {
            VirtualHosts =
            [
                new VirtualHostDocument
                {
                    Name = "sales",
                    Queues =
                    [
                        new QueueDocument
                        {
                            Name = "orders",
                            DeadLetter = new DeadLetterDocument { Enabled = true },
                        },
                    ],
                },
            ],
        };

        var topologyDefinition = await topologyNormalizer.NormalizeAsync(topologyDocument);
        var virtualHost = topologyDefinition.VirtualHosts.Single();

        Assert.Contains(virtualHost.Exchanges, exchange => exchange.Name == "orders.dlx");
        Assert.Contains(virtualHost.Queues, queue => queue.Name == "orders.dlq");
        Assert.Contains(virtualHost.Bindings, binding =>
            binding.SourceExchange == "orders.dlx" &&
            binding.Destination == "orders.dlq" &&
            binding.RoutingKey == "orders");
    }

    [Fact]
    public async Task NormalizeAsync_UsesDefaultNamingPolicy_WhenNamingBlockIsOmitted()
    {
        ITopologyNormalizer topologyNormalizer = new TopologyNormalizationService();
        var topologyDocument = new TopologyDocument
        {
            VirtualHosts =
            [
                new VirtualHostDocument
                {
                    Name = "sales",
                    Queues =
                    [
                        new QueueDocument
                        {
                            Name = "orders",
                            Retry = new RetryDocument
                            {
                                Enabled = true,
                                Steps =
                                [
                                    new RetryStepDocument
                                    {
                                        Delay = "00:00:30",
                                    },
                                ],
                            },
                            DeadLetter = new DeadLetterDocument
                            {
                                Enabled = true,
                            },
                        },
                    ],
                },
            ],
        };

        var topologyDefinition = await topologyNormalizer.NormalizeAsync(topologyDocument);
        var virtualHost = topologyDefinition.VirtualHosts.Single();

        Assert.Equal(".", topologyDefinition.NamingPolicy.Separator);
        Assert.Contains(virtualHost.Exchanges, exchange => exchange.Name == "orders.retry");
        Assert.Contains(virtualHost.Queues, queue => queue.Name == "orders.retry.step1");
        Assert.Contains(virtualHost.Exchanges, exchange => exchange.Name == "orders.dlx");
        Assert.Contains(virtualHost.Queues, queue => queue.Name == "orders.dlq");
    }

    [Fact]
    public async Task NormalizeAsync_AppliesDeadLetterQueueTtl_WhenConfigured()
    {
        ITopologyNormalizer topologyNormalizer = new TopologyNormalizationService();
        var topologyDocument = new TopologyDocument
        {
            VirtualHosts =
            [
                new VirtualHostDocument
                {
                    Name = "sales",
                    Queues =
                    [
                        new QueueDocument
                        {
                            Name = "orders",
                            DeadLetter = new DeadLetterDocument
                            {
                                Enabled = true,
                                Ttl = "00:02:00",
                            },
                        },
                    ],
                },
            ],
        };

        var topologyDefinition = await topologyNormalizer.NormalizeAsync(topologyDocument);
        var deadLetterQueue = topologyDefinition.VirtualHosts.Single().Queues.Single(queue => queue.Name == "orders.dlq");

        Assert.Equal(120000L, deadLetterQueue.Arguments["x-message-ttl"]);
    }

    [Fact]
    public async Task NormalizeAsync_UsesDefaultExchange_WhenDeadLetterTargetsExistingQueue()
    {
        ITopologyNormalizer topologyNormalizer = new TopologyNormalizationService();
        var topologyDocument = new TopologyDocument
        {
            VirtualHosts =
            [
                new VirtualHostDocument
                {
                    Name = "sales",
                    Queues =
                    [
                        new QueueDocument
                        {
                            Name = "orders.delay",
                            DeadLetter = new DeadLetterDocument
                            {
                                Enabled = true,
                                DestinationType = "queue",
                                QueueName = "orders.consume",
                                Ttl = "00:05:00",
                            },
                        },
                        new QueueDocument
                        {
                            Name = "orders.consume",
                        },
                    ],
                },
            ],
        };

        var topologyDefinition = await topologyNormalizer.NormalizeAsync(topologyDocument);
        var virtualHost = topologyDefinition.VirtualHosts.Single();
        var delayQueue = virtualHost.Queues.Single(queue => queue.Name == "orders.delay");

        Assert.Equal(string.Empty, delayQueue.Arguments["x-dead-letter-exchange"]);
        Assert.Equal("orders.consume", delayQueue.Arguments["x-dead-letter-routing-key"]);
        Assert.DoesNotContain(virtualHost.Exchanges, exchange => exchange.Name == "orders.delay.dlx");
        Assert.DoesNotContain(virtualHost.Queues, queue => queue.Name == "orders.consume.dlq");
    }

    [Fact]
    public async Task NormalizeAsync_Throws_WhenDeadLetterQueueTargetOmitsQueueName()
    {
        ITopologyNormalizer topologyNormalizer = new TopologyNormalizationService();
        var topologyDocument = new TopologyDocument
        {
            VirtualHosts =
            [
                new VirtualHostDocument
                {
                    Name = "sales",
                    Queues =
                    [
                        new QueueDocument
                        {
                            Name = "orders.delay",
                            DeadLetter = new DeadLetterDocument
                            {
                                Enabled = true,
                                DestinationType = "queue",
                            },
                        },
                    ],
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<TopologyNormalizationException>(() => topologyNormalizer.NormalizeAsync(topologyDocument).AsTask());

        Assert.Contains(exception.Issues, issue => issue.Code == "dead-letter-queue-target-required");
    }
}
