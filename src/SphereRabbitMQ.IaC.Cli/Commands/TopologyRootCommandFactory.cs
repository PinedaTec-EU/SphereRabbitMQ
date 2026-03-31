using System.CommandLine;

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
        By default this includes declared queues plus generated retry, dead-letter, and debug queues.
        Use --debug-only to purge only generated debug queues.
        Non-dry execution requires --allow-destructive and asks for confirmation unless --auto-approve is used.

        Examples:
          sprmq purge --file samples/minimal-topology.yaml --dry-run
          sprmq purge --file samples/minimal-topology.yaml --dry-run --debug-only
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

        var fileOption = new Option<string>("--file")
        {
            Required = true,
            Description = "Path to the topology YAML file.",
        };
        var templateOption = new Option<string>("--template")
        {
            DefaultValueFactory = _ => "minimal",
            Description = $"Template name. Available: {string.Join(", ", templateCatalog.GetTemplateNames())}.",
        };
        var initOutputPathOption = new Option<string>("--output-file")
        {
            DefaultValueFactory = _ => "-",
            Description = "Output file path for init. Use '-' to write YAML to stdout.",
        };
        var forceOption = new Option<bool>("--force")
        {
            DefaultValueFactory = _ => false,
            Description = "Overwrite the output file when it already exists.",
        };
        var outputFormatOption = new Option<TopologyOutputFormat>("--output")
        {
            DefaultValueFactory = _ => TopologyOutputFormat.Text,
            Description = "Output format: text or json.",
        };
        var managementUrlOption = new Option<string?>("--management-url")
        {
            Description = "RabbitMQ Management API URL.",
        };
        var usernameOption = new Option<string?>("--username")
        {
            Description = "RabbitMQ username.",
        };
        var passwordOption = new Option<string?>("--password")
        {
            Description = "RabbitMQ password.",
        };
        var virtualHostsOption = new Option<string[]>("--vhost")
        {
            DefaultValueFactory = _ => Array.Empty<string>(),
            Description = "Virtual host filter. Repeat to pass multiple values.",
        };
        var dryRunOption = new Option<bool>("--dry-run")
        {
            DefaultValueFactory = _ => false,
            Description = "Compute and print the apply result without changing the broker.",
        };
        var migrateOption = new Option<bool>("--migrate")
        {
            DefaultValueFactory = _ => false,
            Description = "Allow supported exchange and queue migrations when immutable broker arguments changed.",
        };
        var verboseOption = new Option<bool>("--verbose")
        {
            DefaultValueFactory = _ => false,
            Description = "Print detailed execution phases and broker operations.",
        };
        var allowDestructiveOption = new Option<bool>("--allow-destructive")
        {
            DefaultValueFactory = _ => false,
            Description = "Allow destructive execution for commands that delete broker resources.",
        };
        var autoApproveOption = new Option<bool>("--auto-approve")
        {
            DefaultValueFactory = _ => false,
            Description = "Skip the interactive confirmation prompt for destructive commands.",
        };
        var debugOnlyOption = new Option<bool>("--debug-only")
        {
            DefaultValueFactory = _ => false,
            Description = "Purge only generated debug queues.",
        };
        var exportOutputPathOption = new Option<string>("--output-file")
        {
            DefaultValueFactory = _ => "-",
            Description = "Output file path for export. Use '-' to write YAML to stdout.",
        };
        var exportFileOption = new Option<string?>("--file")
        {
            Description = "Optional topology YAML file used as a source for broker connection settings.",
        };
        var includeBrokerOption = new Option<bool>("--include-broker")
        {
            DefaultValueFactory = _ => false,
            Description = "Include the resolved broker settings in the exported YAML.",
        };
        var shellArgument = new Argument<string>("shell")
        {
            Description = "Target shell: bash, zsh, or pwsh.",
        };

        var rootCommand = new RootCommand(RootDescription);

        var initCommand = new Command("init", InitDescription);
        initCommand.Options.Add(templateOption);
        initCommand.Options.Add(initOutputPathOption);
        initCommand.Options.Add(forceOption);
        initCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var exitCode = await handler.InitAsync(
                parseResult.GetRequiredValue(templateOption),
                parseResult.GetRequiredValue(initOutputPathOption),
                parseResult.GetValue(forceOption),
                cancellationToken);
            return exitCode;
        });

        var validateCommand = new Command("validate", ValidateDescription);
        validateCommand.Options.Add(fileOption);
        validateCommand.Options.Add(outputFormatOption);
        validateCommand.Options.Add(verboseOption);
        validateCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var exitCode = await handler.ValidateAsync(
                parseResult.GetRequiredValue(fileOption),
                parseResult.GetValue(outputFormatOption),
                parseResult.GetValue(verboseOption),
                cancellationToken);
            return exitCode;
        });

        var planCommand = new Command("plan", PlanDescription);
        planCommand.Options.Add(fileOption);
        planCommand.Options.Add(outputFormatOption);
        planCommand.Options.Add(managementUrlOption);
        planCommand.Options.Add(usernameOption);
        planCommand.Options.Add(passwordOption);
        planCommand.Options.Add(virtualHostsOption);
        planCommand.Options.Add(verboseOption);
        planCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var brokerOptions = new BrokerOptionsInput(
                parseResult.GetValue(managementUrlOption),
                parseResult.GetValue(usernameOption),
                parseResult.GetValue(passwordOption),
                parseResult.GetValue(virtualHostsOption));
            var exitCode = await handler.PlanAsync(
                parseResult.GetRequiredValue(fileOption),
                brokerOptions,
                parseResult.GetValue(outputFormatOption),
                parseResult.GetValue(verboseOption),
                cancellationToken);
            return exitCode;
        });

        var applyCommand = new Command("apply", ApplyDescription);
        applyCommand.Options.Add(fileOption);
        applyCommand.Options.Add(outputFormatOption);
        applyCommand.Options.Add(managementUrlOption);
        applyCommand.Options.Add(usernameOption);
        applyCommand.Options.Add(passwordOption);
        applyCommand.Options.Add(virtualHostsOption);
        applyCommand.Options.Add(dryRunOption);
        applyCommand.Options.Add(migrateOption);
        applyCommand.Options.Add(verboseOption);
        applyCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var brokerOptions = new BrokerOptionsInput(
                parseResult.GetValue(managementUrlOption),
                parseResult.GetValue(usernameOption),
                parseResult.GetValue(passwordOption),
                parseResult.GetValue(virtualHostsOption));
            var exitCode = await handler.ApplyAsync(
                parseResult.GetRequiredValue(fileOption),
                brokerOptions,
                parseResult.GetValue(outputFormatOption),
                parseResult.GetValue(dryRunOption),
                parseResult.GetValue(migrateOption),
                parseResult.GetValue(verboseOption),
                cancellationToken);
            return exitCode;
        });

        var destroyCommand = new Command("destroy", DestroyDescription);
        destroyCommand.Options.Add(fileOption);
        destroyCommand.Options.Add(outputFormatOption);
        destroyCommand.Options.Add(managementUrlOption);
        destroyCommand.Options.Add(usernameOption);
        destroyCommand.Options.Add(passwordOption);
        destroyCommand.Options.Add(virtualHostsOption);
        destroyCommand.Options.Add(dryRunOption);
        destroyCommand.Options.Add(verboseOption);
        destroyCommand.Options.Add(allowDestructiveOption);
        destroyCommand.Options.Add(autoApproveOption);
        destroyCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var brokerOptions = new BrokerOptionsInput(
                parseResult.GetValue(managementUrlOption),
                parseResult.GetValue(usernameOption),
                parseResult.GetValue(passwordOption),
                parseResult.GetValue(virtualHostsOption));
            var exitCode = await handler.DestroyAsync(
                parseResult.GetRequiredValue(fileOption),
                brokerOptions,
                parseResult.GetValue(outputFormatOption),
                parseResult.GetValue(dryRunOption),
                parseResult.GetValue(verboseOption),
                parseResult.GetValue(allowDestructiveOption),
                parseResult.GetValue(autoApproveOption),
                true,
                cancellationToken);
            return exitCode;
        });

        var purgeCommand = new Command("purge", PurgeDescription);
        purgeCommand.Options.Add(fileOption);
        purgeCommand.Options.Add(outputFormatOption);
        purgeCommand.Options.Add(managementUrlOption);
        purgeCommand.Options.Add(usernameOption);
        purgeCommand.Options.Add(passwordOption);
        purgeCommand.Options.Add(virtualHostsOption);
        purgeCommand.Options.Add(dryRunOption);
        purgeCommand.Options.Add(verboseOption);
        purgeCommand.Options.Add(allowDestructiveOption);
        purgeCommand.Options.Add(autoApproveOption);
        purgeCommand.Options.Add(debugOnlyOption);
        purgeCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var brokerOptions = new BrokerOptionsInput(
                parseResult.GetValue(managementUrlOption),
                parseResult.GetValue(usernameOption),
                parseResult.GetValue(passwordOption),
                parseResult.GetValue(virtualHostsOption));
            var exitCode = await handler.PurgeAsync(
                parseResult.GetRequiredValue(fileOption),
                brokerOptions,
                parseResult.GetValue(outputFormatOption),
                parseResult.GetValue(dryRunOption),
                parseResult.GetValue(verboseOption),
                parseResult.GetValue(allowDestructiveOption),
                parseResult.GetValue(autoApproveOption),
                parseResult.GetValue(debugOnlyOption),
                cancellationToken);
            return exitCode;
        });

        var exportCommand = new Command("export", ExportDescription);
        exportCommand.Options.Add(exportFileOption);
        exportCommand.Options.Add(outputFormatOption);
        exportCommand.Options.Add(managementUrlOption);
        exportCommand.Options.Add(usernameOption);
        exportCommand.Options.Add(passwordOption);
        exportCommand.Options.Add(virtualHostsOption);
        exportCommand.Options.Add(exportOutputPathOption);
        exportCommand.Options.Add(includeBrokerOption);
        exportCommand.Options.Add(verboseOption);
        exportCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var brokerOptions = new BrokerOptionsInput(
                parseResult.GetValue(managementUrlOption),
                parseResult.GetValue(usernameOption),
                parseResult.GetValue(passwordOption),
                parseResult.GetValue(virtualHostsOption));
            var exitCode = await handler.ExportAsync(
                parseResult.GetValue(exportFileOption),
                brokerOptions,
                parseResult.GetRequiredValue(exportOutputPathOption),
                parseResult.GetValue(includeBrokerOption),
                parseResult.GetValue(outputFormatOption),
                parseResult.GetValue(verboseOption),
                cancellationToken);
            return exitCode;
        });

        var completionCommand = new Command("completion", CompletionDescription);
        completionCommand.Arguments.Add(shellArgument);
        completionCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var exitCode = await handler.CompletionAsync(
                parseResult.GetRequiredValue(shellArgument),
                cancellationToken);
            return exitCode;
        });

        rootCommand.Subcommands.Add(initCommand);
        rootCommand.Subcommands.Add(validateCommand);
        rootCommand.Subcommands.Add(planCommand);
        rootCommand.Subcommands.Add(applyCommand);
        rootCommand.Subcommands.Add(purgeCommand);
        rootCommand.Subcommands.Add(destroyCommand);
        rootCommand.Subcommands.Add(exportCommand);
        rootCommand.Subcommands.Add(completionCommand);

        return rootCommand;
    }
}
