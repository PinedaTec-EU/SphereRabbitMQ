using SphereRabbitMQ.Domain.Messaging;

namespace SphereRabbitMQ.Domain.Subscribers;

public sealed record SubscriberDeadLetterNotification<TMessage>(
    MessageEnvelope<TMessage> Message,
    Exception Exception,
    SubscriberFailureDecision Decision);
