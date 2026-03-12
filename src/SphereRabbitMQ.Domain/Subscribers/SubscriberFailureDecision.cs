namespace SphereRabbitMQ.Domain.Subscribers;

public sealed record SubscriberFailureDecision(
    SubscriberFailureDisposition Disposition,
    int RetryCount,
    RetryRouteDefinition? RetryRoute,
    DeadLetterRouteDefinition? DeadLetterRoute);
