namespace SphereRabbitMQ.Domain.Subscribers;

public sealed record RetryRouteDefinition(string Exchange, string RoutingKey, string? QueueName = null);
