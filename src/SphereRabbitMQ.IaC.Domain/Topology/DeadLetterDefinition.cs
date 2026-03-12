using System.Diagnostics.CodeAnalysis;

using SphereRabbitMQ.IaC.Domain.Internal;

namespace SphereRabbitMQ.IaC.Domain.Topology;

/// <summary>
/// Describes the dead-letter behavior for a queue.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record DeadLetterDefinition
{
    public DeadLetterDefinition(
        bool enabled = true,
        string? exchangeName = null,
        string? queueName = null,
        string? routingKey = null,
        TimeSpan? ttl = null)
    {
        Enabled = enabled;
        ExchangeName = NormalizeOptional(exchangeName);
        QueueName = NormalizeOptional(queueName);
        RoutingKey = NormalizeOptional(routingKey);
        if (ttl is not null && ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "Dead-letter ttl must be a positive TimeSpan.");
        }

        Ttl = ttl;
    }

    public bool Enabled { get; }

    public string? ExchangeName { get; }

    public string? QueueName { get; }

    public string? RoutingKey { get; }

    public TimeSpan? Ttl { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Guard.AgainstNullOrWhiteSpace(value, nameof(value));
}
