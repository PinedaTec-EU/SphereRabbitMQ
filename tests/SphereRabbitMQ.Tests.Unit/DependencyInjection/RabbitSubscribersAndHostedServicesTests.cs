using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.Abstractions.Subscribers;
using SphereRabbitMQ.Abstractions.Topology;
using SphereRabbitMQ.Application.Retry;
using SphereRabbitMQ.DependencyInjection;
using SphereRabbitMQ.DependencyInjection.Subscribers;
using SphereRabbitMQ.Domain.Subscribers;
using SphereRabbitMQ.Domain.Messaging;
using SphereRabbitMQ.Domain.Retry;

namespace SphereRabbitMQ.Tests.Unit.DependencyInjection;

public sealed class RabbitSubscriberErrorHandlingBuilderTests
{
    private const int ConfiguredMaxRetryAttempts = 7;

    [Fact]
    public void Build_ReturnsRetryThenDeadLetter_WhenRetryAndDeadLetterAreEnabled()
    {
        var builder = new RabbitSubscriberErrorHandlingBuilder()
            .UseRetryAndDeadLetter(maxRetryAttempts: ConfiguredMaxRetryAttempts);

        builder.NonRetriableExceptions.Add(typeof(InvalidOperationException));

        var settings = builder.Build();

        Assert.Equal(SubscriberErrorStrategyKind.RetryThenDeadLetter, settings.Strategy);
        Assert.Equal(ConfiguredMaxRetryAttempts, settings.MaxRetryAttempts);
        Assert.Null(settings.RetryRoute);
        Assert.Null(settings.DeadLetterRoute);
        Assert.Contains(typeof(InvalidOperationException), settings.NonRetriableExceptions);
    }

    [Theory]
    [InlineData(true, SubscriberErrorStrategyKind.DeadLetterOnly)]
    [InlineData(false, SubscriberErrorStrategyKind.Discard)]
    public void Build_ResolvesExpectedStrategy(bool enableDeadLetter, SubscriberErrorStrategyKind expected)
    {
        var builder = new RabbitSubscriberErrorHandlingBuilder();

        if (enableDeadLetter)
        {
            builder.UseDeadLetterOnly();
        }
        else
        {
            builder.UseDiscard();
        }

        var settings = builder.Build();

        Assert.Equal(expected, settings.Strategy);
        Assert.Null(settings.RetryRoute);
        Assert.Null(settings.DeadLetterRoute);
    }
}

public sealed class RabbitSubscriberRegistrationTests
{
    [Fact]
    public async Task SubscribeAsync_ConfiguresBuilderAndSubscribesToQueue()
    {
        var subscriberMock = new Mock<IRabbitMQSubscriber>();
        SubscriberDefinition<string>? capturedDefinition = null;

        subscriberMock
            .Setup(s => s.SubscribeAsync(It.IsAny<SubscriberDefinition<string>>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriberDefinition<string>, CancellationToken>((definition, _) => capturedDefinition = definition)
            .Returns(Task.CompletedTask);

        var registration = new RabbitSubscriberRegistration<string>(builder =>
        {
            builder
                .FromQueue("orders.queue")
                .WithPrefetchCount(5)
                .WithMaxConcurrency(2)
                .Handle((_, _) => Task.CompletedTask);
        });

        await registration.SubscribeAsync(new ServiceCollection().BuildServiceProvider(), subscriberMock.Object, CancellationToken.None);

        Assert.Equal("orders.queue", registration.QueueName);
        Assert.NotNull(capturedDefinition);
        Assert.Equal("orders.queue", capturedDefinition!.QueueName);
        Assert.Equal((ushort)5, capturedDefinition.PrefetchCount);
        Assert.Equal(2, capturedDefinition.MaxConcurrency);

        subscriberMock.Verify(s => s.SubscribeAsync(It.IsAny<SubscriberDefinition<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddRabbitSubscriberOverload_RegistersTypedHandlerWithQueue()
    {
        var services = new ServiceCollection();
        services.AddRabbitSubscriber<string, SimpleStringHandler>("orders.queue");
        await using var provider = services.BuildServiceProvider();

        var registrations = provider.GetServices<IRabbitSubscriberRegistration>().ToArray();

        Assert.Single(registrations);
        var definition = registrations[0].BuildDefinition(provider);
        Assert.Equal("orders.queue", definition.QueueName);
        Assert.Equal(SubscriberErrorStrategyKind.DeadLetterOnly, definition.ErrorHandling.Strategy);
        Assert.NotNull(((SubscriberDefinition<string>)definition).RetryDelayResolver);
    }

    [Fact]
    public async Task AddRabbitSubscriberOverload_RegistersInlineHandlerWithQueue()
    {
        var services = new ServiceCollection();
        services.AddRabbitSubscriber<string>(
            "orders.queue",
            (_, _) => Task.CompletedTask,
            builder => builder.WithMaxConcurrency(2));
        await using var provider = services.BuildServiceProvider();

        var registrations = provider.GetServices<IRabbitSubscriberRegistration>().ToArray();

        Assert.Single(registrations);
        var definition = registrations[0].BuildDefinition(provider);
        Assert.Equal("orders.queue", definition.QueueName);
        Assert.Equal(2, definition.MaxConcurrency);
        Assert.NotNull(((SubscriberDefinition<string>)definition).RetryDelayResolver);
    }

    [Fact]
    public async Task AddKeyedRabbitSubscriber_RegistersSubscriberWithRequestedQueue()
    {
        var services = new ServiceCollection();
        services.AddKeyedRabbitSubscriber<string, SimpleStringHandler>("inventory", "orders.queue");
        await using var provider = services.BuildServiceProvider();

        var registrations = provider.GetServices<IRabbitSubscriberRegistration>().ToArray();

        Assert.Single(registrations);
        var definition = registrations[0].BuildDefinition(provider);
        Assert.Equal("orders.queue", definition.QueueName);
    }

    private sealed class SimpleStringHandler : IRabbitSubscriberMessageHandler<string>
    {
        public Task HandleAsync(MessageEnvelope<string> message, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}

[Collection(SharedStateTestCollection.Name)]
public sealed class RabbitSubscriberRegistrationBuilderTests
{
    [Fact]
    public void Build_Throws_WhenQueueIsMissing()
    {
        var builder = new RabbitSubscriberRegistrationBuilder<string>();
        builder.Handle((_, _) => Task.CompletedTask);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build(new ServiceCollection().BuildServiceProvider()));

        Assert.Equal("Rabbit subscriber queue name is required.", ex.Message);
    }

    [Fact]
    public void Build_Throws_WhenHandlerIsMissing()
    {
        var builder = new RabbitSubscriberRegistrationBuilder<string>
        {
            Queue = "orders.queue",
        };

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build(new ServiceCollection().BuildServiceProvider()));

        Assert.Equal("Rabbit subscriber 'String' requires a handler.", ex.Message);
    }

    [Fact]
    public async Task Build_UsesDirectHandler_WhenConfiguredWithHandle()
    {
        var builder = new RabbitSubscriberRegistrationBuilder<string>
        {
            Queue = "orders.queue",
            Prefetch = 8,
            MaxConcurrency = 3,
        };

        builder.ErrorHandling.UseRetryAndDeadLetter();

        var invoked = false;
        builder.Handle((_, _) =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        var definition = builder.Build(new ServiceCollection().BuildServiceProvider());
        var envelope = new MessageEnvelope<string>("body", new Dictionary<string, object?>(), "orders.route", "orders.exchange", "id", "corr", DateTimeOffset.UtcNow);

        await definition.Handler(envelope, CancellationToken.None);

        Assert.True(invoked);
        Assert.Equal("orders.queue", definition.QueueName);
        Assert.Equal((ushort)8, definition.PrefetchCount);
        Assert.Equal(3, definition.MaxConcurrency);
        Assert.Equal(SubscriberErrorStrategyKind.RetryThenDeadLetter, definition.ErrorHandling.Strategy);
        Assert.Null(definition.ErrorHandling.RetryRoute);
        Assert.Null(definition.ErrorHandling.DeadLetterRoute);
        Assert.Equal(
            TimeSpan.FromMilliseconds(250),
            definition.RetryDelayResolver!(
                new SubscriberRetryDelayContext<string>(
                    definition.QueueName,
                    envelope,
                    new InvalidOperationException("boom"),
                    1)));
    }

    [Fact]
    public void Build_UsesRetryDelayResolver_FromDependencyInjection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISubscriberRetryDelayResolver<string>, SharedCustomStringRetryDelayResolver>();
        using var provider = services.BuildServiceProvider();

        var builder = new RabbitSubscriberRegistrationBuilder<string>
        {
            Queue = "orders.queue",
        };

        builder.Handle((_, _) => Task.CompletedTask);

        var definition = builder.Build(provider);
        var delay = definition.RetryDelayResolver!(
            new SubscriberRetryDelayContext<string>(
                definition.QueueName,
                new MessageEnvelope<string>("body", new Dictionary<string, object?>(), "orders.created", "orders", "message-1", "corr-1", DateTimeOffset.UtcNow),
                new InvalidOperationException("boom"),
                2));

        Assert.Equal(TimeSpan.FromSeconds(9), delay);
    }

    [Fact]
    public async Task Build_UsesScopedHandler_WhenConfiguredWithUseHandler()
    {
        var services = new ServiceCollection();
        services.AddScoped<RecordingStringHandler>();
        var provider = services.BuildServiceProvider();

        var builder = new RabbitSubscriberRegistrationBuilder<string>
        {
            Queue = "orders.queue",
        };

        builder.UseHandler<RecordingStringHandler>();

        var definition = builder.Build(provider);
        var envelope = new MessageEnvelope<string>("hello", new Dictionary<string, object?>(), "route", "exchange", null, null, null);

        await definition.Handler(envelope, CancellationToken.None);

        Assert.Equal(1, RecordingStringHandler.CallCount);
        Assert.Equal("hello", RecordingStringHandler.LastPayload);
        Assert.NotNull(definition.DeadLetterNotificationHandler);

        RecordingStringHandler.Reset();
    }

    [Fact]
    public async Task Build_UsesScopedDeadLetterNotification_WhenHandlerImplementsOptionalInterface()
    {
        var services = new ServiceCollection();
        services.AddScoped<RecordingStringHandler>();
        var provider = services.BuildServiceProvider();

        var builder = new RabbitSubscriberRegistrationBuilder<string>
        {
            Queue = "orders.queue",
        };

        builder.UseHandler<RecordingStringHandler>();

        var definition = builder.Build(provider);
        var notification = new SubscriberDeadLetterNotification<string>(
            new MessageEnvelope<string>("hello", new Dictionary<string, object?>(), "route", "exchange", "message-1", "corr-1", DateTimeOffset.UtcNow),
            new InvalidOperationException("boom"),
            new SubscriberFailureDecision(
                SubscriberFailureDisposition.DeadLetter,
                3,
                new RetryRouteDefinition("orders.retry", "orders.created.retry", "orders.created.retry"),
                new DeadLetterRouteDefinition("orders.dlx", "orders.created.dlq", "orders.created.dlq")));

        Assert.NotNull(definition.DeadLetterNotificationHandler);

        await definition.DeadLetterNotificationHandler!(notification, CancellationToken.None);

        Assert.Equal(1, RecordingStringHandler.DeadLetterNotificationCount);
        Assert.Equal("hello", RecordingStringHandler.LastDeadLetterPayload);
        Assert.Equal(SubscriberFailureDisposition.DeadLetter, RecordingStringHandler.LastDeadLetterDisposition);
        Assert.Equal(3, RecordingStringHandler.LastDeadLetterRetryCount);

        RecordingStringHandler.Reset();
    }

    [Fact]
    public async Task Build_UsesScopedComponentFailureHandler_WhenHandlerImplementsOptionalInterface()
    {
        var services = new ServiceCollection();
        services.AddScoped<RecordingStringHandler>();
        var provider = services.BuildServiceProvider();

        var builder = new RabbitSubscriberRegistrationBuilder<string>
        {
            Queue = "orders.queue",
        };

        builder.UseHandler<RecordingStringHandler>();

        var definition = builder.Build(provider);

        Assert.NotNull(definition.ComponentFailureHandler);

        var result = await definition.ComponentFailureHandler!(
            new SubscriberComponentFailureContext(
                "orders.queue",
                new MessageEnvelope<ReadOnlyMemory<byte>>(System.Text.Encoding.UTF8.GetBytes("{}"), new Dictionary<string, object?>(), "route", "exchange", "message-1", "corr-1", DateTimeOffset.UtcNow),
                new SubscriberErrorHandlingSettings(),
                RetryMetadata.None,
                SubscriberComponentFailureStage.Deserialize,
                new InvalidOperationException("broken payload")),
            CancellationToken.None);

        Assert.Equal(SubscriberComponentFailureAction.DeadLetter, result.Action);
        Assert.Equal(1, RecordingStringHandler.ComponentFailureCount);
        Assert.Equal(SubscriberComponentFailureStage.Deserialize, RecordingStringHandler.LastComponentFailureStage);

        RecordingStringHandler.Reset();
    }

    [Fact]
    public async Task Build_UsesExplicitDeadLetterNotification_WhenConfigured()
    {
        var builder = new RabbitSubscriberRegistrationBuilder<string>
        {
            Queue = "orders.queue",
        };

        var notificationCount = 0;
        builder.Handle((_, _) => Task.CompletedTask);
        builder.OnDeadLetter((notification, _) =>
        {
            notificationCount++;
            Assert.Equal("hello", notification.Message.Body);
            Assert.Equal(SubscriberFailureDisposition.DeadLetter, notification.Decision.Disposition);
            return Task.CompletedTask;
        });

        var definition = builder.Build(new ServiceCollection().BuildServiceProvider());
        var notification = new SubscriberDeadLetterNotification<string>(
            new MessageEnvelope<string>("hello", new Dictionary<string, object?>(), "route", "exchange", null, null, null),
            new InvalidOperationException("boom"),
            new SubscriberFailureDecision(
                SubscriberFailureDisposition.DeadLetter,
                1,
                null,
                new DeadLetterRouteDefinition("orders.dlx", "orders.created.dlq", "orders.created.dlq")));

        Assert.NotNull(definition.DeadLetterNotificationHandler);

        await definition.DeadLetterNotificationHandler!(notification, CancellationToken.None);

        Assert.Equal(1, notificationCount);
    }

    private sealed class RecordingStringHandler : IRabbitSubscriberMessageHandler<string>
        , IRabbitSubscriberDeadLetterNotificationHandler<string>
        , IRabbitSubscriberComponentFailureHandler<string>
    {
        public static int CallCount { get; private set; }

        public static string? LastPayload { get; private set; }

        public static int DeadLetterNotificationCount { get; private set; }

        public static string? LastDeadLetterPayload { get; private set; }

        public static SubscriberFailureDisposition? LastDeadLetterDisposition { get; private set; }

        public static int? LastDeadLetterRetryCount { get; private set; }

        public static int ComponentFailureCount { get; private set; }

        public static SubscriberComponentFailureStage? LastComponentFailureStage { get; private set; }

        public Task HandleAsync(MessageEnvelope<string> message, CancellationToken cancellationToken)
        {
            CallCount++;
            LastPayload = message.Body;
            return Task.CompletedTask;
        }

        public Task OnDeadLetterAsync(SubscriberDeadLetterNotification<string> notification, CancellationToken cancellationToken)
        {
            DeadLetterNotificationCount++;
            LastDeadLetterPayload = notification.Message.Body;
            LastDeadLetterDisposition = notification.Decision.Disposition;
            LastDeadLetterRetryCount = notification.Decision.RetryCount;
            return Task.CompletedTask;
        }

        public Task<SubscriberComponentFailureHandlingResult> OnComponentFailureAsync(
            SubscriberComponentFailureContext context,
            CancellationToken cancellationToken)
        {
            ComponentFailureCount++;
            LastComponentFailureStage = context.Stage;
            return Task.FromResult(new SubscriberComponentFailureHandlingResult(SubscriberComponentFailureAction.DeadLetter));
        }

        public static void Reset()
        {
            CallCount = 0;
            LastPayload = null;
            DeadLetterNotificationCount = 0;
            LastDeadLetterPayload = null;
            LastDeadLetterDisposition = null;
            LastDeadLetterRetryCount = null;
            ComponentFailureCount = 0;
            LastComponentFailureStage = null;
        }
    }
}

public sealed class SubscriberRetryDelayResolverServiceCollectionTests
{
    [Fact]
    public void AddSphereRabbitMq_RegistersDefaultSubscriberRetryDelayResolver()
    {
        var services = new ServiceCollection();
        services.AddSphereRabbitMq();

        using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<ISubscriberRetryDelayResolver<string>>();

        Assert.IsType<DefaultSubscriberRetryDelayResolver<string>>(resolver);
    }

    [Fact]
    public void AddSphereRabbitMq_AllowsHostToOverrideSubscriberRetryDelayResolver()
    {
        var services = new ServiceCollection();
        services.AddSphereRabbitMq();
        services.AddSingleton<ISubscriberRetryDelayResolver<string>, SharedCustomStringRetryDelayResolver>();

        using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<ISubscriberRetryDelayResolver<string>>();

        Assert.IsType<SharedCustomStringRetryDelayResolver>(resolver);
    }
}

file sealed class SharedCustomStringRetryDelayResolver : ISubscriberRetryDelayResolver<string>
{
    public TimeSpan Resolve(SubscriberRetryDelayContext<string> context) => TimeSpan.FromSeconds(9);
}

public sealed class RabbitMqSubscribersHostedServiceTests
{
    [Fact]
    public async Task ExecuteAsync_SubscribesAllRegistrations()
    {
        var subscriberMock = new Mock<IRabbitMQSubscriber>();
        var logger = NullLogger<RabbitMqSubscribersHostedService>.Instance;

        subscriberMock
            .Setup(s => s.SubscribeAsync(It.IsAny<SubscriberDefinition<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        subscriberMock
            .Setup(s => s.SubscribeAsync(It.IsAny<SubscriberDefinition<int>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var registrations = new IRabbitSubscriberRegistration[]
        {
            new RabbitSubscriberRegistration<string>(builder =>
            {
                builder.Queue = "orders.queue";
                builder.Handle((_, _) => Task.CompletedTask);
            }),
            new RabbitSubscriberRegistration<int>(builder =>
            {
                builder.Queue = "numbers.queue";
                builder.Handle((_, _) => Task.CompletedTask);
            }),
        };

        var hostedService = new RabbitMqSubscribersHostedService(
            registrations,
            new ServiceCollection().BuildServiceProvider(),
            subscriberMock.Object,
            logger);

        var executeMethod = typeof(RabbitMqSubscribersHostedService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(executeMethod);

        var task = (Task)executeMethod!.Invoke(hostedService, [CancellationToken.None])!;
        await task;

        subscriberMock.Verify(s => s.SubscribeAsync(It.IsAny<SubscriberDefinition<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        subscriberMock.Verify(s => s.SubscribeAsync(It.IsAny<SubscriberDefinition<int>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

public sealed class RabbitMqTopologyValidationHostedServiceTests
{
    [Fact]
    public async Task StartAsync_DoesNothing_WhenValidationOnStartupIsDisabled()
    {
        var topologyValidatorMock = new Mock<IRabbitMqTopologyValidator>();
        var options = Options.Create(new SphereRabbitMqOptions { ValidateTopologyOnStartup = false });
        var logger = NullLogger<RabbitMqTopologyValidationHostedService>.Instance;

        var hostedService = new RabbitMqTopologyValidationHostedService(topologyValidatorMock.Object, options, logger);

        await hostedService.StartAsync(CancellationToken.None);

        topologyValidatorMock.Verify(v => v.ValidateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_ValidatesTopology_WhenValidationOnStartupIsEnabled()
    {
        var topologyValidatorMock = new Mock<IRabbitMqTopologyValidator>();
        var options = Options.Create(new SphereRabbitMqOptions { ValidateTopologyOnStartup = true });
        var logger = NullLogger<RabbitMqTopologyValidationHostedService>.Instance;

        var hostedService = new RabbitMqTopologyValidationHostedService(topologyValidatorMock.Object, options, logger);

        await hostedService.StartAsync(CancellationToken.None);

        topologyValidatorMock.Verify(v => v.ValidateAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_CompletesImmediately()
    {
        var topologyValidatorMock = new Mock<IRabbitMqTopologyValidator>();
        var options = Options.Create(new SphereRabbitMqOptions { ValidateTopologyOnStartup = true });
        var logger = NullLogger<RabbitMqTopologyValidationHostedService>.Instance;

        var hostedService = new RabbitMqTopologyValidationHostedService(topologyValidatorMock.Object, options, logger);

        var task = hostedService.StopAsync(CancellationToken.None);
        await task;

        Assert.True(task.IsCompletedSuccessfully);
    }
}
