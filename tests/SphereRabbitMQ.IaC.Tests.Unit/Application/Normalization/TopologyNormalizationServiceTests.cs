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
        Assert.Contains(topologyDefinition.VirtualHosts[1].Queues, queue => queue.Name == "orders.retry.fast");
        Assert.Contains(topologyDefinition.VirtualHosts[1].Queues, queue => queue.Name == "orders.retry.slow");
        Assert.Equal(QueueType.Quorum, topologyDefinition.VirtualHosts[1].Queues.Single(queue => queue.Name == "orders").Type);
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
}
