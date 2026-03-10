using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;
using SphereRabbitMQ.IaC.Cli.Commands.Models;

namespace SphereRabbitMQ.IaC.Cli.Commands;

internal static class TopologyRootCommandFactory
{
    internal static RootCommand Create(IServiceProvider serviceProvider)
    {
        var handler = serviceProvider.GetRequiredService<TopologyCommandHandler>();

        var fileOption = new Option<string>("--file") { IsRequired = true, Description = "Path to the topology YAML file." };
        var outputFormatOption = new Option<TopologyOutputFormat>("--output", () => TopologyOutputFormat.Text, "Output format: text or json.");
        var managementUrlOption = new Option<string>("--management-url", () => Environment.GetEnvironmentVariable("SPHERE_RABBITMQ_MANAGEMENT_URL") ?? "http://localhost:15672/api/", "RabbitMQ Management API URL.");
        var usernameOption = new Option<string>("--username", () => Environment.GetEnvironmentVariable("SPHERE_RABBITMQ_USERNAME") ?? "guest", "RabbitMQ username.");
        var passwordOption = new Option<string>("--password", () => Environment.GetEnvironmentVariable("SPHERE_RABBITMQ_PASSWORD") ?? "guest", "RabbitMQ password.");
        var virtualHostsOption = new Option<string[]>("--vhost", () => Array.Empty<string>(), "Virtual host filter. Repeat to pass multiple values.");
        var dryRunOption = new Option<bool>("--dry-run", () => false, "Compute and print the apply result without changing the broker.");
        var exportOutputPathOption = new Option<string>("--output-file", () => "-", "Output file path for export. Use '-' to write YAML to stdout.");

        var rootCommand = new RootCommand("SphereRabbitMQ.IaC");

        var validateCommand = new Command("validate", "Validate YAML syntax, normalization, and topology semantics.");
        validateCommand.AddOption(fileOption);
        validateCommand.AddOption(outputFormatOption);
        Handler.SetHandler(validateCommand, async (InvocationContext context) =>
        {
            var parseResult = context.ParseResult;
            var cancellationToken = context.GetCancellationToken();
            var exitCode = await handler.ValidateAsync(
                parseResult.GetValueForOption(fileOption)!,
                parseResult.GetValueForOption(outputFormatOption),
                cancellationToken);
            context.ExitCode = exitCode;
        });

        var planCommand = new Command("plan", "Compare desired topology against broker state and print a reconciliation plan.");
        planCommand.AddOption(fileOption);
        planCommand.AddOption(outputFormatOption);
        planCommand.AddOption(managementUrlOption);
        planCommand.AddOption(usernameOption);
        planCommand.AddOption(passwordOption);
        planCommand.AddOption(virtualHostsOption);
        Handler.SetHandler(planCommand, async (InvocationContext context) =>
        {
            var parseResult = context.ParseResult;
            var cancellationToken = context.GetCancellationToken();
            var exitCode = await handler.PlanAsync(
                parseResult.GetValueForOption(fileOption)!,
                CreateBrokerOptions(parseResult, managementUrlOption, usernameOption, passwordOption, virtualHostsOption),
                parseResult.GetValueForOption(outputFormatOption),
                cancellationToken);
            context.ExitCode = exitCode;
        });

        var applyCommand = new Command("apply", "Apply a topology plan to RabbitMQ.");
        applyCommand.AddOption(fileOption);
        applyCommand.AddOption(outputFormatOption);
        applyCommand.AddOption(managementUrlOption);
        applyCommand.AddOption(usernameOption);
        applyCommand.AddOption(passwordOption);
        applyCommand.AddOption(virtualHostsOption);
        applyCommand.AddOption(dryRunOption);
        Handler.SetHandler(applyCommand, async (InvocationContext context) =>
        {
            var parseResult = context.ParseResult;
            var cancellationToken = context.GetCancellationToken();
            var exitCode = await handler.ApplyAsync(
                parseResult.GetValueForOption(fileOption)!,
                CreateBrokerOptions(parseResult, managementUrlOption, usernameOption, passwordOption, virtualHostsOption),
                parseResult.GetValueForOption(outputFormatOption),
                parseResult.GetValueForOption(dryRunOption),
                cancellationToken);
            context.ExitCode = exitCode;
        });

        var exportCommand = new Command("export", "Export broker topology to YAML.");
        exportCommand.AddOption(outputFormatOption);
        exportCommand.AddOption(managementUrlOption);
        exportCommand.AddOption(usernameOption);
        exportCommand.AddOption(passwordOption);
        exportCommand.AddOption(virtualHostsOption);
        exportCommand.AddOption(exportOutputPathOption);
        Handler.SetHandler(exportCommand, async (InvocationContext context) =>
        {
            var parseResult = context.ParseResult;
            var cancellationToken = context.GetCancellationToken();
            var exitCode = await handler.ExportAsync(
                CreateBrokerOptions(parseResult, managementUrlOption, usernameOption, passwordOption, virtualHostsOption),
                parseResult.GetValueForOption(exportOutputPathOption)!,
                parseResult.GetValueForOption(outputFormatOption),
                cancellationToken);
            context.ExitCode = exitCode;
        });

        rootCommand.AddCommand(validateCommand);
        rootCommand.AddCommand(planCommand);
        rootCommand.AddCommand(applyCommand);
        rootCommand.AddCommand(exportCommand);

        return rootCommand;
    }

    private static BrokerOptions CreateBrokerOptions(
        ParseResult parseResult,
        Option<string> managementUrlOption,
        Option<string> usernameOption,
        Option<string> passwordOption,
        Option<string[]> virtualHostsOption)
        => new(
            parseResult.GetValueForOption(managementUrlOption)!,
            parseResult.GetValueForOption(usernameOption)!,
            parseResult.GetValueForOption(passwordOption)!,
            parseResult.GetValueForOption(virtualHostsOption)!);
}
