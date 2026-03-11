namespace SphereRabbitMQ.Domain.Consumers;

public enum ConsumerFailureDisposition
{
    Retry,
    DeadLetter,
    Discard,
}
