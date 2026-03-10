namespace SphereRabbitMQ.IaC.Application.Models;

/// <summary>
/// Source-neutral broker connection settings used by automation adapters.
/// </summary>
public sealed record BrokerDocument
{
    public string? ManagementUrl { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public IReadOnlyList<string> VirtualHosts { get; init; } = Array.Empty<string>();
}
