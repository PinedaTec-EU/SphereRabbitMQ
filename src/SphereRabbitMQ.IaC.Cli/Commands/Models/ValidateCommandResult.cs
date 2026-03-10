using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Cli.Commands.Models;

internal sealed record ValidateCommandResult(TopologyValidationResult Validation);
