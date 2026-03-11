using SphereRabbitMQ.IaC.Application.Validation;
using SphereRabbitMQ.IaC.Application.Validation.Interfaces;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Tests.Unit.Application.Validation;

public sealed class TopologyValidationServiceTests
{
    [Fact]
    public async Task ValidateAsync_ReturnsSuccess_WhenDefinitionIsValid()
    {
        ITopologyValidator validator = new TopologyValidationService();
        var definition = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                "sales",
                exchanges: [new ExchangeDefinition("orders", ExchangeType.Topic)],
                queues: [new QueueDefinition("orders.created")],
                bindings: [new BindingDefinition("orders", "orders.created", routingKey: "orders.created")]),
        ]);

        var result = await validator.ValidateAsync(definition);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsDomainValidationIssues_WhenDefinitionIsInvalid()
    {
        ITopologyValidator validator = new TopologyValidationService();
        var definition = new TopologyDefinition(
        [
            new VirtualHostDefinition(
                "sales",
                bindings: [new BindingDefinition("missing.exchange", "missing.queue", routingKey: "orders.created")]),
        ]);

        var result = await validator.ValidateAsync(definition);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "missing-source-exchange");
        Assert.Contains(result.Issues, issue => issue.Code == "missing-binding-destination");
    }

    [Fact]
    public async Task ValidateAsync_Throws_WhenCancellationIsRequested()
    {
        ITopologyValidator validator = new TopologyValidationService();
        var definition = new TopologyDefinition([]);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => validator.ValidateAsync(definition, cancellationTokenSource.Token).AsTask());
    }

    [Fact]
    public async Task ValidateAsync_Throws_WhenDefinitionIsNull()
    {
        ITopologyValidator validator = new TopologyValidationService();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => validator.ValidateAsync(null!, CancellationToken.None).AsTask());
    }
}
