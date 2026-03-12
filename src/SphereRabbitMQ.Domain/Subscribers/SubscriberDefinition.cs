using SphereRabbitMQ.Domain.Messaging;

namespace SphereRabbitMQ.Domain.Subscribers;

public abstract record SubscriberDefinition
{
    public required string QueueName { get; init; }

    public ushort PrefetchCount { get; init; }

    public int MaxConcurrency { get; init; } = 1;

    public SubscriberErrorHandlingSettings ErrorHandling { get; init; } = new();
}

public sealed record SubscriberDefinition<TMessage> : SubscriberDefinition
{
    public required Func<MessageEnvelope<TMessage>, CancellationToken, Task> Handler { get; init; }

    public Func<SubscriberDeadLetterNotification<TMessage>, CancellationToken, Task>? DeadLetterNotificationHandler { get; init; }

    public Func<SubscriberComponentFailureContext, CancellationToken, Task<SubscriberComponentFailureHandlingResult>>? ComponentFailureHandler { get; init; }
}
