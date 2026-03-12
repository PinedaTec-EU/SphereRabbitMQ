namespace SphereRabbitMQ.Domain.Subscribers;

public enum SubscriberFailureDisposition
{
    Retry,
    DeadLetter,
    Discard,
}
