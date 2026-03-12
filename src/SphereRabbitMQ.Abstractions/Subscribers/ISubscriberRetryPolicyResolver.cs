using SphereRabbitMQ.Domain.Subscribers;
using SphereRabbitMQ.Domain.Retry;

namespace SphereRabbitMQ.Abstractions.Subscribers;

public interface IRetryPolicyResolver
{
    RetryDecision Resolve(
        SubscriberErrorHandlingSettings settings,
        RetryMetadata metadata,
        Exception exception);
}
