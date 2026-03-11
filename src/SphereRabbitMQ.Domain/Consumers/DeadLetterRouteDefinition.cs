namespace SphereRabbitMQ.Domain.Consumers;

public sealed record DeadLetterRouteDefinition(string Exchange, string RoutingKey, string? QueueName = null);
