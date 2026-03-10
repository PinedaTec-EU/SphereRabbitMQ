namespace SphereRabbitMQ.IaC.Application.Models;

/// <summary>
/// Source-neutral retry document.
/// </summary>
public sealed record RetryDocument
{
    public bool Enabled { get; init; } = true;

    public bool AutoGenerateArtifacts { get; init; } = true;

    public string? ExchangeName { get; init; }

    public string? ParkingLotQueueName { get; init; }

    public IReadOnlyList<RetryStepDocument> Steps { get; init; } = Array.Empty<RetryStepDocument>();
}
