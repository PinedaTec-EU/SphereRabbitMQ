namespace SphereRabbitMQ.Domain.Consumers;

public sealed record ConsumerFailureDecision(
    ConsumerFailureDisposition Disposition,
    int RetryCount,
    RetryRouteDefinition? RetryRoute,
    DeadLetterRouteDefinition? DeadLetterRoute);
