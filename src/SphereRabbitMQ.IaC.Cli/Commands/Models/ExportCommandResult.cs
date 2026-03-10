using SphereRabbitMQ.IaC.Application.Models;

namespace SphereRabbitMQ.IaC.Cli.Commands.Models;

internal sealed record ExportCommandResult(
    BrokerResolutionResult Broker,
    string Content,
    TopologyDocument Document);
