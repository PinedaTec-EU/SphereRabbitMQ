using SphereRabbitMQ.Domain.Subscribers;
using SphereRabbitMQ.Domain.Messaging;
using SphereRabbitMQ.Domain.Retry;

namespace SphereRabbitMQ.Abstractions.Subscribers;

public interface ISubscriberErrorStrategy
{
    SubscriberFailureDecision Resolve<TMessage>(
        MessageEnvelope<TMessage> message,
        SubscriberErrorHandlingSettings settings,
        Exception exception,
        RetryDecision retryDecision);
}
