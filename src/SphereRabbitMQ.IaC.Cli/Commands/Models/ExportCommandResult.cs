using SphereRabbitMQ.IaC.Application.Models;

namespace SphereRabbitMQ.IaC.Cli.Commands.Models;

internal sealed record ExportCommandResult(
    string Content,
    TopologyDocument Document);
