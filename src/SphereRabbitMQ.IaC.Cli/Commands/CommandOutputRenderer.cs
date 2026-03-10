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

        return builder.ToString().TrimEnd();
    }

    internal static string RenderApply(ApplyCommandResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine(result.DryRun ? "Dry-run apply completed." : "Apply completed.");
        builder.Append(CommandOutputRenderer.RenderPlan(new PlanCommandResult(result.Validation, result.Plan)));
        return builder.ToString().TrimEnd();
    }

    private static void AppendIssues(StringBuilder builder, IReadOnlyList<TopologyIssue> issues)
    {
        foreach (var issue in issues)
        {
            builder.AppendLine($"- [{issue.Severity}] {issue.Path}: {issue.Message}");
        }
    }
}
