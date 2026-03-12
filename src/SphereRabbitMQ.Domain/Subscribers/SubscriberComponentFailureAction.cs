namespace SphereRabbitMQ.Domain.Subscribers;

public enum SubscriberComponentFailureAction
{
    UseDefault,
    Retry,
    DeadLetter,
    Discard,
}
