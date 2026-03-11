namespace SphereRabbitMQ.Domain.Retry;

public sealed record RetryMetadata(int RetryCount)
{
    public static RetryMetadata None { get; } = new(0);

    public RetryMetadata Increment() => new(RetryCount + 1);
}
