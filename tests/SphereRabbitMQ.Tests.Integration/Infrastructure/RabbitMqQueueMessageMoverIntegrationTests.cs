using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using SphereRabbitMQ.Abstractions.Publishing;
using SphereRabbitMQ.DependencyInjection;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Migration.Interfaces;
using SphereRabbitMQ.Tests.Integration.Support;

namespace SphereRabbitMQ.Tests.Integration.Infrastructure;

[Collection(RabbitMqIntegrationCollection.CollectionName)]
public sealed class RabbitMqQueueMessageMoverIntegrationTests
{
    private readonly RabbitMqDockerFixture _fixture;

    public RabbitMqQueueMessageMoverIntegrationTests(RabbitMqDockerFixture fixture)
    {
        _fixture = fixture;
        if (RabbitMqDockerAvailability.IsDockerAvailable() && !_fixture.IsAvailable)
        {
            throw new InvalidOperationException(_fixture.UnavailableReason ?? "RabbitMQ integration fixture is not available.");
        }
    }

    [Fact]
    public async Task MoveAsync_MovesMessagesBetweenExistingQueues()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSphereRabbitMq(options =>
        {
            options.SetConnectionString(_fixture.CreateConnectionString());
            options.ValidateTopologyOnStartup = false;
        });

        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        var mover = provider.GetRequiredService<IQueueMessageMover>();

        await PurgeQueueAsync("orders.created");
        await PurgeQueueAsync("orders.created.migration");

        await publisher.PublishAsync("orders", "orders.created", new OrderCreated("move-1"));
        await publisher.PublishAsync("orders", "orders.created", new OrderCreated("move-2"));

        var result = await mover.MoveAsync("orders.created", "orders.created.migration");

        Assert.Equal(2, result.MovedMessagesCount);
        Assert.Equal(0u, await CountMessagesAsync("orders.created"));
        Assert.Equal(2u, await CountMessagesAsync("orders.created.migration"));
    }

    private async Task<uint> CountMessagesAsync(string queueName)
    {
        var factory = _fixture.CreateConnectionFactory();
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var result = await channel.QueueDeclarePassiveAsync(queueName);
        return result.MessageCount;
    }

    private async Task PurgeQueueAsync(string queueName)
    {
        var factory = _fixture.CreateConnectionFactory();
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueuePurgeAsync(queueName);
    }

    public sealed record OrderCreated(string OrderId);
}
