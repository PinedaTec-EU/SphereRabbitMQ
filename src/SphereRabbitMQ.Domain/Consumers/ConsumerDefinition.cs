using SphereRabbitMQ.Domain.Messaging;

namespace SphereRabbitMQ.Domain.Consumers;

public sealed record ConsumerDefinition<TMessage>
{
    public required string QueueName { get; init; }

    public ushort PrefetchCount { get; init; } = 1;

    public int MaxConcurrency { get; init; } = 1;

    public required Func<MessageEnvelope<TMessage>, CancellationToken, Task> Handler { get; init; }

    public ConsumerErrorHandlingSettings ErrorHandling { get; init; } = new();
}
