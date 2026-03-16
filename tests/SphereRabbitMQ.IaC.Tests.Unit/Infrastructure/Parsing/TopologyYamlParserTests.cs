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
                debugQueue: true
            queues:
              - name: orders.debug.window
                ttl: "00:05:00"
                debugQueue: true
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
    Assert.True(topologyDocument.VirtualHosts.Single().Exchanges.Single().DebugQueue);
    Assert.Equal("00:05:00", topologyDocument.VirtualHosts.Single().Queues.Single().Ttl);
    Assert.True(topologyDocument.VirtualHosts.Single().Queues.Single().DebugQueue);
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

  [Fact]
  public async Task ParseAsync_MapsDecommissionSection_AndResolvesVariables()
  {
    var variableResolverMock = new Mock<IVariableResolver>(MockBehavior.Strict);
    variableResolverMock
      .Setup(resolver => resolver.Resolve(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string?>>(), true))
      .Returns<string, IReadOnlyDictionary<string, string?>, bool>((value, variables, _) =>
        variables.Aggregate(value, (current, variable) => current.Replace($"${{{variable.Key}}}", variable.Value ?? string.Empty, StringComparison.Ordinal)));

    ITopologyParser topologyParser = new TopologyYamlParser(variableResolverMock.Object);
    var yaml = """
        variables:
          VHOST_NAME: sales
          LEGACY_EXCHANGE: orders.legacy
        virtualHosts:
          - name: ${VHOST_NAME}
            exchanges:
              - name: orders.current
        decommission:
          virtualHosts:
            - name: ${VHOST_NAME}
              exchanges:
                - ${LEGACY_EXCHANGE}
              queues:
                - orders.legacy.queue
              bindings:
                - sourceExchange: ${LEGACY_EXCHANGE}
                  destination: orders.legacy.queue
                  destinationType: queue
                  routingKey: orders.legacy
        """;

    await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml));
    var topologyDocument = await topologyParser.ParseAsync(stream);

    Assert.NotNull(topologyDocument.Decommission);
    var decommission = Assert.Single(topologyDocument.Decommission!.VirtualHosts);
    Assert.Equal("sales", decommission.Name);
    Assert.Equal("orders.legacy", Assert.Single(decommission.Exchanges));
    Assert.Equal("orders.legacy.queue", Assert.Single(decommission.Queues));
    var binding = Assert.Single(decommission.Bindings);
    Assert.Equal("orders.legacy", binding.SourceExchange);
    Assert.Equal("orders.legacy.queue", binding.Destination);
    Assert.Equal("queue", binding.DestinationType);
    Assert.Equal("orders.legacy", binding.RoutingKey);
  }

  [Fact]
  public async Task ParseAsync_MapsDeadLetterQueueDestination()
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
              - name: orders.delay
                deadLetter:
                  destinationType: queue
                  queueName: orders.consume
                  ttl: "00:05:00"
              - name: orders.consume
        """;

    await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml));
    var topologyDocument = await topologyParser.ParseAsync(stream);

    var deadLetter = topologyDocument.VirtualHosts.Single().Queues.Single(queue => queue.Name == "orders.delay").DeadLetter;
    Assert.NotNull(deadLetter);
    Assert.Equal("queue", deadLetter!.DestinationType);
    Assert.Equal("orders.consume", deadLetter.QueueName);
    Assert.Equal("00:05:00", deadLetter.Ttl);
  }
}
