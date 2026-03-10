using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Cli.Commands.Models;

internal sealed record PlanCommandResult(
    BrokerResolutionResult Broker,
    TopologyValidationResult Validation,
    TopologyPlan Plan)
{
    public IReadOnlyList<BlockingChangeResult> BlockingChanges => BlockingChangeResultFactory.Create(Plan);
}
