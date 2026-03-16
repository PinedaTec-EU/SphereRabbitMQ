using SphereRabbitMQ.Domain.Messaging;

namespace SphereRabbitMQ.Domain.Retry;

public sealed record SubscriberRetryDelayContext<TMessage>(
    string QueueName,
    MessageEnvelope<TMessage> Message,
    Exception Exception,
    int AttemptNumber);
