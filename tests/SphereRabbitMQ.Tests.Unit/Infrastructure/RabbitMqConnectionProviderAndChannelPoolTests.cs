using System.Reflection;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using RabbitMQ.Client;

using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Connection;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Publishing;

namespace SphereRabbitMQ.Tests.Unit.Infrastructure;

public sealed class RabbitMqConnectionProviderAndChannelPoolTests
{
    [Fact]
    public async Task GetConnectionAsync_ReturnsSameOpenConnection_ForConcurrentCalls()
    {
        var connectionMock = new Mock<IConnection>(MockBehavior.Strict);
        connectionMock.SetupGet(connection => connection.IsOpen).Returns(true);

        var provider = new RabbitMqConnectionProvider(
            Options.Create(new SphereRabbitMqOptions()),
            NullLogger<RabbitMqConnectionProvider>.Instance);

        SetPrivateField(provider, "_connection", connectionMock.Object);

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => provider.GetConnectionAsync(CancellationToken.None).AsTask())
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.All(tasks, task => Assert.Same(connectionMock.Object, task.Result));
    }

    [Fact]
    public async Task RentAsync_ReusesSameOpenChannel_AcrossLeases()
    {
        var channelMock = new Mock<IChannel>(MockBehavior.Strict);
        channelMock.SetupGet(channel => channel.IsOpen).Returns(true);

        var pool = CreateChannelPoolWithOpenConnection(channelMock.Object);

        var lease1 = await pool.RentAsync(CancellationToken.None);
        var firstChannel = lease1.Channel;
        await lease1.DisposeAsync();

        var lease2 = await pool.RentAsync(CancellationToken.None);
        var secondChannel = lease2.Channel;
        await lease2.DisposeAsync();

        Assert.Same(firstChannel, secondChannel);
    }

    private static RabbitMqChannelPool CreateChannelPoolWithOpenConnection(IChannel channel)
    {
        var connectionMock = new Mock<IConnection>(MockBehavior.Strict);
        connectionMock.SetupGet(connection => connection.IsOpen).Returns(true);
        connectionMock
            .Setup(connection => connection.CreateChannelAsync(It.IsAny<CreateChannelOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel);

        var provider = new RabbitMqConnectionProvider(
            Options.Create(new SphereRabbitMqOptions()),
            NullLogger<RabbitMqConnectionProvider>.Instance);

        SetPrivateField(provider, "_connection", connectionMock.Object);

        return new RabbitMqChannelPool(
            provider,
            Options.Create(new SphereRabbitMqOptions()),
            NullLogger<RabbitMqChannelPool>.Instance);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}
