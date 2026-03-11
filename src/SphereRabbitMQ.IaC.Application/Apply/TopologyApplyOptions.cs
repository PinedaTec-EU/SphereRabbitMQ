namespace SphereRabbitMQ.IaC.Application.Apply;

/// <summary>
/// Controls how a topology apply operation is executed.
/// </summary>
public sealed record TopologyApplyOptions
{
    /// <summary>
    /// Default safe apply behavior.
    /// </summary>
    public static TopologyApplyOptions Safe { get; } = new();

    /// <summary>
    /// Enables broker-side migration for supported destructive and unsupported changes.
    /// </summary>
    public bool AllowMigrations { get; init; }
}
