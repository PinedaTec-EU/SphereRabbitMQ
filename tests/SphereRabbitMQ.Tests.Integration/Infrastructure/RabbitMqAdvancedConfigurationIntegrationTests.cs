using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SphereRabbitMQ.Abstractions.Publishing;
using SphereRabbitMQ.Abstractions.Subscribers;
using SphereRabbitMQ.DependencyInjection;
using SphereRabbitMQ.Domain.Messaging;
using SphereRabbitMQ.Tests.Integration.Support;

namespace SphereRabbitMQ.Tests.Integration.Infrastructure;

[Collection(RabbitMqIntegrationCollection.CollectionName)]
public sealed class RabbitMqAdvancedConfigurationIntegrationTests
{
    private const string DefaultPublisherExchangeName = "orders";
    private const string DefaultOrderCreatedRoutingKey = "orders.created";
    private const string HighPriorityRoutingKey = "orders.created.high";
    private const string LowPriorityRoutingKey = "orders.created.low";
    private const string MultiRegionEuRoutingKey = "orders.created.eu";
    private const string MultiRegionUsRoutingKey = "orders.created.us";
    private const string OrderCreatedQueueName = "orders.created";
    private const string MultiRouteQueueName = "orders.created.multi";

    private readonly RabbitMqDockerFixture _fixture;

    public RabbitMqAdvancedConfigurationIntegrationTests(RabbitMqDockerFixture fixture)
    {
        _fixture = fixture;
        if (RabbitMqDockerAvailability.IsDockerAvailable() && !_fixture.IsAvailable)
        {
            throw new InvalidOperationException(_fixture.UnavailableReason ?? "RabbitMQ integration fixture is not available.");
        }
    }

    [DockerRequiredFact]
    public async Task MinimalTypedConfiguration_WorksWithDefaults()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        services.AddRabbitPublisher<OrderCreated>(DefaultPublisherExchangeName, DefaultOrderCreatedRoutingKey);
        services.AddRabbitSubscriber<OrderCreated>(
            OrderCreatedQueueName,
            (message, _) =>
            {
                MinimalTypedScenario.Received.TrySetResult(message.Body.OrderId);
                return Task.CompletedTask;
            },
            builder => builder.ErrorHandling.UseDiscard());

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        var publisher = provider.GetRequiredService<IMessagePublisher<OrderCreated>>();
        await PurgeQueuesAsync("orders.created");
        MinimalTypedScenario.Reset();

        await StartHostedServicesAsync(hostedServices);
        try
        {
            await publisher.PublishAsync(new OrderCreated("minimal-order"));
            var orderId = await MinimalTypedScenario.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("minimal-order", orderId);
        }
        finally
        {
            await StopHostedServicesAsync(hostedServices);
        }
    }

    [DockerRequiredFact]
    public async Task RoutingKeyOverride_AndKeyedSubscribers_RouteMessagesToMatchingQueues()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        services.AddRabbitPublisher<OrderCreated>(DefaultPublisherExchangeName, HighPriorityRoutingKey);
        services.AddKeyedRabbitSubscriber<OrderCreated, HighPriorityOrderHandler>(
            "high",
            HighPriorityRoutingKey,
            builder => builder.ErrorHandling.UseDiscard());
        services.AddKeyedRabbitSubscriber<OrderCreated, LowPriorityOrderHandler>(
            "low",
            LowPriorityRoutingKey,
            builder => builder.ErrorHandling.UseDiscard());

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        var publisher = provider.GetRequiredService<IMessagePublisher<OrderCreated>>();
        await PurgeQueuesAsync("orders.created.high", "orders.created.low");
        HighPriorityOrderHandler.Reset();
        LowPriorityOrderHandler.Reset();

        await StartHostedServicesAsync(hostedServices);
        try
        {
            await publisher.PublishAsync(new OrderCreated("high-1"));
            await publisher.PublishAsync(LowPriorityRoutingKey, new OrderCreated("low-1"));

            await HighPriorityOrderHandler.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await LowPriorityOrderHandler.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(["high-1"], HighPriorityOrderHandler.Messages.ToArray());
            Assert.Equal(["low-1"], LowPriorityOrderHandler.Messages.ToArray());
        }
        finally
        {
            await StopHostedServicesAsync(hostedServices);
        }
    }

    [DockerRequiredFact]
    public async Task QueueBoundToMultipleRoutingKeys_DeliversAllMessagesToSameConsumer()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        services.AddRabbitPublisher<OrderCreated>(DefaultPublisherExchangeName, MultiRegionEuRoutingKey);
        services.AddRabbitSubscriber<OrderCreated>(
            MultiRouteQueueName,
            (message, _) =>
            {
                MultiRouteScenario.Messages.Enqueue(message.Body.OrderId);
                if (MultiRouteScenario.Messages.Count == 2)
                {
                    MultiRouteScenario.Received.TrySetResult();
                }

                return Task.CompletedTask;
            },
            builder => builder.ErrorHandling.UseDiscard());

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        var publisher = provider.GetRequiredService<IMessagePublisher<OrderCreated>>();
        await PurgeQueuesAsync("orders.created.multi");
        MultiRouteScenario.Reset();

        await StartHostedServicesAsync(hostedServices);
        try
        {
            await publisher.PublishAsync(new OrderCreated("eu-1"));
            await publisher.PublishAsync(MultiRegionUsRoutingKey, new OrderCreated("us-1"));

            await MultiRouteScenario.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(["eu-1", "us-1"], MultiRouteScenario.Messages.ToArray());
        }
        finally
        {
            await StopHostedServicesAsync(hostedServices);
        }
    }

    private ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSphereRabbitMq(options =>
        {
            options.SetConnectionString(_fixture.CreateConnectionString());
            options.ValidateTopologyOnStartup = true;
        });
        return services;
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

    private static async Task StartHostedServicesAsync(IEnumerable<IHostedService> hostedServices)
    {
        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(CancellationToken.None);
        }
    }

    private static async Task StopHostedServicesAsync(IEnumerable<IHostedService> hostedServices)
    {
        foreach (var hostedService in hostedServices.Reverse())
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    private sealed record OrderCreated(string OrderId);

    private static class MinimalTypedScenario
    {
        public static TaskCompletionSource<string> Received { get; private set; } = CreateSource();

        public static void Reset()
        {
            Received = CreateSource();
        }

        private static TaskCompletionSource<string> CreateSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static class MultiRouteScenario
    {
        public static ConcurrentQueue<string> Messages { get; } = new();

        public static TaskCompletionSource Received { get; private set; } = CreateSource();

        public static void Reset()
        {
            while (Messages.TryDequeue(out _))
            {
            }

            Received = CreateSource();
        }

        private static TaskCompletionSource CreateSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class HighPriorityOrderHandler : IRabbitSubscriberMessageHandler<OrderCreated>
    {
        public static ConcurrentQueue<string> Messages { get; } = new();

        public static TaskCompletionSource Received { get; private set; } = CreateSource();

        public Task HandleAsync(MessageEnvelope<OrderCreated> message, CancellationToken cancellationToken)
        {
            Messages.Enqueue(message.Body.OrderId);
            Received.TrySetResult();
            return Task.CompletedTask;
        }

        public static void Reset()
        {
            while (Messages.TryDequeue(out _))
            {
            }

            Received = CreateSource();
        }

        private static TaskCompletionSource CreateSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class LowPriorityOrderHandler : IRabbitSubscriberMessageHandler<OrderCreated>
    {
        public static ConcurrentQueue<string> Messages { get; } = new();

        public static TaskCompletionSource Received { get; private set; } = CreateSource();

        public Task HandleAsync(MessageEnvelope<OrderCreated> message, CancellationToken cancellationToken)
        {
            Messages.Enqueue(message.Body.OrderId);
            Received.TrySetResult();
            return Task.CompletedTask;
        }

        public static void Reset()
        {
            while (Messages.TryDequeue(out _))
            {
            }

            Received = CreateSource();
        }

        private static TaskCompletionSource CreateSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
