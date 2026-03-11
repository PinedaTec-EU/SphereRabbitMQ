namespace SphereRabbitMQ.Domain.Routing;

public sealed class RoutingKeyBuilder
{
    private readonly RoutingKeyValidator _validator = new();

    public string Build(params string[] segments)
    {
        var routingKey = string.Join('.', segments.Select(segment => segment?.Trim() ?? string.Empty));
        _validator.EnsureValid(routingKey);
        return routingKey;
    }

    public string BuildTopicPattern(params string[] segments) => Build(segments);
}
