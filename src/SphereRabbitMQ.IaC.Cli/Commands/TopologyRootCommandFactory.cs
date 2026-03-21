using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

using Microsoft.Extensions.DependencyInjection;

using SphereRabbitMQ.IaC.Cli.Commands.Models;
using SphereRabbitMQ.IaC.Cli.Templates.Interfaces;

namespace SphereRabbitMQ.IaC.Cli.Commands;

internal static class TopologyRootCommandFactory
{
    private const string RootDescription = """
        RabbitMQ topology infrastructure-as-code for CI/CD and operator automation.

        Commands:
          validate  Validate topology files without contacting RabbitMQ.
          init      Create a topology YAML template.
          plan      Compare desired topology with the broker. Never changes broker state.
          apply     Apply only safe reconciliation operations. Refuses destructive changes.
          purge     Remove messages from all queues declared by the topology. Requires confirmation unless --auto-approve is used.
          destroy   Intentionally delete declared virtual hosts. Requires confirmation unless --auto-approve is used.
          export    Export broker topology to YAML.
          completion  Print shell completion scripts.

        Examples:
          sprmq init --template minimal --output-file topology.yaml
          sprmq plan --file samples/minimal-topology.yaml
          sprmq apply --file samples/minimal-topology.yaml --dry-run
          sprmq purge --file samples/minimal-topology.yaml --dry-run
          sprmq destroy --file samples/minimal-topology.yaml --dry-run
          sprmq destroy --file samples/minimal-topology.yaml --allow-destructive --auto-approve
          sprmq completion zsh
        """;
    private const string InitDescription = """
        Create a topology YAML file from a built-in template.

        Example:
          sprmq init --template retry-dead-letter --output-file topology.yaml
        """;
    private const string ValidateDescription = """
        Validate topology YAML syntax, normalization, and semantic consistency.

        Example:
          sprmq validate --file samples/minimal-topology.yaml
        """;
    private const string PlanDescription = """
        Compare desired topology against broker state and print a reconciliation plan.
        This command never changes broker state.

        Example:
          sprmq plan --file samples/minimal-topology.yaml
        """;
    private const string ApplyDescription = """
        Apply a topology plan to RabbitMQ.
        This command only performs safe reconciliation and refuses destructive plans unless --migrate is provided.

        Example:
          sprmq apply --file samples/minimal-topology.yaml --dry-run
          sprmq apply --file samples/minimal-topology.yaml --migrate
        """;
    private const string DestroyDescription = """
        Intentionally delete every virtual host declared in the YAML file.
        Non-dry execution requires --allow-destructive and asks for confirmation unless --auto-approve is used.

        Examples:
          sprmq destroy --file samples/minimal-topology.yaml --dry-run
          sprmq destroy --file samples/minimal-topology.yaml --allow-destructive --auto-approve
        """;
    private const string PurgeDescription = """
        Remove all messages from every queue declared by the normalized topology.
        Generated retry, dead-letter, and debug queues are included when present in the YAML.
        Non-dry execution requires --allow-destructive and asks for confirmation unless --auto-approve is used.

        Examples:
          sprmq purge --file samples/minimal-topology.yaml --dry-run
          sprmq purge --file samples/minimal-topology.yaml --allow-destructive --auto-approve
        """;
    private const string ExportDescription = """
        Export broker topology to YAML.

        Example:
          sprmq export --file samples/minimal-topology.yaml --output-file topology.yaml
          sprmq export --include-broker --output-file topology.yaml
        """;
    private const string CompletionDescription = """
        Print shell completion scripts for bash, zsh, or pwsh.

        Example:
          sprmq completion bash
        """;

    internal static RootCommand Create(IServiceProvider serviceProvider)
    {
        var handler = serviceProvider.GetRequiredService<TopologyCommandHandler>();
        var templateCatalog = serviceProvider.GetRequiredService<ITopologyTemplateCatalog>();

        var fileOption = new Option<string>("--file") { IsRequired = true, Description = "Path to the topology YAML file." };
        var templateOption = new Option<string>("--template", () => "minimal", $"Template name. Available: {string.Join(", ", templateCatalog.GetTemplateNames())}.");
        var initOutputPathOption = new Option<string>("--output-file", () => "-", "Output file path for init. Use '-' to write YAML to stdout.");
        var forceOption = new Option<bool>("--force", () => false, "Overwrite the output file when it already exists.");
        var outputFormatOption = new Option<TopologyOutputFormat>("--output", () => TopologyOutputFormat.Text, "Output format: text or json.");
        var managementUrlOption = new Option<string?>("--management-url", "RabbitMQ Management API URL.");
        var usernameOption = new Option<string?>("--username", "RabbitMQ username.");
        var passwordOption = new Option<string?>("--password", "RabbitMQ password.");
        var virtualHostsOption = new Option<string[]?>("--vhost", "Virtual host filter. Repeat to pass multiple values.");
        var dryRunOption = new Option<bool>("--dry-run", () => false, "Compute and print the apply result without changing the broker.");
        var migrateOption = new Option<bool>("--migrate", () => false, "Allow supported exchange and queue migrations when immutable broker arguments changed.");
        var verboseOption = new Option<bool>("--verbose", () => false, "Print detailed execution phases and broker operations.");
        var allowDestructiveOption = new Option<bool>("--allow-destructive", () => false, "Allow destructive execution for commands that delete broker resources.");
        var autoApproveOption = new Option<bool>("--auto-approve", () => false, "Skip the interactive confirmation prompt for destructive commands.");
        var exportOutputPathOption = new Option<string>("--output-file", () => "-", "Output file path for export. Use '-' to write YAML to stdout.");
        var exportFileOption = new Option<string?>("--file", "Optional topology YAML file used as a source for broker connection settings.");
        var includeBrokerOption = new Option<bool>("--include-broker", () => false, "Include the resolved broker settings in the exported YAML.");
        var shellArgument = new Argument<string>("shell", "Target shell: bash, zsh, or pwsh.");

        var rootCommand = new RootCommand("SphereRabbitMQ.IaC")
        {
            Description = RootDescription,
        };

        var initCommand = new Command("init")
        {
            Description = InitDescription,
        };
        initCommand.AddOption(templateOption);
        initCommand.AddOption(initOutputPathOption);
        initCommand.AddOption(forceOption);
        Handler.SetHandler(initCommand, async (InvocationContext context) =>
        {
            var parseResult = context.ParseResult;
            var cancellationToken = context.GetCancellationToken();
            var exitCode = await handler.InitAsync(
                parseResult.GetValueForOption(templateOption)!,
                parseResult.GetValueForOption(initOutputPathOption)!,
                parseResult.GetValueForOption(forceOption),
                cancellationToken);
            context.ExitCode = exitCode;
        });

        var validateCommand = new Command("validate")
        {
            Description = ValidateDescription,
        };
        validateCommand.AddOption(fileOption);
        validateCommand.AddOption(outputFormatOption);
        validateCommand.AddOption(verboseOption);
        Handler.SetHandler(validateCommand, async (InvocationContext context) =>
        {
            var parseResult = context.ParseResult;
            var cancellationToken = context.GetCancellationToken();
            var exitCode = await handler.ValidateAsync(
                parseResult.GetValueForOption(fileOption)!,
                parseResult.GetValueForOption(outputFormatOption),
                parseResult.GetValueForOption(verboseOption),
                cancellationToken);
            context.ExitCode = exitCode;
        });

        var planCommand = new Command("plan")
        {
            Description = PlanDescription,
        };
        planCommand.AddOption(fileOption);
        planCommand.AddOption(outputFormatOption);
        planCommand.AddOption(managementUrlOption);
        planCommand.AddOption(usernameOption);
        planCommand.AddOption(passwordOption);
        planCommand.AddOption(virtualHostsOption);
        planCommand.AddOption(verboseOption);
        Handler.SetHandler(planCommand, async (InvocationContext context) =>
        {
            var parseResult = context.ParseResult;
            var cancellationToken = context.GetCancellationToken();
            var exitCode = await handler.PlanAsync(
                parseResult.GetValueForOption(fileOption)!,
                CreateBrokerOptions(parseResult, managementUrlOption, usernameOption, passwordOption, virtualHostsOption),
                parseResult.GetValueForOption(outputFormatOption),
                parseResult.GetValueForOption(verboseOption),
                cancellationToken);
            context.ExitCode = exitCode;
        });

        var applyCommand = new Command("apply")
        {
            Description = ApplyDescription,
        };
        applyCommand.AddOption(fileOption);
        applyCommand.AddOption(outputFormatOption);
        applyCommand.AddOption(managementUrlOption);
        applyCommand.AddOption(usernameOption);
        applyCommand.AddOption(passwordOption);
        applyCommand.AddOption(virtualHostsOption);
        applyCommand.AddOption(dryRunOption);
        applyCommand.AddOption(migrateOption);
        applyCommand.AddOption(verboseOption);
        Handler.SetHandler(applyCommand, async (InvocationContext context) =>
        {
            var parseResult = context.ParseResult;
            var cancellationToken = context.GetCancellationToken();
            var exitCode = await handler.ApplyAsync(
                parseResult.GetValueForOption(fileOption)!,
                CreateBrokerOptions(parseResult, managementUrlOption, usernameOption, passwordOption, virtualHostsOption),
                parseResult.GetValueForOption(outputFormatOption),
                parseResult.GetValueForOption(dryRunOption),
                parseResult.GetValueForOption(migrateOption),
                parseResult.GetValueForOption(verboseOption),
                cancellationToken);
            context.ExitCode = exitCode;
        });

        var destroyCommand = new Command("destroy")
        {
            Description = DestroyDescription,
        };
        var purgeCommand = new Command("purge")
        {
            Description = PurgeDescription,
        };
        destroyCommand.AddOption(fileOption);
        destroyCommand.AddOption(outputFormatOption);
        destroyCommand.AddOption(managementUrlOption);
        destroyCommand.AddOption(usernameOption);
        destroyCommand.AddOption(passwordOption);
        destroyCommand.AddOption(virtualHostsOption);
        destroyCommand.AddOption(dryRunOption);
        destroyCommand.AddOption(verboseOption);
        destroyCommand.AddOption(allowDestructiveOption);
        destroyCommand.AddOption(autoApproveOption);
        Handler.SetHandler(destroyCommand, async (InvocationContext context) =>
        {
            var parseResult = context.ParseResult;
            var cancellationToken = context.GetCancellationToken();
            var exitCode = await handler.DestroyAsync(
                parseResult.GetValueForOption(fileOption)!,
                CreateBrokerOptions(parseResult, managementUrlOption, usernameOption, passwordOption, virtualHostsOption),
                parseResult.GetValueForOption(outputFormatOption),
                parseResult.GetValueForOption(dryRunOption),
                parseResult.GetValueForOption(verboseOption),
                parseResult.GetValueForOption(allowDestructiveOption),
                parseResult.GetValueForOption(autoApproveOption),
                true,
                cancellationToken);
            context.ExitCode = exitCode;
        });

        purgeCommand.AddOption(fileOption);
        purgeCommand.AddOption(outputFormatOption);
        purgeCommand.AddOption(managementUrlOption);
        purgeCommand.AddOption(usernameOption);
        purgeCommand.AddOption(passwordOption);
        purgeCommand.AddOption(virtualHostsOption);
        purgeCommand.AddOption(dryRunOption);
        purgeCommand.AddOption(verboseOption);
        purgeCommand.AddOption(allowDestructiveOption);
        purgeCommand.AddOption(autoApproveOption);
        Handler.SetHandler(purgeCommand, async (InvocationContext context) =>
        {
            var parseResult = context.ParseResult;
            var cancellationToken = context.GetCancellationToken();
            var exitCode = await handler.PurgeAsync(
                parseResult.GetValueForOption(fileOption)!,
                CreateBrokerOptions(parseResult, managementUrlOption, usernameOption, passwordOption, virtualHostsOption),
                parseResult.GetValueForOption(outputFormatOption),
                parseResult.GetValueForOption(dryRunOption),
                parseResult.GetValueForOption(verboseOption),
                parseResult.GetValueForOption(allowDestructiveOption),
                parseResult.GetValueForOption(autoApproveOption),
                cancellationToken);
            context.ExitCode = exitCode;
        });

        var exportCommand = new Command("export")
        {
            Description = ExportDescription,
        };
        var completionCommand = new Command("completion")
        {
            Description = CompletionDescription,
        };
        exportCommand.AddOption(exportFileOption);
        exportCommand.AddOption(outputFormatOption);
        exportCommand.AddOption(managementUrlOption);
        exportCommand.AddOption(usernameOption);
        exportCommand.AddOption(passwordOption);
        exportCommand.AddOption(virtualHostsOption);
        exportCommand.AddOption(exportOutputPathOption);
        exportCommand.AddOption(includeBrokerOption);
        exportCommand.AddOption(verboseOption);
        Handler.SetHandler(exportCommand, async (InvocationContext context) =>
        {
            var parseResult = context.ParseResult;
            var cancellationToken = context.GetCancellationToken();
            var exitCode = await handler.ExportAsync(
                parseResult.GetValueForOption(exportFileOption),
                CreateBrokerOptions(parseResult, managementUrlOption, usernameOption, passwordOption, virtualHostsOption),
                parseResult.GetValueForOption(exportOutputPathOption)!,
                parseResult.GetValueForOption(includeBrokerOption),
                parseResult.GetValueForOption(outputFormatOption),
                parseResult.GetValueForOption(verboseOption),
                cancellationToken);
            context.ExitCode = exitCode;
        });

        completionCommand.AddArgument(shellArgument);
        Handler.SetHandler(completionCommand, async (InvocationContext context) =>
        {
            var parseResult = context.ParseResult;
            var cancellationToken = context.GetCancellationToken();
            var exitCode = await handler.CompletionAsync(
                parseResult.GetValueForArgument(shellArgument)!,
                cancellationToken);
            context.ExitCode = exitCode;
        });

        rootCommand.AddCommand(initCommand);
        rootCommand.AddCommand(validateCommand);
        rootCommand.AddCommand(planCommand);
        rootCommand.AddCommand(applyCommand);
        rootCommand.AddCommand(purgeCommand);
        rootCommand.AddCommand(destroyCommand);
        rootCommand.AddCommand(exportCommand);
        rootCommand.AddCommand(completionCommand);

        return rootCommand;
    }

    private static BrokerOptionsInput CreateBrokerOptions(
        ParseResult parseResult,
        Option<string?> managementUrlOption,
        Option<string?> usernameOption,
        Option<string?> passwordOption,
        Option<string[]?> virtualHostsOption)
        => new(
            parseResult.GetValueForOption(managementUrlOption),
            parseResult.GetValueForOption(usernameOption),
            parseResult.GetValueForOption(passwordOption),
            parseResult.GetValueForOption(virtualHostsOption));
}
