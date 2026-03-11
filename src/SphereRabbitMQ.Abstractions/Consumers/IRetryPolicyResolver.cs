using SphereRabbitMQ.Domain.Consumers;
using SphereRabbitMQ.Domain.Retry;

namespace SphereRabbitMQ.Abstractions.Consumers;

public interface IRetryPolicyResolver
{
    RetryDecision Resolve(
        ConsumerErrorHandlingSettings settings,
        RetryMetadata metadata,
        Exception exception);
}
