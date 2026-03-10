namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

/// <summary>
/// YAML contract for retry configuration.
/// </summary>
public sealed record RetryYamlDocument
{
    public bool Enabled { get; init; } = true;

    public bool AutoGenerateArtifacts { get; init; } = true;

    public string? ExchangeName { get; init; }

    public string? ParkingLotQueueName { get; init; }

    public List<RetryStepYamlDocument> Steps { get; init; } = new();
}
