namespace SphereRabbitMQ.Domain.Consumers;

public enum ConsumerErrorStrategyKind
{
    DeadLetterOnly,
    RetryOnly,
    RetryThenDeadLetter,
    Discard,
}
