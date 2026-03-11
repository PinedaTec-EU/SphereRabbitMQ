using SphereRabbitMQ.Abstractions.Consumers;
using SphereRabbitMQ.Domain.Consumers;
using SphereRabbitMQ.Domain.Messaging;
using SphereRabbitMQ.Domain.Retry;

namespace SphereRabbitMQ.Application.Consumers;

public sealed class DefaultConsumerErrorStrategy : IConsumerErrorStrategy
{
    public ConsumerFailureDecision Resolve<TMessage>(
        MessageEnvelope<TMessage> message,
        ConsumerErrorHandlingSettings settings,
        Exception exception,
        RetryDecision retryDecision)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(retryDecision);

        if (exception is DiscardMessageException)
        {
            return new ConsumerFailureDecision(
                ConsumerFailureDisposition.Discard,
                retryDecision.NextRetryCount,
                settings.RetryRoute,
                settings.DeadLetterRoute);
        }

        if (retryDecision.ShouldRetry)
        {
            return new ConsumerFailureDecision(
                ConsumerFailureDisposition.Retry,
                retryDecision.NextRetryCount,
                settings.RetryRoute,
                settings.DeadLetterRoute);
        }

        if (settings.Strategy is ConsumerErrorStrategyKind.DeadLetterOnly or ConsumerErrorStrategyKind.RetryThenDeadLetter &&
            settings.DeadLetterRoute is not null)
        {
            return new ConsumerFailureDecision(
                ConsumerFailureDisposition.DeadLetter,
                retryDecision.NextRetryCount,
                settings.RetryRoute,
                settings.DeadLetterRoute);
        }

        return new ConsumerFailureDecision(
            ConsumerFailureDisposition.Discard,
            retryDecision.NextRetryCount,
            settings.RetryRoute,
            settings.DeadLetterRoute);
    }
}
