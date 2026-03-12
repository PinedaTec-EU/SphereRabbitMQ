using Microsoft.Extensions.DependencyInjection;
using SphereRabbitMQ.Abstractions.Subscribers;
using SphereRabbitMQ.Domain.Subscribers;
using SphereRabbitMQ.Domain.Messaging;

namespace SphereRabbitMQ.DependencyInjection.Subscribers;

public sealed class RabbitSubscriberRegistrationBuilder<TMessage>
{
    private Func<IServiceProvider, Func<MessageEnvelope<TMessage>, CancellationToken, Task>>? _handlerFactory;
    private Func<IServiceProvider, Func<SubscriberDeadLetterNotification<TMessage>, CancellationToken, Task>?>? _deadLetterNotificationFactory;
    private Func<IServiceProvider, Func<SubscriberComponentFailureContext, CancellationToken, Task<SubscriberComponentFailureHandlingResult>>?>? _componentFailureHandlerFactory;

    public string Queue { get; set; } = string.Empty;

    public ushort Prefetch { get; set; }

    public int MaxConcurrency { get; set; } = 1;

    public RabbitSubscriberErrorHandlingBuilder ErrorHandling { get; } = new();

    public RabbitSubscriberRegistrationBuilder<TMessage> FromQueue(string queueName)
    {
        Queue = queueName;
        return this;
    }

    public RabbitSubscriberRegistrationBuilder<TMessage> WithPrefetchCount(ushort prefetchCount)
    {
        Prefetch = prefetchCount;
        return this;
    }

    public RabbitSubscriberRegistrationBuilder<TMessage> WithMaxConcurrency(int maxConcurrency)
    {
        MaxConcurrency = maxConcurrency;
        return this;
    }

    public RabbitSubscriberRegistrationBuilder<TMessage> Handle(Func<MessageEnvelope<TMessage>, CancellationToken, Task> handler)
    {
        _handlerFactory = _ => handler;
        return this;
    }

    public RabbitSubscriberRegistrationBuilder<TMessage> OnDeadLetter(Func<SubscriberDeadLetterNotification<TMessage>, CancellationToken, Task> handler)
    {
        _deadLetterNotificationFactory = _ => handler;
        return this;
    }

    public RabbitSubscriberRegistrationBuilder<TMessage> OnComponentFailure(Func<SubscriberComponentFailureContext, CancellationToken, Task<SubscriberComponentFailureHandlingResult>> handler)
    {
        _componentFailureHandlerFactory = _ => handler;
        return this;
    }

    public RabbitSubscriberRegistrationBuilder<TMessage> UseHandler<THandler>() where THandler : class, IRabbitSubscriberMessageHandler<TMessage>
        => UseHandler<THandler>(null);

    public RabbitSubscriberRegistrationBuilder<TMessage> UseHandler<THandler>(object? serviceKey) where THandler : class, IRabbitSubscriberMessageHandler<TMessage>
    {
        _handlerFactory = serviceProvider =>
        {
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
            return async (message, cancellationToken) =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = serviceKey is null
                    ? scope.ServiceProvider.GetRequiredService<THandler>()
                    : scope.ServiceProvider.GetRequiredKeyedService<THandler>(serviceKey);
                await handler.HandleAsync(message, cancellationToken);
            };
        };

        if (_deadLetterNotificationFactory is null)
        {
            _deadLetterNotificationFactory = serviceProvider =>
            {
                var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
                return async (notification, cancellationToken) =>
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var handler = serviceKey is null
                        ? scope.ServiceProvider.GetRequiredService<THandler>()
                        : scope.ServiceProvider.GetRequiredKeyedService<THandler>(serviceKey);
                    if (handler is IRabbitSubscriberDeadLetterNotificationHandler<TMessage> deadLetterNotificationHandler)
                    {
                        await deadLetterNotificationHandler.OnDeadLetterAsync(notification, cancellationToken);
                    }
                };
            };
        }

        if (_componentFailureHandlerFactory is null)
        {
            _componentFailureHandlerFactory = serviceProvider =>
            {
                var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
                return async (context, cancellationToken) =>
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var handler = serviceKey is null
                        ? scope.ServiceProvider.GetRequiredService<THandler>()
                        : scope.ServiceProvider.GetRequiredKeyedService<THandler>(serviceKey);
                    return handler is IRabbitSubscriberComponentFailureHandler<TMessage> componentFailureHandler
                        ? await componentFailureHandler.OnComponentFailureAsync(context, cancellationToken)
                        : SubscriberComponentFailureHandlingResult.UseDefault;
                };
            };
        }

        return this;
    }

    internal SubscriberDefinition<TMessage> Build(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Queue))
        {
            throw new InvalidOperationException("Rabbit subscriber queue name is required.");
        }

        if (_handlerFactory is null)
        {
            throw new InvalidOperationException($"Rabbit subscriber '{typeof(TMessage).Name}' requires a handler.");
        }

        return new SubscriberDefinition<TMessage>
        {
            QueueName = Queue,
            PrefetchCount = Prefetch,
            MaxConcurrency = MaxConcurrency,
            Handler = _handlerFactory(serviceProvider),
            DeadLetterNotificationHandler = _deadLetterNotificationFactory?.Invoke(serviceProvider),
            ComponentFailureHandler = _componentFailureHandlerFactory?.Invoke(serviceProvider),
            ErrorHandling = ErrorHandling.Build(),
        };
    }
}
