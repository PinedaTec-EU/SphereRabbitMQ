using Microsoft.Extensions.DependencyInjection;
using Moq;
using SphereRabbitMQ.Abstractions.Publishing;
using SphereRabbitMQ.DependencyInjection;
namespace SphereRabbitMQ.Tests.Unit.DependencyInjection;

public sealed class RabbitPublisherServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRabbitPublisher_Throws_WhenExchangeIsMissing()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddRabbitPublisher<string>(config =>
        {
            config.WithRoutingKey("orders.created");
        }));

        Assert.Equal("Rabbit publisher 'String' requires an exchange.", exception.Message);
    }

    [Fact]
    public void AddRabbitPublisher_Throws_WhenRoutingKeyIsMissing()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddRabbitPublisher<string>(config =>
        {
            config.ToExchange("orders");
        }));

        Assert.Equal("Rabbit publisher 'String' requires a routing key.", exception.Message);
    }

    [Fact]
    public async Task AddRabbitPublisher_RegistersTypedPublisherWithConfiguredRoute()
    {
        var rawPublisherMock = new Mock<IRabbitMQPublisher>();
        rawPublisherMock
            .Setup(publisher => publisher.PublishAsync(
                "orders",
                "orders.created",
                It.IsAny<OrderCreated>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(rawPublisherMock.Object);
        services.AddRabbitPublisher<OrderCreated>(config =>
        {
            config
                .ToExchange("orders")
                .WithRoutingKey("orders.created");
        });

        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IMessagePublisher<OrderCreated>>();

        await publisher.PublishAsync(new OrderCreated("order-1"));

        rawPublisherMock.Verify(publisher => publisher.PublishAsync(
            "orders",
            "orders.created",
            It.Is<OrderCreated>(message => message.OrderId == "order-1"),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_AllowsRoutingKeyOverride()
    {
        var rawPublisherMock = new Mock<IRabbitMQPublisher>();
        rawPublisherMock
            .Setup(publisher => publisher.PublishAsync(
                "orders",
                "orders.created.high",
                It.IsAny<OrderCreated>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(rawPublisherMock.Object);
        services.AddRabbitPublisher<OrderCreated>(
            config => config.ToExchange("orders").WithRoutingKey("orders.created.default"));

        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IMessagePublisher<OrderCreated>>();

        await publisher.PublishAsync("orders.created.high", new OrderCreated("order-2"));

        rawPublisherMock.Verify(publisher => publisher.PublishAsync(
            "orders",
            "orders.created.high",
            It.Is<OrderCreated>(message => message.OrderId == "order-2"),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed record OrderCreated(string OrderId);
}
