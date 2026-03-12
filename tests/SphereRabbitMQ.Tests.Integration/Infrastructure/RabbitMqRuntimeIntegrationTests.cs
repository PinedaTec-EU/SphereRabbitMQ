using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.Abstractions.Subscribers;
using SphereRabbitMQ.Abstractions.Publishing;
using SphereRabbitMQ.Abstractions.Serialization;
using SphereRabbitMQ.DependencyInjection;
using SphereRabbitMQ.Domain.Publishing;
using SphereRabbitMQ.Domain.Subscribers;
using SphereRabbitMQ.Domain.Messaging;
using RabbitMQ.Client.Exceptions;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Connection;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Publishing;
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
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        var subscriber = provider.GetRequiredService<IRabbitMQSubscriber>();
        await PurgeQueuesAsync("orders.created", "orders.created.retry.step1", "orders.created.dlq");
        var received = new TaskCompletionSource<MessageEnvelope<OrderCreated>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var subscription = Task.Run(() => subscriber.SubscribeAsync(
            new SubscriberDefinition<OrderCreated>
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
    public async Task PublishAsync_ReusesSinglePublishingChannel_AcrossSequentialPublishes()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        var channelPool = provider.GetRequiredService<RabbitMqChannelPool>();
        await PurgeQueuesAsync("orders.created");

        await publisher.PublishAsync("orders", "orders.created", new OrderCreated("reuse-1"));
        var firstChannel = GetPrivateField<IChannel?>(channelPool, "_channel");

        await publisher.PublishAsync("orders", "orders.created", new OrderCreated("reuse-2"));
        var secondChannel = GetPrivateField<IChannel?>(channelPool, "_channel");

        Assert.NotNull(firstChannel);
        Assert.NotNull(secondChannel);
        Assert.Same(firstChannel, secondChannel);
        Assert.True(secondChannel!.IsOpen);
        Assert.Equal(2u, await CountMessagesAsync("orders.created"));
    }

    [Fact]
    public async Task PublisherAndSubscriber_ShareSameConnectionProviderConnection()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        var subscriber = provider.GetRequiredService<IRabbitMQSubscriber>();
        var connectionProvider = provider.GetRequiredService<RabbitMqConnectionProvider>();
        await PurgeQueuesAsync("orders.created");
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var initialConnection = await connectionProvider.GetConnectionAsync(CancellationToken.None);

        var subscription = Task.Run(() => subscriber.SubscribeAsync(
            new SubscriberDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                PrefetchCount = 1,
                MaxConcurrency = 1,
                Handler = (_, _) =>
                {
                    received.TrySetResult();
                    return Task.CompletedTask;
                },
            },
            cts.Token));

        await publisher.PublishAsync("orders", "orders.created", new OrderCreated("shared-connection"));
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var currentConnection = GetPrivateField<IConnection?>(connectionProvider, "_connection");
        cts.Cancel();
        await IgnoreCancellationAsync(subscription);

        Assert.NotNull(currentConnection);
        Assert.Same(initialConnection, currentConnection);
    }

    [Fact]
    public async Task PublishAsync_HandlesConcurrentPublishRequests_WithoutLosingMessages()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        await PurgeQueuesAsync("orders.created");

        var publishTasks = Enumerable.Range(0, 20)
            .Select(index => publisher.PublishAsync("orders", "orders.created", new OrderCreated($"concurrent-{index}")))
            .ToArray();

        await Task.WhenAll(publishTasks);

        Assert.Equal(20u, await CountMessagesAsync("orders.created"));
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
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        var subscriber = provider.GetRequiredService<IRabbitMQSubscriber>();
        await PurgeQueuesAsync("orders.created", "orders.created.retry.step1", "orders.created.dlq");
        var attempts = 0;
        var completed = new TaskCompletionSource<MessageEnvelope<OrderCreated>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var subscription = Task.Run(() => subscriber.SubscribeAsync(
            new SubscriberDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                PrefetchCount = 1,
                MaxConcurrency = 1,
                ErrorHandling = new SubscriberErrorHandlingSettings
                {
                    Strategy = SubscriberErrorStrategyKind.RetryOnly,
                    MaxRetryAttempts = 1,
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
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        var subscriber = provider.GetRequiredService<IRabbitMQSubscriber>();
        await PurgeQueuesAsync("orders.created", "orders.created.retry.step1", "orders.created.dlq");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var subscription = Task.Run(() => subscriber.SubscribeAsync(
            new SubscriberDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                PrefetchCount = 1,
                MaxConcurrency = 1,
                ErrorHandling = new SubscriberErrorHandlingSettings
                {
                    Strategy = SubscriberErrorStrategyKind.DeadLetterOnly,
                },
                Handler = (_, _) => throw new InvalidOperationException("force dlq"),
            },
            cts.Token));

        await publisher.PublishAsync("orders", "orders.created", new OrderCreated("order-3"));
        var dlqMessage = await WaitForRawDlqMessageAsync();
        cts.Cancel();
        await IgnoreCancellationAsync(subscription);

        Assert.NotEmpty(dlqMessage.ToArray());
    }

    [Fact]
    public async Task SubscriberConcurrency_IsBoundedByMaxConcurrency()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        var subscriber = provider.GetRequiredService<IRabbitMQSubscriber>();
        await PurgeQueuesAsync("orders.created", "orders.created.retry.step1", "orders.created.dlq");
        var started = 0;
        var maxObserved = 0;
        var completed = 0;
        var allProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var subscription = Task.Run(() => subscriber.SubscribeAsync(
            new SubscriberDefinition<OrderCreated>
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
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();

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
        var subscriber = provider.GetRequiredService<IRabbitMQSubscriber>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => subscriber.SubscribeAsync(
            new SubscriberDefinition<OrderCreated>
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
        var subscriber = provider.GetRequiredService<IRabbitMQSubscriber>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => subscriber.SubscribeAsync(
            new SubscriberDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                ErrorHandling = new SubscriberErrorHandlingSettings
                {
                    Strategy = SubscriberErrorStrategyKind.RetryOnly,
                    RetryRoute = new RetryRouteDefinition("orders.created.retry.missing", "orders.created.retry.step1", "orders.created.retry.step1"),
                },
                Handler = (_, _) => Task.CompletedTask,
            }));

        Assert.Contains("orders.created.retry.missing", exception.Message, StringComparison.Ordinal);
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
        var subscriber = provider.GetRequiredService<IRabbitMQSubscriber>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => subscriber.SubscribeAsync(
            new SubscriberDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                ErrorHandling = new SubscriberErrorHandlingSettings
                {
                    Strategy = SubscriberErrorStrategyKind.RetryOnly,
                    RetryRoute = new RetryRouteDefinition("orders.created.retry", "orders.created.retry.step1", "orders.created.retry.step1.missing"),
                },
                Handler = (_, _) => Task.CompletedTask,
            }));

        Assert.Contains("orders.created.retry.step1.missing", exception.Message, StringComparison.Ordinal);
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
        var subscriber = provider.GetRequiredService<IRabbitMQSubscriber>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => subscriber.SubscribeAsync(
            new SubscriberDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                ErrorHandling = new SubscriberErrorHandlingSettings
                {
                    Strategy = SubscriberErrorStrategyKind.DeadLetterOnly,
                    DeadLetterRoute = new DeadLetterRouteDefinition("orders.created.dlx.missing", "orders.created.dlq", "orders.created.dlq"),
                },
                Handler = (_, _) => Task.CompletedTask,
            }));

        Assert.Contains("orders.created.dlx.missing", exception.Message, StringComparison.Ordinal);
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
        var subscriber = provider.GetRequiredService<IRabbitMQSubscriber>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => subscriber.SubscribeAsync(
            new SubscriberDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                ErrorHandling = new SubscriberErrorHandlingSettings
                {
                    Strategy = SubscriberErrorStrategyKind.DeadLetterOnly,
                    DeadLetterRoute = new DeadLetterRouteDefinition("orders.created.dlx", "orders.created.dlq", "orders.created.dlq.missing"),
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
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        var subscriber = provider.GetRequiredService<IRabbitMQSubscriber>();
        await PurgeQueuesAsync("orders.created", "orders.created.retry.step1", "orders.created.dlq");
        var attempts = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var subscription = Task.Run(() => subscriber.SubscribeAsync(
            new SubscriberDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                ErrorHandling = new SubscriberErrorHandlingSettings
                {
                    Strategy = SubscriberErrorStrategyKind.RetryThenDeadLetter,
                    MaxRetryAttempts = 5,
                },
                Handler = (_, _) =>
                {
                    attempts++;
                    throw new NonRetriableMessageException("do not retry");
                },
            },
            cts.Token));

        await publisher.PublishAsync("orders", "orders.created", new OrderCreated("order-non-retriable"));
        var dlqMessage = await WaitForRawDlqMessageAsync();
        cts.Cancel();
        await IgnoreCancellationAsync(subscription);

        Assert.NotEmpty(dlqMessage.ToArray());
        Assert.Equal(1, attempts);
        Assert.Equal(0u, await CountMessagesAsync("orders.created.retry.step1"));
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
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        var subscriber = provider.GetRequiredService<IRabbitMQSubscriber>();
        await PurgeQueuesAsync("orders.created", "orders.created.retry.step1", "orders.created.dlq");
        var attempts = 0;
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var subscription = Task.Run(() => subscriber.SubscribeAsync(
            new SubscriberDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                ErrorHandling = new SubscriberErrorHandlingSettings
                {
                    Strategy = SubscriberErrorStrategyKind.RetryThenDeadLetter,
                    MaxRetryAttempts = 5,
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
        Assert.Equal(0u, await CountMessagesAsync("orders.created.retry.step1"));
        Assert.Equal(0u, await CountMessagesAsync("orders.created.dlq"));
    }

    [Fact]
    public async Task RetryThenDeadLetterAsync_RetriesExpectedTimes_ThenRoutesToDeadLetter_AndNotifies()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        var subscriber = provider.GetRequiredService<IRabbitMQSubscriber>();
        await PurgeQueuesAsync("orders.created", "orders.created.retry.step1", "orders.created.dlq");
        var attempts = 0;
        SubscriberDeadLetterNotification<OrderCreated>? notification = null;
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var subscription = Task.Run(() => subscriber.SubscribeAsync(
            new SubscriberDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                PrefetchCount = 1,
                MaxConcurrency = 1,
                ErrorHandling = new SubscriberErrorHandlingSettings
                {
                    Strategy = SubscriberErrorStrategyKind.RetryThenDeadLetter,
                    MaxRetryAttempts = 2,
                },
                Handler = (_, _) =>
                {
                    attempts++;
                    throw new InvalidOperationException("always fail");
                },
                DeadLetterNotificationHandler = (deadLetterNotification, _) =>
                {
                    notification = deadLetterNotification;
                    notified.TrySetResult();
                    return Task.CompletedTask;
                },
            },
            cts.Token));

        await publisher.PublishAsync("orders", "orders.created", new OrderCreated("order-retry-dlq"));
        await notified.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var dlqMessage = await WaitForRawDlqMessageAsync();
        cts.Cancel();
        await IgnoreCancellationAsync(subscription);

        Assert.Equal(3, attempts);
        Assert.NotNull(notification);
        Assert.Equal("order-retry-dlq", notification!.Message.Body.OrderId);
        Assert.Equal(SubscriberFailureDisposition.DeadLetter, notification.Decision.Disposition);
        Assert.Equal(3, notification.Decision.RetryCount);
        Assert.Contains("order-retry-dlq", System.Text.Encoding.UTF8.GetString(dlqMessage.ToArray()), StringComparison.Ordinal);
        Assert.Equal(0u, await CountMessagesAsync("orders.created.retry.step1"));
    }

    [Fact]
    public async Task DeadLetterOnlyAsync_Notifies_WhenMessageIsRoutedToDeadLetterWithoutRetry()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        var subscriber = provider.GetRequiredService<IRabbitMQSubscriber>();
        await PurgeQueuesAsync("orders.created", "orders.created.retry.step1", "orders.created.dlq");
        var attempts = 0;
        SubscriberDeadLetterNotification<OrderCreated>? notification = null;
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var subscription = Task.Run(() => subscriber.SubscribeAsync(
            new SubscriberDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                PrefetchCount = 1,
                MaxConcurrency = 1,
                ErrorHandling = new SubscriberErrorHandlingSettings
                {
                    Strategy = SubscriberErrorStrategyKind.DeadLetterOnly,
                },
                Handler = (_, _) =>
                {
                    attempts++;
                    throw new InvalidOperationException("send to dlq");
                },
                DeadLetterNotificationHandler = (deadLetterNotification, _) =>
                {
                    notification = deadLetterNotification;
                    notified.TrySetResult();
                    return Task.CompletedTask;
                },
            },
            cts.Token));

        await publisher.PublishAsync("orders", "orders.created", new OrderCreated("order-direct-dlq"));
        await notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var dlqMessage = await WaitForRawDlqMessageAsync();
        cts.Cancel();
        await IgnoreCancellationAsync(subscription);

        Assert.Equal(1, attempts);
        Assert.NotNull(notification);
        Assert.Equal("order-direct-dlq", notification!.Message.Body.OrderId);
        Assert.Equal(SubscriberFailureDisposition.DeadLetter, notification.Decision.Disposition);
        Assert.Equal(1, notification.Decision.RetryCount);
        Assert.Contains("order-direct-dlq", System.Text.Encoding.UTF8.GetString(dlqMessage.ToArray()), StringComparison.Ordinal);
        Assert.Equal(0u, await CountMessagesAsync("orders.created.retry.step1"));
    }

    [Fact]
    public async Task RetryOnlyAsync_DoesNotNotify_WhenRetriesAreExhaustedWithoutDeadLetter()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        var subscriber = provider.GetRequiredService<IRabbitMQSubscriber>();
        await PurgeQueuesAsync("orders.created", "orders.created.retry.step1", "orders.created.dlq");
        var attempts = 0;
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notificationCount = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var subscription = Task.Run(() => subscriber.SubscribeAsync(
            new SubscriberDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                PrefetchCount = 1,
                MaxConcurrency = 1,
                ErrorHandling = new SubscriberErrorHandlingSettings
                {
                    Strategy = SubscriberErrorStrategyKind.RetryOnly,
                    MaxRetryAttempts = 1,
                },
                Handler = (_, _) =>
                {
                    attempts++;
                    if (attempts == 2)
                    {
                        processed.TrySetResult();
                    }

                    throw new InvalidOperationException("retry then discard");
                },
                DeadLetterNotificationHandler = (_, _) =>
                {
                    notificationCount++;
                    return Task.CompletedTask;
                },
            },
            cts.Token));

        await publisher.PublishAsync("orders", "orders.created", new OrderCreated("order-retry-only"));
        await processed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(750);
        cts.Cancel();
        await IgnoreCancellationAsync(subscription);

        Assert.Equal(2, attempts);
        Assert.Equal(0, notificationCount);
        Assert.Equal(0u, await CountMessagesAsync("orders.created.retry.step1"));
        Assert.Equal(0u, await CountMessagesAsync("orders.created.dlq"));
    }

    [Fact]
    public async Task DiscardStrategy_DoesNotNotify_WhenRetryAndDeadLetterAreDisabled()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        var subscriber = provider.GetRequiredService<IRabbitMQSubscriber>();
        await PurgeQueuesAsync("orders.created", "orders.created.retry.step1", "orders.created.dlq");
        var attempts = 0;
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notificationCount = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var subscription = Task.Run(() => subscriber.SubscribeAsync(
            new SubscriberDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                PrefetchCount = 1,
                MaxConcurrency = 1,
                ErrorHandling = new SubscriberErrorHandlingSettings
                {
                    Strategy = SubscriberErrorStrategyKind.Discard,
                },
                Handler = (_, _) =>
                {
                    attempts++;
                    processed.TrySetResult();
                    throw new InvalidOperationException("discard directly");
                },
                DeadLetterNotificationHandler = (_, _) =>
                {
                    notificationCount++;
                    return Task.CompletedTask;
                },
            },
            cts.Token));

        await publisher.PublishAsync("orders", "orders.created", new OrderCreated("order-discard-no-dlq"));
        await processed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(750);
        cts.Cancel();
        await IgnoreCancellationAsync(subscription);

        Assert.Equal(1, attempts);
        Assert.Equal(0, notificationCount);
        Assert.Equal(0u, await CountMessagesAsync("orders.created"));
        Assert.Equal(0u, await CountMessagesAsync("orders.created.retry.step1"));
        Assert.Equal(0u, await CountMessagesAsync("orders.created.dlq"));
    }

    [Fact]
    public async Task DeserializeFailure_DeadLettersMalformedPayload_WhenComponentFailureHandlerChoosesDeadLetter()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var subscriber = provider.GetRequiredService<IRabbitMQSubscriber>();
        await PurgeQueuesAsync("orders.created", "orders.created.retry.step1", "orders.created.dlq");
        var notified = new TaskCompletionSource<SubscriberComponentFailureStage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var subscription = Task.Run(() => subscriber.SubscribeAsync(
            new SubscriberDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                PrefetchCount = 1,
                MaxConcurrency = 1,
                ErrorHandling = new SubscriberErrorHandlingSettings
                {
                    Strategy = SubscriberErrorStrategyKind.RetryThenDeadLetter,
                    MaxRetryAttempts = 3,
                },
                Handler = (_, _) => Task.CompletedTask,
                ComponentFailureHandler = (context, _) =>
                {
                    notified.TrySetResult(context.Stage);
                    return Task.FromResult(new SubscriberComponentFailureHandlingResult(SubscriberComponentFailureAction.DeadLetter));
                },
            },
            cts.Token));

        await PublishRawMessageAsync("orders", "orders.created", "not-json"u8.ToArray());
        var stage = await notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var dlqMessage = await WaitForRawDlqMessageAsync();
        cts.Cancel();
        await IgnoreCancellationAsync(subscription);

        Assert.Equal(SubscriberComponentFailureStage.Deserialize, stage);
        Assert.Equal("not-json", System.Text.Encoding.UTF8.GetString(dlqMessage.ToArray()));
        Assert.Equal(0u, await CountMessagesAsync("orders.created.retry.step1"));
    }

    [Fact]
    public async Task DeserializeFailure_DiscardsMalformedPayload_WhenComponentFailureHandlerChoosesDiscard()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var subscriber = provider.GetRequiredService<IRabbitMQSubscriber>();
        await PurgeQueuesAsync("orders.created", "orders.created.retry.step1", "orders.created.dlq");
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var subscription = Task.Run(() => subscriber.SubscribeAsync(
            new SubscriberDefinition<OrderCreated>
            {
                QueueName = "orders.created",
                PrefetchCount = 1,
                MaxConcurrency = 1,
                ErrorHandling = new SubscriberErrorHandlingSettings
                {
                    Strategy = SubscriberErrorStrategyKind.RetryThenDeadLetter,
                    MaxRetryAttempts = 3,
                },
                Handler = (_, _) => Task.CompletedTask,
                ComponentFailureHandler = (_, _) =>
                {
                    notified.TrySetResult();
                    return Task.FromResult(new SubscriberComponentFailureHandlingResult(SubscriberComponentFailureAction.Discard));
                },
            },
            cts.Token));

        await PublishRawMessageAsync("orders", "orders.created", "not-json"u8.ToArray());
        await notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(750);
        cts.Cancel();
        await IgnoreCancellationAsync(subscription);

        Assert.Equal(0u, await CountMessagesAsync("orders.created"));
        Assert.Equal(0u, await CountMessagesAsync("orders.created.retry.step1"));
        Assert.Equal(0u, await CountMessagesAsync("orders.created.dlq"));
    }

    [Fact]
    public async Task PublishAsync_NotifiesPublisherFailureHandler_WhenSerializerThrows()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var failureHandler = new RecordingPublisherFailureHandler();
        var services = CreateServices();
        services.AddSingleton<IMessageSerializer, ThrowingSerializeMessageSerializer>();
        services.AddSingleton<IRabbitMQPublisherFailureHandler>(failureHandler);
        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            publisher.PublishAsync("orders", "orders.created", new OrderCreated("serialize-failure")));

        Assert.Equal("serialize failure", exception.Message);
        Assert.Single(failureHandler.Notifications);
        Assert.Equal(PublisherFailureStage.Serialize, failureHandler.Notifications[0].Stage);
        Assert.Equal("orders", failureHandler.Notifications[0].Exchange);
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

    private async Task<ReadOnlyMemory<byte>> WaitForRawDlqMessageAsync()
    {
        var factory = _fixture.CreateConnectionFactory();
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var result = await channel.BasicGetAsync("orders.created.dlq", true);
            if (result is not null)
            {
                return result.Body;
            }

            await Task.Delay(250);
        }

        throw new InvalidOperationException("Expected raw dead-letter message was not found.");
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

    private async Task PublishRawMessageAsync(string exchange, string routingKey, ReadOnlyMemory<byte> body)
    {
        var factory = _fixture.CreateConnectionFactory();
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var properties = new RabbitMQ.Client.BasicProperties
        {
            ContentType = "application/json",
            Persistent = true,
        };

        await channel.BasicPublishAsync(exchange, routingKey, true, properties, body, CancellationToken.None);
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

    private static T? GetPrivateField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T?)field!.GetValue(target);
    }

    private sealed class ThrowingSerializeMessageSerializer : IMessageSerializer
    {
        public string ContentType => "application/json";

        public TMessage Deserialize<TMessage>(ReadOnlyMemory<byte> body)
            => throw new NotSupportedException("Deserialize is not used in this test.");

        public ReadOnlyMemory<byte> Serialize<TMessage>(TMessage message)
            => throw new InvalidOperationException("serialize failure");
    }

    private sealed class RecordingPublisherFailureHandler : IRabbitMQPublisherFailureHandler
    {
        public List<PublisherFailureContext> Notifications { get; } = new();

        public Task OnPublishFailureAsync(PublisherFailureContext context, CancellationToken cancellationToken)
        {
            Notifications.Add(context);
            return Task.CompletedTask;
        }
    }
}
