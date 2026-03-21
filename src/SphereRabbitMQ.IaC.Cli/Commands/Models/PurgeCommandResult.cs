using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Cli.Commands.Models;

internal sealed record PurgeQueueResult(
    string VirtualHostName,
    string QueueName,
    string ResourcePath);

internal sealed record PurgeCommandResult(
    bool DryRun,
    BrokerResolutionResult Broker,
    TopologyValidationResult Validation,
    IReadOnlyList<PurgeQueueResult> Queues);
