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

    private static Option<string> CreateRequiredFileOption()
        => new("--file")
        {
            Required = true,
            Description = "Path to the topology YAML file.",
        };

    private static Option<string?> CreateOptionalFileOption()
        => new("--file")
        {
            Description = "Optional topology YAML file used as a source for broker connection settings.",
        };

    private static Option<TopologyOutputFormat> CreateOutputFormatOption()
        => new("--output")
        {
            DefaultValueFactory = _ => TopologyOutputFormat.Text,
            Description = "Output format: text or json.",
        };

    private static Option<string?> CreateManagementUrlOption()
        => new("--management-url")
        {
            Description = "RabbitMQ Management API URL.",
        };

    private static Option<string?> CreateUsernameOption()
        => new("--username")
        {
            Description = "RabbitMQ username.",
        };

    private static Option<string?> CreatePasswordOption()
        => new("--password")
        {
            Description = "RabbitMQ password.",
        };

    private static Option<string[]> CreateVirtualHostsOption()
        => new("--vhost")
        {
            DefaultValueFactory = _ => Array.Empty<string>(),
            Description = "Virtual host filter. Repeat to pass multiple values.",
        };

    private static Option<bool> CreateDryRunOption()
        => new("--dry-run")
        {
            DefaultValueFactory = _ => false,
            Description = "Compute and print the apply result without changing the broker.",
        };

    private static Option<bool> CreateVerboseOption()
        => new("--verbose")
        {
            DefaultValueFactory = _ => false,
            Description = "Print detailed execution phases and broker operations.",
        };

    internal static RootCommand Create(IServiceProvider serviceProvider)
    {
        var handler = serviceProvider.GetRequiredService<TopologyCommandHandler>();
        var templateCatalog = serviceProvider.GetRequiredService<ITopologyTemplateCatalog>();

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
        var migrateOption = new Option<bool>("--migrate")
        {
            DefaultValueFactory = _ => false,
            Description = "Allow supported exchange and queue migrations when immutable broker arguments changed.",
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
        var validateFileOption = CreateRequiredFileOption();
        var validateOutputFormatOption = CreateOutputFormatOption();
        var validateVerboseOption = CreateVerboseOption();
        validateCommand.Options.Add(validateFileOption);
        validateCommand.Options.Add(validateOutputFormatOption);
        validateCommand.Options.Add(validateVerboseOption);
        validateCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var exitCode = await handler.ValidateAsync(
                parseResult.GetRequiredValue(validateFileOption),
                parseResult.GetValue(validateOutputFormatOption),
                parseResult.GetValue(validateVerboseOption),
                cancellationToken);
            return exitCode;
        });

        var planCommand = new Command("plan", PlanDescription);
        var planFileOption = CreateRequiredFileOption();
        var planOutputFormatOption = CreateOutputFormatOption();
        var planManagementUrlOption = CreateManagementUrlOption();
        var planUsernameOption = CreateUsernameOption();
        var planPasswordOption = CreatePasswordOption();
        var planVirtualHostsOption = CreateVirtualHostsOption();
        var planVerboseOption = CreateVerboseOption();
        planCommand.Options.Add(planFileOption);
        planCommand.Options.Add(planOutputFormatOption);
        planCommand.Options.Add(planManagementUrlOption);
        planCommand.Options.Add(planUsernameOption);
        planCommand.Options.Add(planPasswordOption);
        planCommand.Options.Add(planVirtualHostsOption);
        planCommand.Options.Add(planVerboseOption);
        planCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var brokerOptions = new BrokerOptionsInput(
                parseResult.GetValue(planManagementUrlOption),
                parseResult.GetValue(planUsernameOption),
                parseResult.GetValue(planPasswordOption),
                parseResult.GetValue(planVirtualHostsOption));
            var exitCode = await handler.PlanAsync(
                parseResult.GetRequiredValue(planFileOption),
                brokerOptions,
                parseResult.GetValue(planOutputFormatOption),
                parseResult.GetValue(planVerboseOption),
                cancellationToken);
            return exitCode;
        });

        var applyCommand = new Command("apply", ApplyDescription);
        var applyFileOption = CreateRequiredFileOption();
        var applyOutputFormatOption = CreateOutputFormatOption();
        var applyManagementUrlOption = CreateManagementUrlOption();
        var applyUsernameOption = CreateUsernameOption();
        var applyPasswordOption = CreatePasswordOption();
        var applyVirtualHostsOption = CreateVirtualHostsOption();
        var applyDryRunOption = CreateDryRunOption();
        var applyVerboseOption = CreateVerboseOption();
        applyCommand.Options.Add(applyFileOption);
        applyCommand.Options.Add(applyOutputFormatOption);
        applyCommand.Options.Add(applyManagementUrlOption);
        applyCommand.Options.Add(applyUsernameOption);
        applyCommand.Options.Add(applyPasswordOption);
        applyCommand.Options.Add(applyVirtualHostsOption);
        applyCommand.Options.Add(applyDryRunOption);
        applyCommand.Options.Add(migrateOption);
        applyCommand.Options.Add(applyVerboseOption);
        applyCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var brokerOptions = new BrokerOptionsInput(
                parseResult.GetValue(applyManagementUrlOption),
                parseResult.GetValue(applyUsernameOption),
                parseResult.GetValue(applyPasswordOption),
                parseResult.GetValue(applyVirtualHostsOption));
            var exitCode = await handler.ApplyAsync(
                parseResult.GetRequiredValue(applyFileOption),
                brokerOptions,
                parseResult.GetValue(applyOutputFormatOption),
                parseResult.GetValue(applyDryRunOption),
                parseResult.GetValue(migrateOption),
                parseResult.GetValue(applyVerboseOption),
                cancellationToken);
            return exitCode;
        });

        var destroyCommand = new Command("destroy", DestroyDescription);
        var destroyFileOption = CreateRequiredFileOption();
        var destroyOutputFormatOption = CreateOutputFormatOption();
        var destroyManagementUrlOption = CreateManagementUrlOption();
        var destroyUsernameOption = CreateUsernameOption();
        var destroyPasswordOption = CreatePasswordOption();
        var destroyVirtualHostsOption = CreateVirtualHostsOption();
        var destroyDryRunOption = CreateDryRunOption();
        var destroyVerboseOption = CreateVerboseOption();
        destroyCommand.Options.Add(destroyFileOption);
        destroyCommand.Options.Add(destroyOutputFormatOption);
        destroyCommand.Options.Add(destroyManagementUrlOption);
        destroyCommand.Options.Add(destroyUsernameOption);
        destroyCommand.Options.Add(destroyPasswordOption);
        destroyCommand.Options.Add(destroyVirtualHostsOption);
        destroyCommand.Options.Add(destroyDryRunOption);
        destroyCommand.Options.Add(destroyVerboseOption);
        destroyCommand.Options.Add(allowDestructiveOption);
        destroyCommand.Options.Add(autoApproveOption);
        destroyCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var brokerOptions = new BrokerOptionsInput(
                parseResult.GetValue(destroyManagementUrlOption),
                parseResult.GetValue(destroyUsernameOption),
                parseResult.GetValue(destroyPasswordOption),
                parseResult.GetValue(destroyVirtualHostsOption));
            var exitCode = await handler.DestroyAsync(
                parseResult.GetRequiredValue(destroyFileOption),
                brokerOptions,
                parseResult.GetValue(destroyOutputFormatOption),
                parseResult.GetValue(destroyDryRunOption),
                parseResult.GetValue(destroyVerboseOption),
                parseResult.GetValue(allowDestructiveOption),
                parseResult.GetValue(autoApproveOption),
                true,
                cancellationToken);
            return exitCode;
        });

        var purgeCommand = new Command("purge", PurgeDescription);
        var purgeFileOption = CreateRequiredFileOption();
        var purgeOutputFormatOption = CreateOutputFormatOption();
        var purgeManagementUrlOption = CreateManagementUrlOption();
        var purgeUsernameOption = CreateUsernameOption();
        var purgePasswordOption = CreatePasswordOption();
        var purgeVirtualHostsOption = CreateVirtualHostsOption();
        var purgeDryRunOption = CreateDryRunOption();
        var purgeVerboseOption = CreateVerboseOption();
        purgeCommand.Options.Add(purgeFileOption);
        purgeCommand.Options.Add(purgeOutputFormatOption);
        purgeCommand.Options.Add(purgeManagementUrlOption);
        purgeCommand.Options.Add(purgeUsernameOption);
        purgeCommand.Options.Add(purgePasswordOption);
        purgeCommand.Options.Add(purgeVirtualHostsOption);
        purgeCommand.Options.Add(purgeDryRunOption);
        purgeCommand.Options.Add(purgeVerboseOption);
        purgeCommand.Options.Add(allowDestructiveOption);
        purgeCommand.Options.Add(autoApproveOption);
        purgeCommand.Options.Add(debugOnlyOption);
        purgeCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var brokerOptions = new BrokerOptionsInput(
                parseResult.GetValue(purgeManagementUrlOption),
                parseResult.GetValue(purgeUsernameOption),
                parseResult.GetValue(purgePasswordOption),
                parseResult.GetValue(purgeVirtualHostsOption));
            var exitCode = await handler.PurgeAsync(
                parseResult.GetRequiredValue(purgeFileOption),
                brokerOptions,
                parseResult.GetValue(purgeOutputFormatOption),
                parseResult.GetValue(purgeDryRunOption),
                parseResult.GetValue(purgeVerboseOption),
                parseResult.GetValue(allowDestructiveOption),
                parseResult.GetValue(autoApproveOption),
                parseResult.GetValue(debugOnlyOption),
                cancellationToken);
            return exitCode;
        });

        var exportCommand = new Command("export", ExportDescription);
        var exportFileOption = CreateOptionalFileOption();
        var exportOutputFormatOption = CreateOutputFormatOption();
        var exportManagementUrlOption = CreateManagementUrlOption();
        var exportUsernameOption = CreateUsernameOption();
        var exportPasswordOption = CreatePasswordOption();
        var exportVirtualHostsOption = CreateVirtualHostsOption();
        var exportVerboseOption = CreateVerboseOption();
        exportCommand.Options.Add(exportFileOption);
        exportCommand.Options.Add(exportOutputFormatOption);
        exportCommand.Options.Add(exportManagementUrlOption);
        exportCommand.Options.Add(exportUsernameOption);
        exportCommand.Options.Add(exportPasswordOption);
        exportCommand.Options.Add(exportVirtualHostsOption);
        exportCommand.Options.Add(exportOutputPathOption);
        exportCommand.Options.Add(includeBrokerOption);
        exportCommand.Options.Add(exportVerboseOption);
        exportCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var brokerOptions = new BrokerOptionsInput(
                parseResult.GetValue(exportManagementUrlOption),
                parseResult.GetValue(exportUsernameOption),
                parseResult.GetValue(exportPasswordOption),
                parseResult.GetValue(exportVirtualHostsOption));
            var exitCode = await handler.ExportAsync(
                parseResult.GetValue(exportFileOption),
                brokerOptions,
                parseResult.GetRequiredValue(exportOutputPathOption),
                parseResult.GetValue(includeBrokerOption),
                parseResult.GetValue(exportOutputFormatOption),
                parseResult.GetValue(exportVerboseOption),
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
