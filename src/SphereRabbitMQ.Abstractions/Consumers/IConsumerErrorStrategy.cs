using SphereRabbitMQ.Domain.Consumers;
using SphereRabbitMQ.Domain.Messaging;
using SphereRabbitMQ.Domain.Retry;

namespace SphereRabbitMQ.Abstractions.Consumers;

public interface IConsumerErrorStrategy
{
    ConsumerFailureDecision Resolve<TMessage>(
        MessageEnvelope<TMessage> message,
        ConsumerErrorHandlingSettings settings,
        Exception exception,
        RetryDecision retryDecision);
}
