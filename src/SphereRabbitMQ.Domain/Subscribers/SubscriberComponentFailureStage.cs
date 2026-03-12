namespace SphereRabbitMQ.Domain.Subscribers;

public enum SubscriberComponentFailureStage
{
    Deserialize,
    RetryForward,
    DeadLetterForward,
    Acknowledge,
}
