using SphereRabbitMQ.IaC.Application.Models;
using SphereRabbitMQ.IaC.Application.Services;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Tests.Unit.Application;

public sealed class TopologyNormalizationServiceTests
{
    [Fact]
    public async Task NormalizeAsync_ProducesDeterministicOrdering_AndParsesRetrySettings()
    {
        var service = new TopologyNormalizationService();
        var document = new TopologyDocument
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

        var result = await service.NormalizeAsync(document);

        Assert.Equal(["a-vhost", "z-vhost"], result.VirtualHosts.Select(vhost => vhost.Name).ToArray());
        var normalizedQueue = result.VirtualHosts[1].Queues.Single();
        Assert.Equal(QueueType.Quorum, normalizedQueue.Type);
        Assert.NotNull(normalizedQueue.Retry);
        Assert.Equal(2, normalizedQueue.Retry!.Steps.Count);
        Assert.Equal(["audit", "events"], result.VirtualHosts[1].Exchanges.Select(exchange => exchange.Name).ToArray());
    }

    [Fact]
    public async Task NormalizeAsync_Throws_WhenExchangeTypeIsInvalid()
    {
        var service = new TopologyNormalizationService();
        var document = new TopologyDocument
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

        var exception = await Assert.ThrowsAsync<TopologyNormalizationException>(() => service.NormalizeAsync(document).AsTask());

        Assert.Contains(exception.Issues, issue => issue.Code == "invalid-exchange-type");
    }
}
