using Microsoft.Extensions.DependencyInjection;
using Moq;
using SphereRabbitMQ.Abstractions.Publishing;
using SphereRabbitMQ.DependencyInjection;
namespace SphereRabbitMQ.Tests.Unit.DependencyInjection;

public sealed class RabbitPublisherServiceCollectionExtensionsTests
{
    private const string OrdersExchangeName = "orders";
    private const string OrderCreatedRoutingKey = "orders.created";
    private const string HighPriorityRoutingKey = "orders.created.high";
    private const string DefaultRoutingKey = "orders.created.default";

    [Fact]
    public void AddRabbitPublisher_Throws_WhenExchangeIsMissing()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddRabbitPublisher<string>(config =>
        {
            config.WithRoutingKey(OrderCreatedRoutingKey);
        }));

        Assert.Equal("Rabbit publisher 'String' requires an exchange.", exception.Message);
    }

    [Fact]
    public void AddRabbitPublisher_AllowsMissingRoutingKey()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IRabbitMQPublisher>().Object);

        services.AddRabbitPublisher<string>(config =>
        {
            config.ToExchange(OrdersExchangeName);
        });

        using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IMessagePublisher<string>>();

        Assert.NotNull(publisher);
    }

    [Fact]
    public async Task AddRabbitPublisher_RegistersTypedPublisherWithConfiguredRoute()
    {
        var rawPublisherMock = new Mock<IRabbitMQPublisher>();
        rawPublisherMock
            .Setup(publisher => publisher.PublishAsync(
                OrdersExchangeName,
                OrderCreatedRoutingKey,
                It.IsAny<OrderCreated>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(rawPublisherMock.Object);
        services.AddRabbitPublisher<OrderCreated>(config =>
        {
            config
                .ToExchange(OrdersExchangeName)
                .WithRoutingKey(OrderCreatedRoutingKey);
        });

        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IMessagePublisher<OrderCreated>>();

        await publisher.PublishAsync(new OrderCreated("order-1"));

        rawPublisherMock.Verify(publisher => publisher.PublishAsync(
            OrdersExchangeName,
            OrderCreatedRoutingKey,
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
                OrdersExchangeName,
                HighPriorityRoutingKey,
                It.IsAny<OrderCreated>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(rawPublisherMock.Object);
        services.AddRabbitPublisher<OrderCreated>(
            config => config.ToExchange(OrdersExchangeName).WithRoutingKey(DefaultRoutingKey));

        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IMessagePublisher<OrderCreated>>();

        await publisher.PublishAsync(HighPriorityRoutingKey, new OrderCreated("order-2"));

        rawPublisherMock.Verify(publisher => publisher.PublishAsync(
            OrdersExchangeName,
            HighPriorityRoutingKey,
            It.Is<OrderCreated>(message => message.OrderId == "order-2"),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_Throws_WhenDefaultRoutingKeyIsMissing()
    {
        var rawPublisherMock = new Mock<IRabbitMQPublisher>(MockBehavior.Strict);

        var services = new ServiceCollection();
        services.AddSingleton(rawPublisherMock.Object);
        services.AddRabbitPublisher<OrderCreated>(config =>
        {
            config.ToExchange(OrdersExchangeName);
        });

        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IMessagePublisher<OrderCreated>>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(new OrderCreated("order-3")));

        Assert.Equal(
            "Rabbit publisher 'OrderCreated' does not have a default routing key configured. Use PublishAsync(routingKey, message, ...) or configure WithRoutingKey(...).",
            exception.Message);
        rawPublisherMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task PublishAsync_Throws_WhenRoutingKeyOverrideIsBlank(string routingKey)
    {
        var rawPublisherMock = new Mock<IRabbitMQPublisher>(MockBehavior.Strict);

        var services = new ServiceCollection();
        services.AddSingleton(rawPublisherMock.Object);
        services.AddRabbitPublisher<OrderCreated>(config =>
        {
            config.ToExchange(OrdersExchangeName);
        });

        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IMessagePublisher<OrderCreated>>();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => publisher.PublishAsync(routingKey, new OrderCreated("order-4")));

        Assert.Equal("routingKey", exception.ParamName);
        rawPublisherMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PublishAsync_AllowsRoutingKeyOverride_WhenDefaultRoutingKeyIsMissing()
    {
        var rawPublisherMock = new Mock<IRabbitMQPublisher>();
        rawPublisherMock
            .Setup(publisher => publisher.PublishAsync(
                OrdersExchangeName,
                HighPriorityRoutingKey,
                It.IsAny<OrderCreated>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(rawPublisherMock.Object);
        services.AddRabbitPublisher<OrderCreated>(config =>
        {
            config.ToExchange(OrdersExchangeName);
        });

        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IMessagePublisher<OrderCreated>>();

        await publisher.PublishAsync(HighPriorityRoutingKey, new OrderCreated("order-5"));

        rawPublisherMock.Verify(publisher => publisher.PublishAsync(
            OrdersExchangeName,
            HighPriorityRoutingKey,
            It.Is<OrderCreated>(message => message.OrderId == "order-5"),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed record OrderCreated(string OrderId);
}
