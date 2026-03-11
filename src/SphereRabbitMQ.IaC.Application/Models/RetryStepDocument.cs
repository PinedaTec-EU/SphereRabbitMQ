using System.Diagnostics.CodeAnalysis;

namespace SphereRabbitMQ.IaC.Application.Models;

/// <summary>
/// Source-neutral retry step document.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record RetryStepDocument
{
    public required string Delay { get; init; }

    public string? Name { get; init; }

    public string? QueueName { get; init; }

    public string? RoutingKey { get; init; }
}
