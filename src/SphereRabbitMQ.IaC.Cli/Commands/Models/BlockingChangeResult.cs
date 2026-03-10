using SphereRabbitMQ.IaC.Domain.Planning;

namespace SphereRabbitMQ.IaC.Cli.Commands.Models;

internal sealed record BlockingChangeResult(
    string Kind,
    TopologyResourceKind ResourceKind,
    string ResourcePath,
    string Reason,
    IReadOnlyList<TopologyDiff> Diffs);
