namespace SphereRabbitMQ.Domain.Publishing;

public sealed record PublisherFailureContext(
    string Exchange,
    string RoutingKey,
    Type MessageType,
    PublisherFailureStage Stage,
    Exception Exception);
