using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SphereRabbitMQ.Abstractions.Publishing;
using SphereRabbitMQ.DependencyInjection;
using SphereRabbitMQ.Tests.Integration.Support;

namespace SphereRabbitMQ.Tests.Integration.Infrastructure;

[Collection(RabbitMqIntegrationCollection.CollectionName)]
public sealed class RabbitMqTypedPublisherIntegrationTests
{
    private const string PublisherExchangeName = "orders";
    private const string OrderCreatedRoutingKey = "orders.created";

    private readonly RabbitMqDockerFixture _fixture;

    public RabbitMqTypedPublisherIntegrationTests(RabbitMqDockerFixture fixture)
    {
        _fixture = fixture;
        if (RabbitMqDockerAvailability.IsDockerAvailable() && !_fixture.IsAvailable)
        {
            throw new InvalidOperationException(_fixture.UnavailableReason ?? "RabbitMQ integration fixture is not available.");
        }
    }

    [DockerRequiredFact]
    public async Task PublishAsync_UsesPreconfiguredRoute()
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
        });
        services.AddRabbitPublisher<OrderCreated>(config =>
        {
            config
                .ToExchange(PublisherExchangeName)
                .WithRoutingKey(OrderCreatedRoutingKey);
        });

        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IMessagePublisher<OrderCreated>>();
        await PurgeQueuesAsync("orders.created");

        await publisher.PublishAsync(new OrderCreated("typed-order"));

        Assert.Equal(1u, await CountMessagesAsync("orders.created"));
    }

    private async Task PurgeQueuesAsync(params string[] queues)
    {
        await using var connection = await _fixture.CreateConnectionFactory().CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        foreach (var queue in queues)
        {
            await channel.QueuePurgeAsync(queue, CancellationToken.None);
        }
    }

    private async Task<uint> CountMessagesAsync(string queueName)
    {
        await using var connection = await _fixture.CreateConnectionFactory().CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var declareOk = await channel.QueueDeclarePassiveAsync(queueName, CancellationToken.None);
        return declareOk.MessageCount;
    }

    private sealed record OrderCreated(string OrderId);
}
