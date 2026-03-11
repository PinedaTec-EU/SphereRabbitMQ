using Microsoft.Extensions.DependencyInjection;
using SphereRabbitMQ.Abstractions.Consumers;
using SphereRabbitMQ.Domain.Consumers;
using SphereRabbitMQ.Domain.Messaging;

namespace SphereRabbitMQ.DependencyInjection.Consumers;

public sealed class RabbitConsumerRegistrationBuilder<TMessage>
{
    private Func<IServiceProvider, Func<MessageEnvelope<TMessage>, CancellationToken, Task>>? _handlerFactory;

    public string Queue { get; set; } = string.Empty;

    public ushort Prefetch { get; set; } = 1;

    public int MaxConcurrency { get; set; } = 1;

    public RabbitConsumerErrorHandlingBuilder ErrorHandling { get; } = new();

    public string FallbackExchange { get; set; } = string.Empty;

    public string FallbackRoutingKey { get; set; } = string.Empty;

    public void Handle(Func<MessageEnvelope<TMessage>, CancellationToken, Task> handler)
        => _handlerFactory = _ => handler;

    public void UseHandler<THandler>() where THandler : class, IRabbitMessageHandler<TMessage>
        => _handlerFactory = serviceProvider =>
        {
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
            return async (message, cancellationToken) =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<THandler>();
                await handler.HandleAsync(message, cancellationToken);
            };
        };

    internal ConsumerDefinition<TMessage> Build(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Queue))
        {
            throw new InvalidOperationException("Rabbit consumer queue name is required.");
        }

        if (_handlerFactory is null)
        {
            throw new InvalidOperationException($"Rabbit consumer '{typeof(TMessage).Name}' requires a handler.");
        }

        return new ConsumerDefinition<TMessage>
        {
            QueueName = Queue,
            PrefetchCount = Prefetch,
            MaxConcurrency = MaxConcurrency,
            Handler = _handlerFactory(serviceProvider),
            ErrorHandling = ErrorHandling.Build(FallbackExchange, FallbackRoutingKey),
        };
    }
}
