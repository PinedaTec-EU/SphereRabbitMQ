namespace SphereRabbitMQ.IaC.Cli.Commands.Models;

internal sealed record BrokerResolutionResult(
    BrokerOptions Options,
    BrokerOptionValue<string> ManagementUrl,
    BrokerOptionValue<string> Username,
    BrokerOptionSource PasswordSource,
    BrokerOptionValue<IReadOnlyList<string>> VirtualHosts);
