namespace SphereRabbitMQ.Domain.Subscribers;

public sealed record SubscriberComponentFailureHandlingResult(
    SubscriberComponentFailureAction Action)
{
    public static SubscriberComponentFailureHandlingResult UseDefault { get; } =
        new(SubscriberComponentFailureAction.UseDefault);
}
