using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.Abstractions.Consumers;
using SphereRabbitMQ.Abstractions.Publishing;
using SphereRabbitMQ.DependencyInjection;
using SphereRabbitMQ.Domain.Consumers;
using SphereRabbitMQ.Domain.Messaging;
using RabbitMQ.Client.Exceptions;
using SphereRabbitMQ.Tests.Integration.Support;

namespace SphereRabbitMQ.Tests.Integration.Infrastructure;

[Collection(RabbitMqIntegrationCollection.CollectionName)]
public sealed class RabbitMqRuntimeIntegrationTests
{
    private readonly RabbitMqDockerFixture _fixture;

    public RabbitMqRuntimeIntegrationTests(RabbitMqDockerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PublishAndConsumeAsync_WorksAgainstRealBroker()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IPublisher>();
        var subscriber = provider.GetRequiredService<ISubscriber>();
        await PurgeQueuesAsync("orders.created", "orders.created.retry", "orders.created.dlq");
        var received = new TaskCompletionSource<MessageEnvelope<OrderCreated>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var subscription = Task.Run(() => subscriber.SubscribeAsync(
            new ConsumerDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                PrefetchCount = 5,
                MaxConcurrency = 2,
                Handler = (message, _) =>
                {
                    received.TrySetResult(message);
                    return Task.CompletedTask;
                },
            },
            cts.Token));

        await publisher.PublishAsync("orders", "orders.created", new OrderCreated("order-1"));
        var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();
        await IgnoreCancellationAsync(subscription);

        Assert.Equal("order-1", envelope.Body.OrderId);
    }

    [Fact]
    public async Task RetryRoutingAsync_RequeuesThroughBrokerRetryTopology()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IPublisher>();
        var subscriber = provider.GetRequiredService<ISubscriber>();
        await PurgeQueuesAsync("orders.created", "orders.created.retry", "orders.created.dlq");
        var attempts = 0;
        var completed = new TaskCompletionSource<MessageEnvelope<OrderCreated>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var subscription = Task.Run(() => subscriber.SubscribeAsync(
            new ConsumerDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                PrefetchCount = 1,
                MaxConcurrency = 1,
                ErrorHandling = new ConsumerErrorHandlingSettings
                {
                    Strategy = ConsumerErrorStrategyKind.RetryOnly,
                    MaxRetryAttempts = 1,
                    RetryRoute = new RetryRouteDefinition("orders.retry", "orders.created.retry"),
                },
                Handler = (message, _) =>
                {
                    attempts++;
                    if (attempts == 1)
                    {
                        throw new InvalidOperationException("first attempt fails");
                    }

                    completed.TrySetResult(message);
                    return Task.CompletedTask;
                },
            },
            cts.Token));

        await publisher.PublishAsync("orders", "orders.created", new OrderCreated("order-2"));
        var envelope = await completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        cts.Cancel();
        await IgnoreCancellationAsync(subscription);

        Assert.Equal(2, attempts);
        Assert.Equal(1, Convert.ToInt32(envelope.Headers["x-retry-count"]));
    }

    [Fact]
    public async Task DeadLetterAsync_ForwardsFailedMessagesToConfiguredDeadLetterRoute()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IPublisher>();
        var subscriber = provider.GetRequiredService<ISubscriber>();
        await PurgeQueuesAsync("orders.created", "orders.created.retry", "orders.created.dlq");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var subscription = Task.Run(() => subscriber.SubscribeAsync(
            new ConsumerDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                PrefetchCount = 1,
                MaxConcurrency = 1,
                ErrorHandling = new ConsumerErrorHandlingSettings
                {
                    Strategy = ConsumerErrorStrategyKind.DeadLetterOnly,
                    DeadLetterRoute = new DeadLetterRouteDefinition("orders.dlx", "orders.created.dlq"),
                },
                Handler = (_, _) => throw new InvalidOperationException("force dlq"),
            },
            cts.Token));

        await publisher.PublishAsync("orders", "orders.created", new OrderCreated("order-3"));
        var dlqMessage = await WaitForDlqMessageAsync();
        cts.Cancel();
        await IgnoreCancellationAsync(subscription);

        Assert.NotNull(dlqMessage);
    }

    [Fact]
    public async Task ConsumerConcurrency_IsBoundedByMaxConcurrency()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IPublisher>();
        var subscriber = provider.GetRequiredService<ISubscriber>();
        await PurgeQueuesAsync("orders.created", "orders.created.retry", "orders.created.dlq");
        var started = 0;
        var maxObserved = 0;
        var completed = 0;
        var allProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var subscription = Task.Run(() => subscriber.SubscribeAsync(
            new ConsumerDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                PrefetchCount = 5,
                MaxConcurrency = 2,
                Handler = async (_, cancellationToken) =>
                {
                    var running = Interlocked.Increment(ref started);
                    UpdateMax(ref maxObserved, running);

                    try
                    {
                        await Task.Delay(250, cancellationToken);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref started);
                    }

                    if (Interlocked.Increment(ref completed) == 4)
                    {
                        allProcessed.TrySetResult();
                    }
                },
            },
            cts.Token));

        for (var index = 0; index < 4; index++)
        {
            await publisher.PublishAsync("orders", "orders.created", new OrderCreated($"order-c-{index}"));
        }

        await allProcessed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        cts.Cancel();
        await IgnoreCancellationAsync(subscription);

        Assert.Equal(2, maxObserved);
    }

    [Fact]
    public async Task PublishAsync_FailsExplicitly_WhenExchangeDoesNotExist()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IPublisher>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            publisher.PublishAsync("orders.missing", "orders.created", new OrderCreated("missing-exchange")));

        Assert.Contains("does not exist", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubscribeAsync_FailsExplicitly_WhenQueueDoesNotExist()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var subscriber = provider.GetRequiredService<ISubscriber>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => subscriber.SubscribeAsync(
            new ConsumerDefinition<OrderCreated>
            {
                QueueName = "orders.created.missing",
                Handler = (_, _) => Task.CompletedTask,
            }));

        Assert.Contains("does not exist", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubscribeAsync_FailsExplicitly_WhenRetryExchangeDoesNotExist()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var subscriber = provider.GetRequiredService<ISubscriber>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => subscriber.SubscribeAsync(
            new ConsumerDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                ErrorHandling = new ConsumerErrorHandlingSettings
                {
                    Strategy = ConsumerErrorStrategyKind.RetryOnly,
                    RetryRoute = new RetryRouteDefinition("orders.retry.missing", "orders.created.retry", "orders.created.retry"),
                },
                Handler = (_, _) => Task.CompletedTask,
            }));

        Assert.Contains("orders.retry.missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubscribeAsync_FailsExplicitly_WhenRetryQueueDoesNotExist()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var subscriber = provider.GetRequiredService<ISubscriber>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => subscriber.SubscribeAsync(
            new ConsumerDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                ErrorHandling = new ConsumerErrorHandlingSettings
                {
                    Strategy = ConsumerErrorStrategyKind.RetryOnly,
                    RetryRoute = new RetryRouteDefinition("orders.retry", "orders.created.retry", "orders.created.retry.missing"),
                },
                Handler = (_, _) => Task.CompletedTask,
            }));

        Assert.Contains("orders.created.retry.missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubscribeAsync_FailsExplicitly_WhenDeadLetterExchangeDoesNotExist()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var subscriber = provider.GetRequiredService<ISubscriber>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => subscriber.SubscribeAsync(
            new ConsumerDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                ErrorHandling = new ConsumerErrorHandlingSettings
                {
                    Strategy = ConsumerErrorStrategyKind.DeadLetterOnly,
                    DeadLetterRoute = new DeadLetterRouteDefinition("orders.dlx.missing", "orders.created.dlq", "orders.created.dlq"),
                },
                Handler = (_, _) => Task.CompletedTask,
            }));

        Assert.Contains("orders.dlx.missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubscribeAsync_FailsExplicitly_WhenDeadLetterQueueDoesNotExist()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var subscriber = provider.GetRequiredService<ISubscriber>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => subscriber.SubscribeAsync(
            new ConsumerDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                ErrorHandling = new ConsumerErrorHandlingSettings
                {
                    Strategy = ConsumerErrorStrategyKind.DeadLetterOnly,
                    DeadLetterRoute = new DeadLetterRouteDefinition("orders.dlx", "orders.created.dlq", "orders.created.dlq.missing"),
                },
                Handler = (_, _) => Task.CompletedTask,
            }));

        Assert.Contains("orders.created.dlq.missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonRetriableMessageException_SendsDirectlyToDeadLetter()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IPublisher>();
        var subscriber = provider.GetRequiredService<ISubscriber>();
        await PurgeQueuesAsync("orders.created", "orders.created.retry", "orders.created.dlq");
        var attempts = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var subscription = Task.Run(() => subscriber.SubscribeAsync(
            new ConsumerDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                ErrorHandling = new ConsumerErrorHandlingSettings
                {
                    Strategy = ConsumerErrorStrategyKind.RetryThenDeadLetter,
                    MaxRetryAttempts = 5,
                    RetryRoute = new RetryRouteDefinition("orders.retry", "orders.created.retry", "orders.created.retry"),
                    DeadLetterRoute = new DeadLetterRouteDefinition("orders.dlx", "orders.created.dlq", "orders.created.dlq"),
                },
                Handler = (_, _) =>
                {
                    attempts++;
                    throw new NonRetriableMessageException("do not retry");
                },
            },
            cts.Token));

        await publisher.PublishAsync("orders", "orders.created", new OrderCreated("order-non-retriable"));
        var dlqMessage = await WaitForDlqMessageAsync();
        cts.Cancel();
        await IgnoreCancellationAsync(subscription);

        Assert.NotNull(dlqMessage);
        Assert.Equal(1, attempts);
        Assert.Equal(0u, await CountMessagesAsync("orders.created.retry"));
    }

    [Fact]
    public async Task DiscardMessageException_DiscardsWithoutRetryOrDeadLetter()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IPublisher>();
        var subscriber = provider.GetRequiredService<ISubscriber>();
        await PurgeQueuesAsync("orders.created", "orders.created.retry", "orders.created.dlq");
        var attempts = 0;
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var subscription = Task.Run(() => subscriber.SubscribeAsync(
            new ConsumerDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                ErrorHandling = new ConsumerErrorHandlingSettings
                {
                    Strategy = ConsumerErrorStrategyKind.RetryThenDeadLetter,
                    MaxRetryAttempts = 5,
                    RetryRoute = new RetryRouteDefinition("orders.retry", "orders.created.retry", "orders.created.retry"),
                    DeadLetterRoute = new DeadLetterRouteDefinition("orders.dlx", "orders.created.dlq", "orders.created.dlq"),
                },
                Handler = (_, _) =>
                {
                    attempts++;
                    processed.TrySetResult();
                    throw new DiscardMessageException("discard now");
                },
            },
            cts.Token));

        await publisher.PublishAsync("orders", "orders.created", new OrderCreated("order-discard"));
        await processed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(500);
        cts.Cancel();
        await IgnoreCancellationAsync(subscription);

        Assert.Equal(1, attempts);
        Assert.Equal(0u, await CountMessagesAsync("orders.created"));
        Assert.Equal(0u, await CountMessagesAsync("orders.created.retry"));
        Assert.Equal(0u, await CountMessagesAsync("orders.created.dlq"));
    }

    private ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSphereRabbitMq(options =>
        {
            options.HostName = "localhost";
            options.Port = _fixture.AmqpPort;
            options.UserName = "guest";
            options.Password = "guest";
            options.ValidateTopologyOnStartup = false;
        });

        return services;
    }

    private async Task<OrderCreated?> WaitForDlqMessageAsync()
    {
        var factory = _fixture.CreateConnectionFactory();
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var result = await channel.BasicGetAsync("orders.created.dlq", true);
            if (result is not null)
            {
                var body = System.Text.Json.JsonSerializer.Deserialize<OrderCreated>(result.Body.Span);
                return body;
            }

            await Task.Delay(250);
        }

        return null;
    }

    private async Task<uint> CountMessagesAsync(string queueName)
    {
        var factory = _fixture.CreateConnectionFactory();
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var result = await channel.QueueDeclarePassiveAsync(queueName);
        return result.MessageCount;
    }

    private async Task PurgeQueuesAsync(params string[] queueNames)
    {
        var factory = _fixture.CreateConnectionFactory();
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        foreach (var queueName in queueNames)
        {
            await channel.QueuePurgeAsync(queueName);
        }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void UpdateMax(ref int target, int candidate)
    {
        while (true)
        {
            var current = target;
            if (candidate <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref target, candidate, current) == current)
            {
                return;
            }
        }
    }

    public sealed record OrderCreated(string OrderId);
}
