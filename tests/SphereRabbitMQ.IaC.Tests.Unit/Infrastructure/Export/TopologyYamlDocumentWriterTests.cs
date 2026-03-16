using SphereRabbitMQ.IaC.Application.Export.Interfaces;
using SphereRabbitMQ.IaC.Application.Models;
using SphereRabbitMQ.IaC.Infrastructure.Yaml.Export;

namespace SphereRabbitMQ.IaC.Tests.Unit.Infrastructure.Export;

public sealed class TopologyYamlDocumentWriterTests
{
    [Fact]
    public async Task WriteAsync_SerializesCompleteDocument_ToYaml()
    {
        ITopologyDocumentWriter writer = new TopologyYamlDocumentWriter();
        var document = new TopologyDocument
        {
            Broker = new BrokerDocument
            {
                ManagementUrl = "http://localhost:15672/api/",
                Username = "guest",
                Password = "guest",
                VirtualHosts = ["sales"],
            },
            Decommission = new DecommissionDocument
            {
                VirtualHosts =
                [
                    new DecommissionVirtualHostDocument
                    {
                        Name = "sales",
                        Exchanges = ["orders.legacy"],
                    },
                ],
            },
            DebugQueues = new DebugQueuesDocument
            {
                Enabled = true,
                QueueSuffix = "dbg",
            },
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["team"] = "sales",
            },
            Naming = new NamingConventionDocument
            {
                Separator = ".",
                RetryExchangeSuffix = "retry",
                RetryQueueSuffix = "retry",
                DeadLetterExchangeSuffix = "dlx",
                DeadLetterQueueSuffix = "dlq",
                StepTokenPrefix = "step",
            },
            VirtualHosts =
            [
                new VirtualHostDocument
                {
                    Name = "sales",
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["owner"] = "platform",
                    },
                    Exchanges =
                    [
                        new ExchangeDocument
                        {
                            Name = "orders",
                            Type = "topic",
                            Durable = true,
                            DebugQueue = true,
                            Arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["alternate-exchange"] = "orders.unrouted",
                            },
                            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["purpose"] = "business-events",
                            },
                        },
                    ],
                    Queues =
                    [
                        new QueueDocument
                        {
                            Name = "orders.created",
                            Type = "quorum",
                            Durable = true,
                            DebugQueue = true,
                            Arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["x-message-ttl"] = 30000,
                            },
                            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["classification"] = "critical",
                            },
                            DeadLetter = new DeadLetterDocument
                            {
                                Enabled = true,
                                DestinationType = "generated",
                                ExchangeName = "orders.dlx",
                                QueueName = "orders.created.dlq",
                                RoutingKey = "orders.created.dead",
                                Ttl = "00:05:00",
                            },
                            Retry = new RetryDocument
                            {
                                Enabled = true,
                                AutoGenerateArtifacts = false,
                                ExchangeName = "orders.retry",
                                Steps =
                                [
                                    new RetryStepDocument
                                    {
                                        Delay = "00:00:30",
                                        Name = "fast",
                                        QueueName = "orders.retry.fast",
                                        RoutingKey = "orders.created.retry.fast",
                                    },
                                ],
                            },
                        },
                    ],
                    Bindings =
                    [
                        new BindingDocument
                        {
                            SourceExchange = "orders",
                            Destination = "orders.created",
                            DestinationType = "queue",
                            RoutingKey = "orders.created",
                            Arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["x-match"] = "all",
                            },
                            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["managed-by"] = "iac",
                            },
                        },
                    ],
                },
            ],
        };

        var yaml = await writer.WriteAsync(document, CancellationToken.None);

        Assert.Contains("broker:", yaml, StringComparison.Ordinal);
        Assert.Contains("managementUrl: http://localhost:15672/api/", yaml, StringComparison.Ordinal);
        Assert.Contains("username: guest", yaml, StringComparison.Ordinal);
        Assert.Contains("password: guest", yaml, StringComparison.Ordinal);
        Assert.Contains("metadata:", yaml, StringComparison.Ordinal);
        Assert.Contains("team: sales", yaml, StringComparison.Ordinal);
        Assert.Contains("decommission:", yaml, StringComparison.Ordinal);
        Assert.Contains("orders.legacy", yaml, StringComparison.Ordinal);
        Assert.Contains("debugQueues:", yaml, StringComparison.Ordinal);
        Assert.Contains("queueSuffix: dbg", yaml, StringComparison.Ordinal);
        Assert.Contains("naming:", yaml, StringComparison.Ordinal);
        Assert.Contains("retryExchangeSuffix: retry", yaml, StringComparison.Ordinal);
        Assert.Contains("deadLetterExchangeSuffix: dlx", yaml, StringComparison.Ordinal);
        Assert.Contains("virtualHosts:", yaml, StringComparison.Ordinal);
        Assert.Contains("- name: sales", yaml, StringComparison.Ordinal);
        Assert.Contains("exchanges:", yaml, StringComparison.Ordinal);
        Assert.Contains("debugQueue: true", yaml, StringComparison.Ordinal);
        Assert.Contains("type: topic", yaml, StringComparison.Ordinal);
        Assert.Contains("queues:", yaml, StringComparison.Ordinal);
        Assert.Contains("deadLetter:", yaml, StringComparison.Ordinal);
        Assert.Contains("destinationType: generated", yaml, StringComparison.Ordinal);
        Assert.Contains("exchangeName: orders.dlx", yaml, StringComparison.Ordinal);
        Assert.Contains("ttl: 00:05:00", yaml, StringComparison.Ordinal);
        Assert.Contains("retry:", yaml, StringComparison.Ordinal);
        Assert.Contains("autoGenerateArtifacts: false", yaml, StringComparison.Ordinal);
        Assert.Contains("steps:", yaml, StringComparison.Ordinal);
        Assert.Contains("delay: 00:00:30", yaml, StringComparison.Ordinal);
        Assert.Contains("bindings:", yaml, StringComparison.Ordinal);
        Assert.Contains("sourceExchange: orders", yaml, StringComparison.Ordinal);
        Assert.Contains("destinationType: queue", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_OmitsOptionalSections_WhenTheyAreNull()
    {
        ITopologyDocumentWriter writer = new TopologyYamlDocumentWriter();
        var document = new TopologyDocument
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
                            Name = "orders.created",
                        },
                    ],
                },
            ],
        };

        var yaml = await writer.WriteAsync(document, CancellationToken.None);

        Assert.Contains("virtualHosts:", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("broker:", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("naming:", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("deadLetter:", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("retry:", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_Throws_WhenCancellationIsRequested()
    {
        ITopologyDocumentWriter writer = new TopologyYamlDocumentWriter();
        var document = new TopologyDocument();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => writer.WriteAsync(document, cancellationTokenSource.Token).AsTask());
    }

    [Fact]
    public async Task WriteAsync_Throws_WhenDocumentIsNull()
    {
        ITopologyDocumentWriter writer = new TopologyYamlDocumentWriter();

        await Assert.ThrowsAsync<ArgumentNullException>(() => writer.WriteAsync(null!, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task WriteAsync_SerializesDeadLetterQueueDestination()
    {
        ITopologyDocumentWriter writer = new TopologyYamlDocumentWriter();
        var document = new TopologyDocument
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

        var yaml = await writer.WriteAsync(document, CancellationToken.None);

        Assert.Contains("destinationType: queue", yaml, StringComparison.Ordinal);
        Assert.Contains("queueName: orders.consume", yaml, StringComparison.Ordinal);
    }
}
