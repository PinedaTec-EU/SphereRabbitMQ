namespace SphereRabbitMQ.Domain.Routing;

public sealed class RoutingKeyValidator
{
    public bool IsValid(string routingKey)
        => !string.IsNullOrWhiteSpace(routingKey) &&
           !routingKey.Split('.', StringSplitOptions.None).Any(segment => segment.Length == 0);

    public void EnsureValid(string routingKey)
    {
        if (!IsValid(routingKey))
        {
            throw new ArgumentException($"Routing key '{routingKey}' is invalid.", nameof(routingKey));
        }
    }
}
