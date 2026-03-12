namespace SphereRabbitMQ.Domain.Subscribers;

public enum SubscriberErrorStrategyKind
{
    DeadLetterOnly,
    RetryThenDeadLetter,
    Discard,
}
