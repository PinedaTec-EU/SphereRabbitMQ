namespace SphereRabbitMQ.Domain.Subscribers;

public sealed record DeadLetterRouteDefinition(string Exchange, string RoutingKey, string? QueueName = null);
