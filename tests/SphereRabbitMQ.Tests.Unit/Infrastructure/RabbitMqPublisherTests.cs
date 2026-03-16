using System.Reflection;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using RabbitMQ.Client;

using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.Abstractions.Publishing;
using SphereRabbitMQ.Abstractions.Serialization;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Connection;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Publishing;

namespace SphereRabbitMQ.Tests.Unit.Infrastructure;

public sealed class RabbitMqPublisherTests
{
    [Fact]
    public async Task PublishAsync_SetsPerMessageExpiration_WhenTimeToLiveIsProvided()
    {
        var channelMock = new Mock<IChannel>(MockBehavior.Strict);
        channelMock.SetupGet(channel => channel.IsOpen).Returns(true);
        channelMock
            .Setup(channel => channel.ExchangeDeclarePassiveAsync("orders", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        BasicProperties? capturedProperties = null;
        channelMock
            .Setup(channel => channel.BasicPublishAsync(
                "orders",
                "orders.created.retry",
                true,
                It.IsAny<BasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, BasicProperties, ReadOnlyMemory<byte>, CancellationToken>((_, _, _, properties, _, _) => capturedProperties = properties)
            .Returns(ValueTask.CompletedTask);

        var publisher = CreatePublisher(channelMock.Object);

        await publisher.PublishAsync(
            "orders",
            "orders.created.retry",
            "order-1",
            new PublishOptions
            {
                TimeToLive = TimeSpan.FromMilliseconds(750),
            },
            CancellationToken.None);

        Assert.NotNull(capturedProperties);
        Assert.Equal("750", capturedProperties!.Expiration);
    }

    private static RabbitMqPublisher CreatePublisher(IChannel channel)
    {
        var connectionMock = new Mock<IConnection>(MockBehavior.Strict);
        connectionMock.SetupGet(connection => connection.IsOpen).Returns(true);
        connectionMock
            .Setup(connection => connection.CreateChannelAsync(It.IsAny<CreateChannelOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel);

        var connectionProvider = new RabbitMqConnectionProvider(
            Options.Create(new SphereRabbitMqOptions()),
            NullLogger<RabbitMqConnectionProvider>.Instance);

        SetPrivateField(connectionProvider, "_connection", connectionMock.Object);

        var channelPool = new RabbitMqChannelPool(
            connectionProvider,
            Options.Create(new SphereRabbitMqOptions()),
            NullLogger<RabbitMqChannelPool>.Instance);

        var serializerMock = new Mock<IMessageSerializer>(MockBehavior.Strict);
        serializerMock.SetupGet(serializer => serializer.ContentType).Returns("application/json");
        serializerMock
            .Setup(serializer => serializer.Serialize(It.IsAny<string>()))
            .Returns(ReadOnlyMemory<byte>.Empty);

        return new RabbitMqPublisher(
            channelPool,
            serializerMock.Object,
            [],
            Options.Create(new SphereRabbitMqOptions()),
            NullLogger<RabbitMqPublisher>.Instance);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}
