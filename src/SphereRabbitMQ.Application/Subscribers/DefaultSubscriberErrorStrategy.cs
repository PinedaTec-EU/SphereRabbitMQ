using SphereRabbitMQ.Abstractions.Subscribers;
using SphereRabbitMQ.Domain.Subscribers;
using SphereRabbitMQ.Domain.Messaging;
using SphereRabbitMQ.Domain.Retry;

namespace SphereRabbitMQ.Application.Subscribers;

public sealed class DefaultSubscriberErrorStrategy : ISubscriberErrorStrategy
{
    public SubscriberFailureDecision Resolve<TMessage>(
        MessageEnvelope<TMessage> message,
        SubscriberErrorHandlingSettings settings,
        Exception exception,
        RetryDecision retryDecision)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(retryDecision);

        if (exception is DiscardMessageException)
        {
            return new SubscriberFailureDecision(
                SubscriberFailureDisposition.Discard,
                retryDecision.NextRetryCount,
                settings.RetryRoute,
                settings.DeadLetterRoute);
        }

        if (retryDecision.ShouldRetry)
        {
            return new SubscriberFailureDecision(
                SubscriberFailureDisposition.Retry,
                retryDecision.NextRetryCount,
                settings.RetryRoute,
                settings.DeadLetterRoute);
        }

        if (settings.Strategy is SubscriberErrorStrategyKind.DeadLetterOnly or SubscriberErrorStrategyKind.RetryThenDeadLetter &&
            settings.DeadLetterRoute is not null)
        {
            return new SubscriberFailureDecision(
                SubscriberFailureDisposition.DeadLetter,
                retryDecision.NextRetryCount,
                settings.RetryRoute,
                settings.DeadLetterRoute);
        }

        return new SubscriberFailureDecision(
            SubscriberFailureDisposition.Discard,
            retryDecision.NextRetryCount,
            settings.RetryRoute,
            settings.DeadLetterRoute);
    }
}
