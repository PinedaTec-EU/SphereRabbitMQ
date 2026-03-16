namespace SphereRabbitMQ.IaC.Application.Models;

/// <summary>
/// Enables debug generation for main (declared) and secondary (generated) artifacts.
/// </summary>
public sealed record DebugQueueScopeDocument
{
    public bool Main { get; init; }

    public bool Secondary { get; init; }
}
