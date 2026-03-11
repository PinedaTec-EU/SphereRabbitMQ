namespace SphereRabbitMQ.Domain.Retry;

public sealed record RetryDecision(bool ShouldRetry, int NextRetryCount);
