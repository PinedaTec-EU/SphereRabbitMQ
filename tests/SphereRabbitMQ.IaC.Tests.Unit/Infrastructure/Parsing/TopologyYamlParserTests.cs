using Moq;
using SphereRabbitMQ.IaC.Application.Parsing.Interfaces;
using SphereRabbitMQ.IaC.Application.Variables.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.Yaml.Parsing;

namespace SphereRabbitMQ.IaC.Tests.Unit.Infrastructure.Parsing;

public sealed class TopologyYamlParserTests
{
    [Fact]
    public async Task ParseAsync_ResolvesVariables_AndBuildsTopologyDocument()
    {
        var variableResolverMock = new Mock<IVariableResolver>(MockBehavior.Strict);
        variableResolverMock
            .Setup(resolver => resolver.Resolve(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string?>>(), true))
            .Returns<string, IReadOnlyDictionary<string, string?>, bool>((value, variables, _) =>
            {
                return variables.Aggregate(value, (current, variable) => current.Replace($"${{{variable.Key}}}", variable.Value, StringComparison.Ordinal));
            });

        ITopologyParser topologyParser = new TopologyYamlParser(variableResolverMock.Object);
        var yaml = """
        broker:
          managementUrl: ${MANAGEMENT_URL}
          username: ${USERNAME}
          password: ${PASSWORD}
          virtualHosts:
            - ${VHOST_NAME}
        variables:
          MANAGEMENT_URL: http://localhost:15672/api/
          USERNAME: guest
          PASSWORD: guest
          VHOST_NAME: sales
          EXCHANGE_NAME: orders
        virtualHosts:
          - name: ${VHOST_NAME}
            exchanges:
              - name: ${EXCHANGE_NAME}
                type: topic
        """;

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml));
        var topologyDocument = await topologyParser.ParseAsync(stream);

        Assert.Equal("sales", topologyDocument.VirtualHosts.Single().Name);
        Assert.Equal("orders", topologyDocument.VirtualHosts.Single().Exchanges.Single().Name);
        Assert.NotNull(topologyDocument.Broker);
        Assert.Equal("http://localhost:15672/api/", topologyDocument.Broker!.ManagementUrl);
        Assert.Equal("guest", topologyDocument.Broker.Username);
        Assert.Equal("guest", topologyDocument.Broker.Password);
        Assert.Equal("sales", topologyDocument.Broker.VirtualHosts.Single());
        variableResolverMock.Verify(
            resolver => resolver.Resolve(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string?>>(), true),
            Times.AtLeastOnce);
    }
}
