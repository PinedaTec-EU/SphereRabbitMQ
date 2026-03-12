using System.Reflection;
using System.Runtime.Serialization;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.Abstractions.Topology;
using SphereRabbitMQ.Domain.Topology;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Connection;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Topology;

namespace SphereRabbitMQ.Tests.Unit.Infrastructure;

public sealed class RabbitMqTopologyValidatorTests
{
    [Fact]
    public async Task ValidateAsync_DeclaresExpectedExchangesAndQueues_InSortedOrder()
    {
        var channelMock = new Mock<IChannel>(MockBehavior.Strict);
        channelMock.Setup(channel => channel.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var sequence = new MockSequence();
        channelMock.InSequence(sequence)
            .Setup(channel => channel.ExchangeDeclarePassiveAsync("a.exchange", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        channelMock.InSequence(sequence)
            .Setup(channel => channel.ExchangeDeclarePassiveAsync("z.exchange", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        channelMock.InSequence(sequence)
            .Setup(channel => channel.QueueDeclarePassiveAsync("a.queue", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueDeclareOk("a.queue", 0, 0));
        channelMock.InSequence(sequence)
            .Setup(channel => channel.QueueDeclarePassiveAsync("z.queue", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueDeclareOk("z.queue", 0, 0));

        var validator = CreateValidator(
            CreateConnectionProvider(channelMock.Object),
            new SphereRabbitMqOptions
            {
                ExpectedTopology = new TopologyExpectation(
                    Exchanges: ["z.exchange", "a.exchange"],
                    Queues: ["z.queue", "a.queue"]),
            },
            derivedExpectation: new TopologyExpectation(Array.Empty<string>(), Array.Empty<string>()));

        await validator.ValidateAsync(CancellationToken.None);

        channelMock.Verify(channel => channel.ExchangeDeclarePassiveAsync("a.exchange", It.IsAny<CancellationToken>()), Times.Once);
        channelMock.Verify(channel => channel.ExchangeDeclarePassiveAsync("z.exchange", It.IsAny<CancellationToken>()), Times.Once);
        channelMock.Verify(channel => channel.QueueDeclarePassiveAsync("a.queue", It.IsAny<CancellationToken>()), Times.Once);
        channelMock.Verify(channel => channel.QueueDeclarePassiveAsync("z.queue", It.IsAny<CancellationToken>()), Times.Once);
        channelMock.Verify(channel => channel.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task ValidateAsync_ThrowsInvalidOperationException_WhenExpectedExchangeIsMissing()
    {
        var interrupted = CreateInterruptedException();
        var channelMock = new Mock<IChannel>(MockBehavior.Strict);
        channelMock.Setup(channel => channel.DisposeAsync()).Returns(ValueTask.CompletedTask);
        channelMock
            .Setup(channel => channel.ExchangeDeclarePassiveAsync("orders", It.IsAny<CancellationToken>()))
            .ThrowsAsync(interrupted);

        var validator = CreateValidator(
            CreateConnectionProvider(channelMock.Object),
            new SphereRabbitMqOptions
            {
                ExpectedTopology = new TopologyExpectation(
                    Exchanges: ["orders"],
                    Queues: Array.Empty<string>()),
            },
            derivedExpectation: new TopologyExpectation(Array.Empty<string>(), Array.Empty<string>()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(CancellationToken.None));

        Assert.Equal("Expected exchange 'orders' does not exist in RabbitMQ. SphereRabbitMQ will not declare topology automatically.", exception.Message);
        Assert.Same(interrupted, exception.InnerException);
    }

    [Fact]
    public async Task ValidateAsync_ThrowsInvalidOperationException_WhenExpectedQueueIsMissing()
    {
        var interrupted = CreateInterruptedException();
        var channelMock = new Mock<IChannel>(MockBehavior.Strict);
        channelMock.Setup(channel => channel.DisposeAsync()).Returns(ValueTask.CompletedTask);
        channelMock
            .Setup(channel => channel.ExchangeDeclarePassiveAsync("orders", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        channelMock
            .Setup(channel => channel.QueueDeclarePassiveAsync("orders.created", It.IsAny<CancellationToken>()))
            .ThrowsAsync(interrupted);

        var validator = CreateValidator(
            CreateConnectionProvider(channelMock.Object),
            new SphereRabbitMqOptions
            {
                ExpectedTopology = new TopologyExpectation(
                    Exchanges: ["orders"],
                    Queues: ["orders.created"]),
            },
            derivedExpectation: new TopologyExpectation(Array.Empty<string>(), Array.Empty<string>()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(CancellationToken.None));

        Assert.Equal("Expected queue 'orders.created' does not exist in RabbitMQ. SphereRabbitMQ will not declare topology automatically.", exception.Message);
        Assert.Same(interrupted, exception.InnerException);
    }

    [Fact]
    public async Task ValidateAsync_DerivesSubscriberTopology_WhenStartupValidationIsEnabled()
    {
        var channelMock = new Mock<IChannel>(MockBehavior.Strict);
        channelMock.Setup(channel => channel.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var sequence = new MockSequence();
        channelMock.InSequence(sequence)
            .Setup(channel => channel.ExchangeDeclarePassiveAsync("orders.created.dlx", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        channelMock.InSequence(sequence)
            .Setup(channel => channel.ExchangeDeclarePassiveAsync("orders.created.retry", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        channelMock.InSequence(sequence)
            .Setup(channel => channel.QueueDeclarePassiveAsync("orders.created", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueDeclareOk("orders.created", 0, 0));
        channelMock.InSequence(sequence)
            .Setup(channel => channel.QueueDeclarePassiveAsync("orders.created.dlq", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueDeclareOk("orders.created.dlq", 0, 0));
        channelMock.InSequence(sequence)
            .Setup(channel => channel.QueueDeclarePassiveAsync("orders.created.retry.step1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueDeclareOk("orders.created.retry.step1", 0, 0));

        var validator = CreateValidator(
            CreateConnectionProvider(channelMock.Object),
            new SphereRabbitMqOptions(),
            new TopologyExpectation(
                Exchanges: ["orders.created.retry", "orders.created.dlx"],
                Queues: ["orders.created", "orders.created.retry.step1", "orders.created.dlq"]));

        await validator.ValidateAsync(CancellationToken.None);

        channelMock.Verify(channel => channel.ExchangeDeclarePassiveAsync("orders.created.retry", It.IsAny<CancellationToken>()), Times.Once);
        channelMock.Verify(channel => channel.ExchangeDeclarePassiveAsync("orders.created.dlx", It.IsAny<CancellationToken>()), Times.Once);
        channelMock.Verify(channel => channel.QueueDeclarePassiveAsync("orders.created", It.IsAny<CancellationToken>()), Times.Once);
        channelMock.Verify(channel => channel.QueueDeclarePassiveAsync("orders.created.retry.step1", It.IsAny<CancellationToken>()), Times.Once);
        channelMock.Verify(channel => channel.QueueDeclarePassiveAsync("orders.created.dlq", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static IRabbitMqTopologyValidator CreateValidator(
        RabbitMqConnectionProvider connectionProvider,
        SphereRabbitMqOptions options,
        TopologyExpectation derivedExpectation)
    {
        var expectationProviderMock = new Mock<ISubscriberTopologyExpectationProvider>();
        expectationProviderMock
            .Setup(provider => provider.BuildExpectation())
            .Returns(derivedExpectation);

        return new RabbitMqTopologyValidator(
            connectionProvider,
            Options.Create(options),
            expectationProviderMock.Object,
            NullLogger<RabbitMqTopologyValidator>.Instance);
    }

    private static RabbitMqConnectionProvider CreateConnectionProvider(IChannel channel)
    {
        var connectionMock = new Mock<IConnection>(MockBehavior.Strict);
        connectionMock.SetupGet(connection => connection.IsOpen).Returns(true);
        connectionMock
            .Setup(connection => connection.CreateChannelAsync(It.IsAny<CreateChannelOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel);

        var provider = new RabbitMqConnectionProvider(
            Options.Create(new SphereRabbitMqOptions()),
            NullLogger<RabbitMqConnectionProvider>.Instance);

        var field = typeof(RabbitMqConnectionProvider).GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(provider, connectionMock.Object);

        return provider;
    }

    private static OperationInterruptedException CreateInterruptedException()
    {
#pragma warning disable SYSLIB0050
        return (OperationInterruptedException)FormatterServices.GetUninitializedObject(typeof(OperationInterruptedException));
#pragma warning restore SYSLIB0050
    }
}
