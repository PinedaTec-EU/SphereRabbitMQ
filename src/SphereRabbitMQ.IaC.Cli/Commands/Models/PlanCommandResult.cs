using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Cli.Commands.Models;

internal sealed record PlanCommandResult(
    TopologyValidationResult Validation,
    TopologyPlan Plan);
