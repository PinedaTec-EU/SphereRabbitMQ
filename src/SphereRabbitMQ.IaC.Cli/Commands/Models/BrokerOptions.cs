namespace SphereRabbitMQ.IaC.Cli.Commands.Models;

internal sealed record BrokerOptions(
    string ManagementUrl,
    string Username,
    string Password,
    IReadOnlyList<string> VirtualHosts);
