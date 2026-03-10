using SphereRabbitMQ.IaC.Domain.Internal;

namespace SphereRabbitMQ.IaC.Domain.Topology;

/// <summary>
/// Describes a single broker-based retry step using TTL and dead-letter reinjection.
/// </summary>
public sealed record RetryStepDefinition
{
    public RetryStepDefinition(TimeSpan delay, string? name = null, string? queueName = null, string? routingKey = null)
    {
        if (delay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), "Retry delay must be greater than zero.");
        }

        Delay = delay;
        Name = string.IsNullOrWhiteSpace(name) ? null : Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        QueueName = string.IsNullOrWhiteSpace(queueName) ? null : Guard.AgainstNullOrWhiteSpace(queueName, nameof(queueName));
        RoutingKey = string.IsNullOrWhiteSpace(routingKey) ? null : Guard.AgainstNullOrWhiteSpace(routingKey, nameof(routingKey));
    }

    public TimeSpan Delay { get; }

    public string? Name { get; }

    public string? QueueName { get; }

    public string? RoutingKey { get; }
}
