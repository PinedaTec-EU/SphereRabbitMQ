namespace SphereRabbitMQ.Domain.Consumers;

public sealed record RetryRouteDefinition(string Exchange, string RoutingKey, string? QueueName = null);
