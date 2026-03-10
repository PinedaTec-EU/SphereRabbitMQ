using SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

namespace SphereRabbitMQ.IaC.Tests.Unit.Infrastructure;

public sealed class YamlTopologyDocumentMapperTests
{
    [Fact]
    public void Map_ConvertsYamlContracts_IntoApplicationDocument()
    {
        var yaml = new TopologyYamlDocument
        {
            VirtualHosts =
            [
                new VirtualHostYamlDocument
                {
                    Name = "sales",
                    Queues =
                    [
                        new QueueYamlDocument
                        {
                            Name = "orders",
                            Retry = new RetryYamlDocument
                            {
                                Steps =
                                [
                                    new RetryStepYamlDocument { Delay = "00:00:30", Name = "fast" },
                                ],
                            },
                        },
                    ],
                },
            ],
        };

        var document = YamlTopologyDocumentMapper.Map(yaml);

        var queue = document.VirtualHosts.Single().Queues.Single();
        Assert.Equal("orders", queue.Name);
        Assert.NotNull(queue.Retry);
        Assert.Equal("00:00:30", queue.Retry!.Steps.Single().Delay);
    }
}
