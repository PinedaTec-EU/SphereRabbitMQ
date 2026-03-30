using System.Text;
using SphereRabbitMQ.IaC.Cli.Commands.Models;
using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Cli.Commands;

internal static class CommandOutputRenderer
{
    internal static string RenderValidation(ValidateCommandResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine(result.Validation.IsValid ? "Topology is valid." : "Topology is invalid.");
        AppendIssues(builder, result.Validation.Issues);
        return builder.ToString().TrimEnd();
    }

    internal static string RenderPlan(PlanCommandResult result)
    {
        var builder = new StringBuilder();
        AppendPlanBody(builder, result.Broker, result.Validation, result.Plan, result.BlockingChanges, "Plan:");
        return builder.ToString().TrimEnd();
    }

    internal static string RenderApply(ApplyCommandResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine(RenderApplyStatus(result));
        AppendPlanBody(builder, result.Broker, result.Validation, result.Plan, result.BlockingChanges, RenderApplyHeading(result));

        if (result.Validation.IsValid)
        {
            AppendApplySummary(builder, result);
        }

        return builder.ToString().TrimEnd();
    }

    internal static string RenderDestroy(DestroyCommandResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine(result.DryRun ? "Dry-run destroy completed." : "Destroy completed.");
        builder.AppendLine(result.DestroyVirtualHost
            ? "Destroy scope: full virtual host deletion."
            : "Destroy scope: declared resources only.");
        AppendBroker(builder, result.Broker);
        builder.AppendLine(result.Validation.IsValid ? "Validation succeeded." : "Validation failed.");
        AppendIssues(builder, result.Validation.Issues);

        if (!result.Validation.IsValid)
        {
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine("Destroy plan:");
        foreach (var operation in result.Plan.Operations)
        {
            builder.AppendLine($"- [{operation.Kind}] {operation.ResourcePath}: {operation.Description}");
        }

        AppendBlockingChanges(builder, result.BlockingChanges);
        AppendPlanSummary(builder, "Destroy summary:", result.Plan);

        return builder.ToString().TrimEnd();
    }

    internal static string RenderPurge(PurgeCommandResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine(result.DryRun ? "Dry-run purge completed." : "Purge completed.");
        AppendBroker(builder, result.Broker);
        builder.AppendLine(result.Validation.IsValid ? "Validation succeeded." : "Validation failed.");
        AppendIssues(builder, result.Validation.Issues);

        if (!result.Validation.IsValid)
        {
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine(result.Queues.Count == 0 ? "No queues matched the topology." : "Queues to purge:");

        foreach (var queue in result.Queues)
        {
            builder.AppendLine($"- {queue.ResourcePath}");
        }

        builder.AppendLine($"{(result.DryRun ? "Dry-run purge summary:" : "Purge summary:")} queues: {result.Queues.Count}.");

        return builder.ToString().TrimEnd();
    }

    internal static string RenderExport(ExportCommandResult result)
    {
        var builder = new StringBuilder();
        AppendBroker(builder, result.Broker);
        builder.AppendLine("Export completed.");
        return builder.ToString().TrimEnd();
    }

    private static void AppendIssues(StringBuilder builder, IReadOnlyList<TopologyIssue> issues)
    {
        foreach (var issue in issues)
        {
            builder.AppendLine($"- [{issue.Severity}] {issue.Path}: {issue.Message}");
        }
    }

    private static void AppendPlanBody(
        StringBuilder builder,
        BrokerResolutionResult broker,
        TopologyValidationResult validation,
        TopologyPlan plan,
        IReadOnlyList<BlockingChangeResult> blockingChanges,
        string heading)
    {
        AppendBroker(builder, broker);
        builder.AppendLine(validation.IsValid ? "Validation succeeded." : "Validation failed.");
        AppendIssues(builder, validation.Issues);

        if (!validation.IsValid)
        {
            return;
        }

        builder.AppendLine(heading);
        foreach (var operation in plan.Operations)
        {
            builder.AppendLine($"- [{operation.Kind}] {operation.ResourcePath}: {operation.Description}");
        }

        AppendBlockingChanges(builder, blockingChanges);
    }

    private static void AppendPlanSummary(
        StringBuilder builder,
        string heading,
        TopologyPlan plan)
    {
        var counts = plan.Operations
            .GroupBy(operation => operation.Kind)
            .ToDictionary(group => group.Key, group => group.Count());

        builder.AppendLine(
            $"{heading} noop: {GetCount(TopologyPlanOperationKind.NoOp)}, create: {GetCount(TopologyPlanOperationKind.Create)}, update: {GetCount(TopologyPlanOperationKind.Update)}, destroy: {GetCount(TopologyPlanOperationKind.Destroy)}, destructive: {GetCount(TopologyPlanOperationKind.DestructiveChange)}, unsupported: {GetCount(TopologyPlanOperationKind.UnsupportedChange)}.");
        return;

        int GetCount(TopologyPlanOperationKind kind)
            => counts.TryGetValue(kind, out var count) ? count : 0;
    }

    private static void AppendApplySummary(
        StringBuilder builder,
        ApplyCommandResult result)
    {
        var counts = result.Plan.Operations
            .GroupBy(operation => operation.Kind)
            .ToDictionary(group => group.Key, group => group.Count());
        var isPreview = result.DryRun || !result.Executed;

        builder.AppendLine(
            isPreview
                ? $"Plan summary: noop: {GetCount(TopologyPlanOperationKind.NoOp)}, would-create: {GetCount(TopologyPlanOperationKind.Create)}, would-update: {GetCount(TopologyPlanOperationKind.Update)}, would-destroy: {GetCount(TopologyPlanOperationKind.Destroy)}, blocking-destructive: {GetCount(TopologyPlanOperationKind.DestructiveChange)}, blocking-unsupported: {GetCount(TopologyPlanOperationKind.UnsupportedChange)}."
                : $"Apply result: noop: {GetCount(TopologyPlanOperationKind.NoOp)}, created: {GetCount(TopologyPlanOperationKind.Create)}, updated: {GetCount(TopologyPlanOperationKind.Update)}, destroyed: {GetCount(TopologyPlanOperationKind.Destroy)}, blocking-destructive: {GetCount(TopologyPlanOperationKind.DestructiveChange)}, blocking-unsupported: {GetCount(TopologyPlanOperationKind.UnsupportedChange)}.");
        return;

        int GetCount(TopologyPlanOperationKind kind)
            => counts.TryGetValue(kind, out var count) ? count : 0;
    }

    private static string RenderApplyStatus(ApplyCommandResult result)
    {
        if (!result.Validation.IsValid)
        {
            return result.DryRun ? "Dry-run apply validation failed." : "Apply validation failed.";
        }

        if (result.DryRun)
        {
            return "Dry-run apply completed.";
        }

        return result.Executed ? "Apply completed." : "Apply blocked.";
    }

    private static string RenderApplyHeading(ApplyCommandResult result)
    {
        if (result.DryRun || !result.Executed)
        {
            return "Planned operations:";
        }

        return "Applied operations:";
    }

    private static void AppendBroker(StringBuilder builder, BrokerResolutionResult broker)
    {
        builder.AppendLine("Broker settings:");
        builder.AppendLine($"- managementUrl: {broker.ManagementUrl.Value} ({RenderSource(broker.ManagementUrl.Source)})");
        builder.AppendLine($"- username: {broker.Username.Value} ({RenderSource(broker.Username.Source)})");
        builder.AppendLine($"- password: ({RenderSource(broker.PasswordSource)})");
        builder.AppendLine($"- virtualHosts: {string.Join(", ", broker.VirtualHosts.Value)} ({RenderSource(broker.VirtualHosts.Source)})");
    }

    private static void AppendBlockingChanges(StringBuilder builder, IReadOnlyList<BlockingChangeResult> blockingChanges)
    {
        if (blockingChanges.Count == 0)
        {
            return;
        }

        builder.AppendLine("Blocking plan operations:");
        foreach (var blockingChange in blockingChanges)
        {
            builder.AppendLine($"- [{blockingChange.Kind}] {blockingChange.ResourcePath}: {blockingChange.Reason}");

            foreach (var diff in blockingChange.Diffs.OrderBy(diff => diff.PropertyName, StringComparer.Ordinal))
            {
                builder.AppendLine($"  diff {diff.PropertyName}: desired={diff.DesiredValue ?? "<null>"} actual={diff.ActualValue ?? "<null>"}");
            }
        }
    }

    private static string RenderSource(BrokerOptionSource source)
        => source switch
        {
            BrokerOptionSource.CommandLine => "command-line",
            BrokerOptionSource.Environment => "environment",
            BrokerOptionSource.Yaml => "yaml",
            BrokerOptionSource.Default => "default",
            BrokerOptionSource.Derived => "derived",
            _ => source.ToString(),
        };
}
