using SphereRabbitMQ.Abstractions.Subscribers;
using SphereRabbitMQ.Domain.Subscribers;

namespace SphereRabbitMQ.Application.Subscribers;

public sealed class DefaultSubscriberInfrastructureRouteResolver : ISubscriberInfrastructureRouteResolver
{
    private const string DeadLetterExchangeSuffix = "dlx";
    private const string DeadLetterQueueSuffix = "dlq";
    private const string RetryExchangeSuffix = "retry";
    private const string RetryStepToken = "step1";

    public RetryRouteDefinition ResolveRetryRoute(string queueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        var retryExchange = $"{queueName}.{RetryExchangeSuffix}";
        var retryQueue = $"{queueName}.{RetryExchangeSuffix}.{RetryStepToken}";
        return new RetryRouteDefinition(retryExchange, retryQueue, retryQueue);
    }

    public DeadLetterRouteDefinition ResolveDeadLetterRoute(string queueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        var deadLetterExchange = $"{queueName}.{DeadLetterExchangeSuffix}";
        var deadLetterQueue = $"{queueName}.{DeadLetterQueueSuffix}";
        return new DeadLetterRouteDefinition(deadLetterExchange, deadLetterQueue, deadLetterQueue);
    }
}
