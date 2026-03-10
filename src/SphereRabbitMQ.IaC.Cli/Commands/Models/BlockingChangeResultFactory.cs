using SphereRabbitMQ.IaC.Domain.Planning;

namespace SphereRabbitMQ.IaC.Cli.Commands.Models;

internal static class BlockingChangeResultFactory
{
    internal static IReadOnlyList<BlockingChangeResult> Create(TopologyPlan plan)
        => plan.Operations
            .Where(operation => operation.Kind is TopologyPlanOperationKind.DestructiveChange or TopologyPlanOperationKind.UnsupportedChange)
            .OrderBy(operation => operation.ResourcePath, StringComparer.Ordinal)
            .ThenBy(operation => operation.Kind)
            .Select(operation => new BlockingChangeResult(
                RenderKind(operation.Kind),
                operation.ResourceKind,
                operation.ResourcePath,
                operation.Description,
                operation.Diffs))
            .ToArray();

    private static string RenderKind(TopologyPlanOperationKind kind)
        => kind switch
        {
            TopologyPlanOperationKind.DestructiveChange => "destructive",
            TopologyPlanOperationKind.UnsupportedChange => "unsupported",
            _ => kind.ToString(),
        };
}
