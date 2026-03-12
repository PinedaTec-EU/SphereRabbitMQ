using SphereRabbitMQ.Domain.Subscribers;

namespace SphereRabbitMQ.Abstractions.Subscribers;

public interface ISubscriberInfrastructureRouteResolver
{
    RetryRouteDefinition ResolveRetryRoute(string queueName);

    DeadLetterRouteDefinition ResolveDeadLetterRoute(string queueName);
}
