namespace SphereRabbitMQ.Domain.Subscribers;

public enum SubscriberErrorStrategyKind
{
    DeadLetterOnly,
    RetryOnly,
    RetryThenDeadLetter,
    Discard,
}
