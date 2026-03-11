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
        debugQueues:
          enabled: true
          queueSuffix: dbg
        virtualHosts:
          - name: ${VHOST_NAME}
            exchanges:
              - name: ${EXCHANGE_NAME}
            queues:
              - name: orders.debug.window
                ttl: "00:05:00"
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
    Assert.NotNull(topologyDocument.DebugQueues);
    Assert.True(topologyDocument.DebugQueues!.Enabled);
    Assert.Equal("dbg", topologyDocument.DebugQueues.QueueSuffix);
    Assert.Equal("topic", topologyDocument.VirtualHosts.Single().Exchanges.Single().Type);
    Assert.Equal("00:05:00", topologyDocument.VirtualHosts.Single().Queues.Single().Ttl);
    variableResolverMock.Verify(
        resolver => resolver.Resolve(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string?>>(), true),
        Times.AtLeastOnce);
  }

  [Fact]
  public async Task ParseAsync_Throws_WhenStreamIsNull()
  {
    var variableResolverMock = new Mock<IVariableResolver>(MockBehavior.Strict);
    ITopologyParser topologyParser = new TopologyYamlParser(variableResolverMock.Object);

    await Assert.ThrowsAsync<ArgumentNullException>(() => topologyParser.ParseAsync(null!, CancellationToken.None).AsTask());
  }

  [Fact]
  public async Task ParseAsync_Throws_WhenCancellationIsRequested()
  {
    var variableResolverMock = new Mock<IVariableResolver>(MockBehavior.Strict);
    ITopologyParser topologyParser = new TopologyYamlParser(variableResolverMock.Object);
    await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("virtualHosts: []"));
    using var cancellationTokenSource = new CancellationTokenSource();
    cancellationTokenSource.Cancel();

    await Assert.ThrowsAsync<OperationCanceledException>(() => topologyParser.ParseAsync(stream, cancellationTokenSource.Token).AsTask());
  }

  [Fact]
  public async Task ParseAsync_MapsEmptyYaml_AndPreservesNonStringArguments()
  {
    var variableResolverMock = new Mock<IVariableResolver>(MockBehavior.Strict);
    variableResolverMock
      .Setup(resolver => resolver.Resolve(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string?>>(), true))
      .Returns<string, IReadOnlyDictionary<string, string?>, bool>((value, variables, _) =>
        variables.Aggregate(value, (current, variable) => current.Replace($"${{{variable.Key}}}", variable.Value ?? string.Empty, StringComparison.Ordinal)));

    ITopologyParser topologyParser = new TopologyYamlParser(variableResolverMock.Object);
    var yaml = """
        virtualHosts:
          - name: sales
            queues:
              - name: orders
                arguments:
                  retries: 3
                  enabled: true
                  payload: null
        """;

    await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml));
    var topologyDocument = await topologyParser.ParseAsync(stream);

    var queue = topologyDocument.VirtualHosts.Single().Queues.Single();
    Assert.Equal("orders", queue.Name);
    Assert.True(queue.Arguments.ContainsKey("retries"));
    Assert.True(queue.Arguments.ContainsKey("enabled"));
    Assert.True(queue.Arguments.ContainsKey("payload"));
    Assert.Null(queue.Arguments["payload"]);
  }
}
