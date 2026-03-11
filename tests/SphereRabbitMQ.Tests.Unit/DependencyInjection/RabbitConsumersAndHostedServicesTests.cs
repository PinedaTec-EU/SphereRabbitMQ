using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.Abstractions.Consumers;
using SphereRabbitMQ.Abstractions.Topology;
using SphereRabbitMQ.DependencyInjection.Consumers;
using SphereRabbitMQ.Domain.Consumers;
using SphereRabbitMQ.Domain.Messaging;

namespace SphereRabbitMQ.Tests.Unit.DependencyInjection;

public sealed class RabbitConsumerErrorHandlingBuilderTests
{
    [Fact]
    public void Build_ReturnsRetryThenDeadLetter_WhenRetryAndDeadLetterAreEnabled()
    {
        var builder = new RabbitConsumerErrorHandlingBuilder
        {
            EnableRetry = true,
            EnableDeadLetter = true,
            MaxRetryAttempts = 7,
        };

        builder.NonRetriableExceptions.Add(typeof(InvalidOperationException));

        var settings = builder.Build("fallback.exchange", "fallback.route");

        Assert.Equal(ConsumerErrorStrategyKind.RetryThenDeadLetter, settings.Strategy);
        Assert.Equal(7, settings.MaxRetryAttempts);
        Assert.NotNull(settings.RetryRoute);
        Assert.Equal("fallback.exchange", settings.RetryRoute!.Exchange);
        Assert.Equal("fallback.route", settings.RetryRoute.RoutingKey);
        Assert.Equal("fallback.route", settings.RetryRoute.QueueName);
        Assert.NotNull(settings.DeadLetterRoute);
        Assert.Equal("fallback.exchange", settings.DeadLetterRoute!.Exchange);
        Assert.Equal("fallback.route", settings.DeadLetterRoute.RoutingKey);
        Assert.Equal("fallback.route", settings.DeadLetterRoute.QueueName);
        Assert.Contains(typeof(InvalidOperationException), settings.NonRetriableExceptions);
    }

    [Theory]
    [InlineData(true, false, ConsumerErrorStrategyKind.RetryOnly)]
    [InlineData(false, true, ConsumerErrorStrategyKind.DeadLetterOnly)]
    [InlineData(false, false, ConsumerErrorStrategyKind.Discard)]
    public void Build_ResolvesExpectedStrategy(bool enableRetry, bool enableDeadLetter, ConsumerErrorStrategyKind expected)
    {
        var builder = new RabbitConsumerErrorHandlingBuilder
        {
            EnableRetry = enableRetry,
            EnableDeadLetter = enableDeadLetter,
            RetryExchange = "retry.exchange",
            RetryRoutingKey = "retry.route",
            RetryQueue = "retry.queue",
            DeadLetterExchange = "dlx.exchange",
            DeadLetterRoutingKey = "dlx.route",
            DeadLetterQueue = "dlx.queue",
        };

        var settings = builder.Build("fallback.exchange", "fallback.route");

        Assert.Equal(expected, settings.Strategy);

        if (enableRetry)
        {
            Assert.NotNull(settings.RetryRoute);
            Assert.Equal("retry.exchange", settings.RetryRoute!.Exchange);
            Assert.Equal("retry.route", settings.RetryRoute.RoutingKey);
            Assert.Equal("retry.queue", settings.RetryRoute.QueueName);
        }
        else
        {
            Assert.Null(settings.RetryRoute);
        }

        if (enableDeadLetter)
        {
            Assert.NotNull(settings.DeadLetterRoute);
            Assert.Equal("dlx.exchange", settings.DeadLetterRoute!.Exchange);
            Assert.Equal("dlx.route", settings.DeadLetterRoute.RoutingKey);
            Assert.Equal("dlx.queue", settings.DeadLetterRoute.QueueName);
        }
        else
        {
            Assert.Null(settings.DeadLetterRoute);
        }
    }
}

public sealed class RabbitConsumerRegistrationTests
{
    [Fact]
    public async Task SubscribeAsync_ConfiguresBuilderAndSubscribesToQueue()
    {
        var subscriberMock = new Mock<ISubscriber>();
        ConsumerDefinition<string>? capturedDefinition = null;

        subscriberMock
            .Setup(s => s.SubscribeAsync(It.IsAny<ConsumerDefinition<string>>(), It.IsAny<CancellationToken>()))
            .Callback<ConsumerDefinition<string>, CancellationToken>((definition, _) => capturedDefinition = definition)
            .Returns(Task.CompletedTask);

        var registration = new RabbitConsumerRegistration<string>(builder =>
        {
            builder.Queue = "orders.queue";
            builder.Prefetch = 5;
            builder.MaxConcurrency = 2;
            builder.Handle((_, _) => Task.CompletedTask);
        });

        await registration.SubscribeAsync(new ServiceCollection().BuildServiceProvider(), subscriberMock.Object, CancellationToken.None);

        Assert.Equal("orders.queue", registration.QueueName);
        Assert.NotNull(capturedDefinition);
        Assert.Equal("orders.queue", capturedDefinition!.QueueName);
        Assert.Equal((ushort)5, capturedDefinition.PrefetchCount);
        Assert.Equal(2, capturedDefinition.MaxConcurrency);

        subscriberMock.Verify(s => s.SubscribeAsync(It.IsAny<ConsumerDefinition<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

public sealed class RabbitConsumerRegistrationBuilderTests
{
    [Fact]
    public void Build_Throws_WhenQueueIsMissing()
    {
        var builder = new RabbitConsumerRegistrationBuilder<string>();
        builder.Handle((_, _) => Task.CompletedTask);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build(new ServiceCollection().BuildServiceProvider()));

        Assert.Equal("Rabbit consumer queue name is required.", ex.Message);
    }

    [Fact]
    public void Build_Throws_WhenHandlerIsMissing()
    {
        var builder = new RabbitConsumerRegistrationBuilder<string>
        {
            Queue = "orders.queue",
        };

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build(new ServiceCollection().BuildServiceProvider()));

        Assert.Equal("Rabbit consumer 'String' requires a handler.", ex.Message);
    }

    [Fact]
    public async Task Build_UsesDirectHandler_WhenConfiguredWithHandle()
    {
        var builder = new RabbitConsumerRegistrationBuilder<string>
        {
            Queue = "orders.queue",
            Prefetch = 8,
            MaxConcurrency = 3,
            FallbackExchange = "orders.exchange",
            FallbackRoutingKey = "orders.route",
        };

        builder.ErrorHandling.EnableRetry = true;

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
        Assert.Equal(ConsumerErrorStrategyKind.RetryThenDeadLetter, definition.ErrorHandling.Strategy);
        Assert.Equal("orders.exchange", definition.ErrorHandling.RetryRoute!.Exchange);
    }

    [Fact]
    public async Task Build_UsesScopedHandler_WhenConfiguredWithUseHandler()
    {
        var services = new ServiceCollection();
        services.AddScoped<RecordingStringHandler>();
        var provider = services.BuildServiceProvider();

        var builder = new RabbitConsumerRegistrationBuilder<string>
        {
            Queue = "orders.queue",
        };

        builder.UseHandler<RecordingStringHandler>();

        var definition = builder.Build(provider);
        var envelope = new MessageEnvelope<string>("hello", new Dictionary<string, object?>(), "route", "exchange", null, null, null);

        await definition.Handler(envelope, CancellationToken.None);

        Assert.Equal(1, RecordingStringHandler.CallCount);
        Assert.Equal("hello", RecordingStringHandler.LastPayload);

        RecordingStringHandler.Reset();
    }

    private sealed class RecordingStringHandler : IRabbitMessageHandler<string>
    {
        public static int CallCount { get; private set; }

        public static string? LastPayload { get; private set; }

        public Task HandleAsync(MessageEnvelope<string> message, CancellationToken cancellationToken)
        {
            CallCount++;
            LastPayload = message.Body;
            return Task.CompletedTask;
        }

        public static void Reset()
        {
            CallCount = 0;
            LastPayload = null;
        }
    }
}

public sealed class RabbitMqConsumersHostedServiceTests
{
    [Fact]
    public async Task ExecuteAsync_SubscribesAllRegistrations()
    {
        var subscriberMock = new Mock<ISubscriber>();
        var logger = Mock.Of<ILogger<RabbitMqConsumersHostedService>>();

        subscriberMock
            .Setup(s => s.SubscribeAsync(It.IsAny<ConsumerDefinition<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        subscriberMock
            .Setup(s => s.SubscribeAsync(It.IsAny<ConsumerDefinition<int>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var registrations = new IRabbitConsumerRegistration[]
        {
            new RabbitConsumerRegistration<string>(builder =>
            {
                builder.Queue = "orders.queue";
                builder.Handle((_, _) => Task.CompletedTask);
            }),
            new RabbitConsumerRegistration<int>(builder =>
            {
                builder.Queue = "numbers.queue";
                builder.Handle((_, _) => Task.CompletedTask);
            }),
        };

        var hostedService = new RabbitMqConsumersHostedService(
            registrations,
            new ServiceCollection().BuildServiceProvider(),
            subscriberMock.Object,
            logger);

        var executeMethod = typeof(RabbitMqConsumersHostedService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(executeMethod);

        var task = (Task)executeMethod!.Invoke(hostedService, [CancellationToken.None])!;
        await task;

        subscriberMock.Verify(s => s.SubscribeAsync(It.IsAny<ConsumerDefinition<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        subscriberMock.Verify(s => s.SubscribeAsync(It.IsAny<ConsumerDefinition<int>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

public sealed class RabbitMqTopologyValidationHostedServiceTests
{
    [Fact]
    public async Task StartAsync_DoesNothing_WhenValidationOnStartupIsDisabled()
    {
        var topologyValidatorMock = new Mock<IRabbitMqTopologyValidator>();
        var options = Options.Create(new SphereRabbitMqOptions { ValidateTopologyOnStartup = false });
        var logger = Mock.Of<ILogger<RabbitMqTopologyValidationHostedService>>();

        var hostedService = new RabbitMqTopologyValidationHostedService(topologyValidatorMock.Object, options, logger);

        await hostedService.StartAsync(CancellationToken.None);

        topologyValidatorMock.Verify(v => v.ValidateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_ValidatesTopology_WhenValidationOnStartupIsEnabled()
    {
        var topologyValidatorMock = new Mock<IRabbitMqTopologyValidator>();
        var options = Options.Create(new SphereRabbitMqOptions { ValidateTopologyOnStartup = true });
        var logger = Mock.Of<ILogger<RabbitMqTopologyValidationHostedService>>();

        var hostedService = new RabbitMqTopologyValidationHostedService(topologyValidatorMock.Object, options, logger);

        await hostedService.StartAsync(CancellationToken.None);

        topologyValidatorMock.Verify(v => v.ValidateAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_CompletesImmediately()
    {
        var topologyValidatorMock = new Mock<IRabbitMqTopologyValidator>();
        var options = Options.Create(new SphereRabbitMqOptions { ValidateTopologyOnStartup = true });
        var logger = Mock.Of<ILogger<RabbitMqTopologyValidationHostedService>>();

        var hostedService = new RabbitMqTopologyValidationHostedService(topologyValidatorMock.Object, options, logger);

        var task = hostedService.StopAsync(CancellationToken.None);
        await task;

        Assert.True(task.IsCompletedSuccessfully);
    }
}
