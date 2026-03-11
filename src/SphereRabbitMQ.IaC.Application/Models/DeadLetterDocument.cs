using System.Diagnostics.CodeAnalysis;

namespace SphereRabbitMQ.IaC.Application.Models;

/// <summary>
/// Source-neutral dead-letter document.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record DeadLetterDocument
{
    public bool Enabled { get; init; } = true;

    public string? ExchangeName { get; init; }

    public string? QueueName { get; init; }

    public string? RoutingKey { get; init; }
}
