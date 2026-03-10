using SphereRabbitMQ.IaC.Domain.Internal;

namespace SphereRabbitMQ.IaC.Domain.Topology;

/// <summary>
/// Describes broker-based retry topology for a queue.
/// </summary>
public sealed record RetryDefinition
{
    public RetryDefinition(
        IReadOnlyList<RetryStepDefinition> steps,
        bool enabled = true,
        bool autoGenerateArtifacts = true,
        string? exchangeName = null,
        string? parkingLotQueueName = null)
    {
        Steps = Guard.AgainstNull(steps, nameof(steps));
        Enabled = enabled;
        AutoGenerateArtifacts = autoGenerateArtifacts;
        ExchangeName = string.IsNullOrWhiteSpace(exchangeName)
            ? null
            : Guard.AgainstNullOrWhiteSpace(exchangeName, nameof(exchangeName));
        ParkingLotQueueName = string.IsNullOrWhiteSpace(parkingLotQueueName)
            ? null
            : Guard.AgainstNullOrWhiteSpace(parkingLotQueueName, nameof(parkingLotQueueName));
    }

    public IReadOnlyList<RetryStepDefinition> Steps { get; }

    public bool Enabled { get; }

    public bool AutoGenerateArtifacts { get; }

    public string? ExchangeName { get; }

    public string? ParkingLotQueueName { get; }
}
