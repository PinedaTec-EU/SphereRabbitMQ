using System.Text;
using SphereRabbitMQ.IaC.Cli.Commands.Models;
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
        AppendBroker(builder, result.Broker);
        builder.AppendLine(result.Validation.IsValid ? "Validation succeeded." : "Validation failed.");
        AppendIssues(builder, result.Validation.Issues);

        if (!result.Validation.IsValid)
        {
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine("Plan:");
        foreach (var operation in result.Plan.Operations)
        {
            builder.AppendLine($"- [{operation.Kind}] {operation.ResourcePath}: {operation.Description}");
        }

        AppendBlockingChanges(builder, result.BlockingChanges);

        return builder.ToString().TrimEnd();
    }

    internal static string RenderApply(ApplyCommandResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine(result.DryRun ? "Dry-run apply completed." : "Apply completed.");
        builder.Append(CommandOutputRenderer.RenderPlan(new PlanCommandResult(result.Broker, result.Validation, result.Plan)));
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
