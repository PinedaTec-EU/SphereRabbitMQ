using System.Diagnostics.CodeAnalysis;

namespace SphereRabbitMQ.IaC.Application.Models;

/// <summary>
/// Source-neutral naming policy document.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record NamingConventionDocument
{
    public string? Separator { get; init; }

    public string? RetryExchangeSuffix { get; init; }

    public string? RetryQueueSuffix { get; init; }

    public string? DeadLetterExchangeSuffix { get; init; }

    public string? DeadLetterQueueSuffix { get; init; }

    public string? StepTokenPrefix { get; init; }
}
