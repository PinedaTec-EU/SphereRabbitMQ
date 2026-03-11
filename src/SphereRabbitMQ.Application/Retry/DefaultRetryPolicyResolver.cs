using SphereRabbitMQ.Abstractions.Consumers;
using SphereRabbitMQ.Domain.Consumers;
using SphereRabbitMQ.Domain.Retry;

namespace SphereRabbitMQ.Application.Retry;

public sealed class DefaultRetryPolicyResolver : IRetryPolicyResolver
{
    public RetryDecision Resolve(
        ConsumerErrorHandlingSettings settings,
        RetryMetadata metadata,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is DiscardMessageException or NonRetriableMessageException)
        {
            return new RetryDecision(false, metadata.RetryCount);
        }

        var isNonRetriable = settings.NonRetriableExceptions.Any(nonRetriableType => nonRetriableType.IsInstanceOfType(exception));
        if (isNonRetriable)
        {
            return new RetryDecision(false, metadata.RetryCount);
        }

        var nextRetryCount = metadata.RetryCount + 1;
        var shouldRetry = settings.Strategy is ConsumerErrorStrategyKind.RetryOnly or ConsumerErrorStrategyKind.RetryThenDeadLetter
            && settings.RetryRoute is not null
            && nextRetryCount <= settings.MaxRetryAttempts;

        return new RetryDecision(shouldRetry, nextRetryCount);
    }
}
