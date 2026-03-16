using SphereRabbitMQ.Abstractions.Subscribers;
using SphereRabbitMQ.Domain.Retry;

namespace SphereRabbitMQ.Application.Retry;

public sealed class DefaultSubscriberRetryDelayResolver<TMessage> : ISubscriberRetryDelayResolver<TMessage>
{
    private const int RetryDelayMillisecondsStep = 250;

    public TimeSpan Resolve(SubscriberRetryDelayContext<TMessage> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.AttemptNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context), "Retry attempt number must be greater than zero.");
        }

        return TimeSpan.FromMilliseconds(context.AttemptNumber * RetryDelayMillisecondsStep);
    }
}
