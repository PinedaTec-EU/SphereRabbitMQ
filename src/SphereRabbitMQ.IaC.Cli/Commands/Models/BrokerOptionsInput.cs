namespace SphereRabbitMQ.IaC.Cli.Commands.Models;

internal sealed record BrokerOptionsInput(
    string? ManagementUrl,
    string? Username,
    string? Password,
    IReadOnlyList<string>? VirtualHosts);
