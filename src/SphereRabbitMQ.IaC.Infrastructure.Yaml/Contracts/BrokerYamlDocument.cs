namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

/// <summary>
/// YAML contract for broker connection settings used by adapters.
/// </summary>
public sealed record BrokerYamlDocument
{
    public string? ManagementUrl { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public List<string> VirtualHosts { get; init; } = new();
}
