using System.Diagnostics.CodeAnalysis;

namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

/// <summary>
/// YAML contract for naming policy overrides.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record NamingConventionYamlDocument
{
    public string? Separator { get; init; }

    public string? RetryExchangeSuffix { get; init; }

    public string? RetryQueueSuffix { get; init; }

    public string? DeadLetterExchangeSuffix { get; init; }

    public string? DeadLetterQueueSuffix { get; init; }

    public string? ParkingLotQueueSuffix { get; init; }

    public string? StepTokenPrefix { get; init; }
}
