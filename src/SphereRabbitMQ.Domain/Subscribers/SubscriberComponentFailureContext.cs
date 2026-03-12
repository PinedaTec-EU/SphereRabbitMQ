using SphereRabbitMQ.Domain.Messaging;
using SphereRabbitMQ.Domain.Retry;

namespace SphereRabbitMQ.Domain.Subscribers;

public sealed record SubscriberComponentFailureContext(
    string QueueName,
    MessageEnvelope<ReadOnlyMemory<byte>> Message,
    SubscriberErrorHandlingSettings ErrorHandling,
    RetryMetadata RetryMetadata,
    SubscriberComponentFailureStage Stage,
    Exception Exception);
