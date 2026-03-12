namespace SphereRabbitMQ.Domain.Publishing;

public enum PublisherFailureStage
{
    EnsureExchangeExists,
    Serialize,
    Publish,
}
