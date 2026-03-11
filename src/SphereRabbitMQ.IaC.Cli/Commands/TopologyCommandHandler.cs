using SphereRabbitMQ.IaC.Application.Normalization.Interfaces;
using SphereRabbitMQ.IaC.Application.Export.Interfaces;
using SphereRabbitMQ.IaC.Application.Normalization;
using SphereRabbitMQ.IaC.Application.Parsing.Interfaces;
using SphereRabbitMQ.IaC.Application.Validation.Interfaces;
using SphereRabbitMQ.IaC.Cli.Commands.Interfaces;
using SphereRabbitMQ.IaC.Cli.Commands.Models;
using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime.Interfaces;
using System.Reflection;

namespace SphereRabbitMQ.IaC.Cli.Commands;

internal sealed class TopologyCommandHandler
{
    private const string DefaultManagementUrl = "http://localhost:15672/api/";
    private const string DefaultPassword = "guest";
    private const string DefaultUsername = "guest";
    private const string DestroyPermissionMessage = "Destroy requires '--allow-destructive' unless '--dry-run' is specified.";
    private const string ToolName = "SphereRabbitMQ";
    private const string ManagementUrlEnvironmentVariable = "SPHERE_RABBITMQ_MANAGEMENT_URL";
    private const string PasswordEnvironmentVariable = "SPHERE_RABBITMQ_PASSWORD";
    private const string VirtualHostsEnvironmentVariable = "SPHERE_RABBITMQ_VHOSTS";
    private const string JsonOutputPath = "-";
    private const string UsernameEnvironmentVariable = "SPHERE_RABBITMQ_USERNAME";

    private readonly ITopologyParser _topologyParser;
    private readonly ITopologyNormalizer _topologyNormalizer;
    private readonly ITopologyValidator _topologyValidator;
    private readonly IRabbitMqRuntimeServiceFactory _rabbitMqRuntimeServiceFactory;
    private readonly ITopologyDocumentWriter _topologyDocumentWriter;
    private readonly ICommandOutputWriter _commandOutputWriter;

    public TopologyCommandHandler(
        ITopologyParser topologyParser,
        ITopologyNormalizer topologyNormalizer,
        ITopologyValidator topologyValidator,
        IRabbitMqRuntimeServiceFactory rabbitMqRuntimeServiceFactory,
        ITopologyDocumentWriter topologyDocumentWriter,
        ICommandOutputWriter commandOutputWriter)
    {
        _topologyParser = topologyParser;
        _topologyNormalizer = topologyNormalizer;
        _topologyValidator = topologyValidator;
        _rabbitMqRuntimeServiceFactory = rabbitMqRuntimeServiceFactory;
        _topologyDocumentWriter = topologyDocumentWriter;
        _commandOutputWriter = commandOutputWriter;
    }

    public async Task<int> ValidateAsync(
        string filePath,
        TopologyOutputFormat outputFormat,
        bool verbose,
        CancellationToken cancellationToken)
    {
        WriteStartup(outputFormat);
        WriteVerbose(outputFormat, verbose, "Phase: parse topology file.");

        try
        {
            await using var stream = File.OpenRead(filePath);
            var document = await _topologyParser.ParseAsync(stream, cancellationToken);
            WriteVerbose(outputFormat, verbose, "Phase: normalize topology.");
            var definition = await _topologyNormalizer.NormalizeAsync(document, cancellationToken);
            WriteVerbose(outputFormat, verbose, "Phase: validate topology.");
            var validation = await _topologyValidator.ValidateAsync(definition, cancellationToken);
            var result = new ValidateCommandResult(validation);
            Write(outputFormat, result, CommandOutputRenderer.RenderValidation(result));
            return validation.IsValid ? CommandExitCodes.Success : CommandExitCodes.ValidationFailed;
        }
        catch (TopologyNormalizationException exception)
        {
            var result = new ValidateCommandResult(new Domain.Topology.TopologyValidationResult(exception.Issues));
            Write(outputFormat, result, CommandOutputRenderer.RenderValidation(result));
            return CommandExitCodes.ValidationFailed;
        }
        catch (Exception exception)
        {
            WriteError(outputFormat, "validate", exception);
            return CommandExitCodes.ExecutionFailed;
        }
    }

    public async Task<int> PlanAsync(
        string filePath,
        BrokerOptionsInput brokerOptionsInput,
        TopologyOutputFormat outputFormat,
        bool verbose,
        CancellationToken cancellationToken)
    {
        WriteStartup(outputFormat);

        try
        {
            await using var stream = File.OpenRead(filePath);
            WriteVerbose(outputFormat, verbose, "Phase: parse topology file.");
            var topologyDocument = await ParseTopologyDocumentAsync(stream, cancellationToken);
            WriteVerbose(outputFormat, verbose, "Phase: resolve broker settings.");
            var broker = ResolveBrokerOptions(brokerOptionsInput, topologyDocument.Broker, topologyDocument.VirtualHosts);
            var brokerValidation = ValidateBrokerVirtualHostAlignment(broker, topologyDocument.VirtualHosts);
            if (!brokerValidation.IsValid)
            {
                var invalidBrokerResult = new PlanCommandResult(broker, brokerValidation, CreateEmptyPlan());
                Write(outputFormat, invalidBrokerResult, CommandOutputRenderer.RenderPlan(invalidBrokerResult));
                return CommandExitCodes.ValidationFailed;
            }

            WriteConnection(outputFormat, broker);
            WriteVerbose(outputFormat, verbose, "Phase: read broker topology and build reconciliation plan.");
            stream.Position = 0;
            using var runtimeServices = CreateRuntimeServices(broker.Options);
            var (_, validation, plan) = await runtimeServices.TopologyWorkflowService.PlanAsync(stream, cancellationToken);
            var result = new PlanCommandResult(broker, validation, plan);
            Write(outputFormat, result, CommandOutputRenderer.RenderPlan(result));

            if (!validation.IsValid)
            {
                return CommandExitCodes.ValidationFailed;
            }

            return plan.CanApply && plan.DestructiveChanges.Count == 0
                ? CommandExitCodes.Success
                : CommandExitCodes.UnsupportedPlan;
        }
        catch (TopologyNormalizationException exception)
        {
            var result = new PlanCommandResult(
                CreateFailureBrokerResolutionResult(),
                new Domain.Topology.TopologyValidationResult(exception.Issues),
                new Domain.Planning.TopologyPlan(Array.Empty<Domain.Planning.TopologyPlanOperation>()));
            Write(outputFormat, result, CommandOutputRenderer.RenderPlan(result));
            return CommandExitCodes.ValidationFailed;
        }
        catch (Exception exception)
        {
            WriteError(outputFormat, "plan", exception);
            return CommandExitCodes.ExecutionFailed;
        }
    }

    public async Task<int> ApplyAsync(
        string filePath,
        BrokerOptionsInput brokerOptionsInput,
        TopologyOutputFormat outputFormat,
        bool dryRun,
        bool verbose,
        CancellationToken cancellationToken)
    {
        WriteStartup(outputFormat);

        try
        {
            await using var stream = File.OpenRead(filePath);
            WriteVerbose(outputFormat, verbose, "Phase: parse topology file.");
            var topologyDocument = await ParseTopologyDocumentAsync(stream, cancellationToken);
            WriteVerbose(outputFormat, verbose, "Phase: resolve broker settings.");
            var broker = ResolveBrokerOptions(brokerOptionsInput, topologyDocument.Broker, topologyDocument.VirtualHosts);
            var brokerValidation = ValidateBrokerVirtualHostAlignment(broker, topologyDocument.VirtualHosts);
            if (!brokerValidation.IsValid)
            {
                var invalidBrokerResult = new ApplyCommandResult(dryRun, broker, brokerValidation, CreateEmptyPlan());
                Write(outputFormat, invalidBrokerResult, CommandOutputRenderer.RenderApply(invalidBrokerResult));
                return CommandExitCodes.ValidationFailed;
            }

            WriteConnection(outputFormat, broker);
            WriteVerbose(outputFormat, verbose, dryRun
                ? "Phase: read broker topology and build dry-run apply plan."
                : "Phase: read broker topology, build plan, and apply safe operations.");
            stream.Position = 0;
            using var runtimeServices = CreateRuntimeServices(broker.Options);
            var result = await BuildApplyPreviewResultAsync(broker, dryRun, runtimeServices, stream, cancellationToken);

            Write(outputFormat, result, CommandOutputRenderer.RenderApply(result));

            if (!result.Validation.IsValid)
            {
                return CommandExitCodes.ValidationFailed;
            }

            if (!result.Plan.CanApply || result.Plan.DestructiveChanges.Count > 0)
            {
                WriteVerbose(outputFormat, verbose, "Blocking plan operations detected.");
                return CommandExitCodes.UnsupportedPlan;
            }

            if (dryRun)
            {
                return CommandExitCodes.Success;
            }

            stream.Position = 0;
            WriteVerbose(outputFormat, verbose, "Phase: executing safe apply operations.");
            await runtimeServices.TopologyWorkflowService.ApplyAsync(stream, cancellationToken);
            return CommandExitCodes.Success;
        }
        catch (TopologyNormalizationException exception)
        {
            var result = new ApplyCommandResult(
                dryRun,
                CreateFailureBrokerResolutionResult(),
                new Domain.Topology.TopologyValidationResult(exception.Issues),
                new Domain.Planning.TopologyPlan(Array.Empty<Domain.Planning.TopologyPlanOperation>()));
            Write(outputFormat, result, CommandOutputRenderer.RenderApply(result));
            return CommandExitCodes.ValidationFailed;
        }
        catch (Exception exception)
        {
            WriteError(outputFormat, dryRun ? "apply dry-run" : "apply", exception);
            return CommandExitCodes.ExecutionFailed;
        }
    }

    public async Task<int> ExportAsync(
        string? filePath,
        BrokerOptionsInput brokerOptionsInput,
        string outputPath,
        TopologyOutputFormat outputFormat,
        bool verbose,
        CancellationToken cancellationToken)
    {
        WriteStartup(outputFormat);

        try
        {
            WriteVerbose(outputFormat, verbose, "Phase: resolve broker settings.");
            var topologyDocument = await ParseTopologyDocumentOrDefaultAsync(filePath, cancellationToken);
            var broker = ResolveBrokerOptions(
                brokerOptionsInput,
                topologyDocument?.Broker,
                topologyDocument?.VirtualHosts ?? Array.Empty<Application.Models.VirtualHostDocument>());
            WriteConnection(outputFormat, broker);
            WriteVerbose(outputFormat, verbose, "Phase: export broker topology.");
            using var runtimeServices = CreateRuntimeServices(broker.Options);
            var document = await runtimeServices.TopologyWorkflowService.ExportAsync(cancellationToken);
            var content = await _topologyDocumentWriter.WriteAsync(document, cancellationToken);
            var result = new ExportCommandResult(broker, content, document);

            if (outputPath != JsonOutputPath)
            {
                await File.WriteAllTextAsync(outputPath, content, cancellationToken);
            }

            if (outputFormat == TopologyOutputFormat.Json)
            {
                _commandOutputWriter.WriteJson(result);
            }
            else if (outputPath == JsonOutputPath)
            {
                _commandOutputWriter.WriteText(CommandOutputRenderer.RenderExport(result));
                _commandOutputWriter.WriteText(content);
            }
            else
            {
                _commandOutputWriter.WriteText(CommandOutputRenderer.RenderExport(result));
                _commandOutputWriter.WriteText($"Exported topology to '{outputPath}'.");
            }

            return CommandExitCodes.Success;
        }
        catch (Exception exception)
        {
            WriteError(outputFormat, "export", exception);
            return CommandExitCodes.ExecutionFailed;
        }
    }

    public async Task<int> DestroyAsync(
        string filePath,
        BrokerOptionsInput brokerOptionsInput,
        TopologyOutputFormat outputFormat,
        bool dryRun,
        bool verbose,
        bool allowDestructive,
        bool destroyVirtualHost,
        CancellationToken cancellationToken)
    {
        WriteStartup(outputFormat);

        if (!dryRun && !allowDestructive)
        {
            WriteError(outputFormat, "destroy", new InvalidOperationException(DestroyPermissionMessage));
            return CommandExitCodes.DestructivePermissionRequired;
        }

        try
        {
            await using var stream = File.OpenRead(filePath);
            WriteVerbose(outputFormat, verbose, "Phase: parse topology file.");
            var topologyDocument = await ParseTopologyDocumentAsync(stream, cancellationToken);
            WriteVerbose(outputFormat, verbose, "Phase: resolve broker settings.");
            var broker = ResolveBrokerOptions(brokerOptionsInput, topologyDocument.Broker, topologyDocument.VirtualHosts);
            var brokerValidation = ValidateBrokerVirtualHostAlignment(broker, topologyDocument.VirtualHosts);
            if (!brokerValidation.IsValid)
            {
                var invalidBrokerResult = new DestroyCommandResult(dryRun, destroyVirtualHost, broker, brokerValidation, CreateEmptyPlan());
                Write(outputFormat, invalidBrokerResult, CommandOutputRenderer.RenderDestroy(invalidBrokerResult));
                return CommandExitCodes.ValidationFailed;
            }

            WriteConnection(outputFormat, broker);
            WriteVerbose(outputFormat, verbose, dryRun
                ? "Phase: read broker topology and build dry-run destroy plan."
                : "Phase: read broker topology, build destroy plan, and execute destructive operations.");
            stream.Position = 0;
            using var runtimeServices = CreateRuntimeServices(broker.Options);
            var result = dryRun
                ? await BuildDestroyDryRunResultAsync(broker, destroyVirtualHost, runtimeServices, stream, cancellationToken)
                : await BuildDestroyResultAsync(broker, destroyVirtualHost, runtimeServices, stream, cancellationToken);

            Write(outputFormat, result, CommandOutputRenderer.RenderDestroy(result));

            if (!result.Validation.IsValid)
            {
                return CommandExitCodes.ValidationFailed;
            }

            return result.Plan.CanApply
                ? CommandExitCodes.Success
                : CommandExitCodes.UnsupportedPlan;
        }
        catch (TopologyNormalizationException exception)
        {
            var result = new DestroyCommandResult(
                dryRun,
                destroyVirtualHost,
                CreateFailureBrokerResolutionResult(),
                new Domain.Topology.TopologyValidationResult(exception.Issues),
                new Domain.Planning.TopologyPlan(Array.Empty<Domain.Planning.TopologyPlanOperation>()));
            Write(outputFormat, result, CommandOutputRenderer.RenderDestroy(result));
            return CommandExitCodes.ValidationFailed;
        }
        catch (Exception exception)
        {
            WriteError(outputFormat, dryRun ? "destroy dry-run" : "destroy", exception);
            return CommandExitCodes.ExecutionFailed;
        }
    }

    private static async Task<ApplyCommandResult> BuildApplyPreviewResultAsync(
        BrokerResolutionResult broker,
        bool dryRun,
        global::SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime.RabbitMqRuntimeServices runtimeServices,
        Stream stream,
        CancellationToken cancellationToken)
    {
        var (_, validation, plan) = await runtimeServices.TopologyWorkflowService.PlanAsync(stream, cancellationToken);
        return new ApplyCommandResult(dryRun, broker, validation, plan);
    }

    private static async Task<DestroyCommandResult> BuildDestroyDryRunResultAsync(
        BrokerResolutionResult broker,
        bool destroyVirtualHost,
        global::SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime.RabbitMqRuntimeServices runtimeServices,
        Stream stream,
        CancellationToken cancellationToken)
    {
        var (_, validation, plan) = await runtimeServices.TopologyWorkflowService.PlanDestroyAsync(stream, destroyVirtualHost, cancellationToken);
        return new DestroyCommandResult(true, destroyVirtualHost, broker, validation, plan);
    }

    private static async Task<DestroyCommandResult> BuildDestroyResultAsync(
        BrokerResolutionResult broker,
        bool destroyVirtualHost,
        global::SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime.RabbitMqRuntimeServices runtimeServices,
        Stream stream,
        CancellationToken cancellationToken)
    {
        var (_, validation, plan) = await runtimeServices.TopologyWorkflowService.DestroyAsync(stream, destroyVirtualHost, cancellationToken);
        return new DestroyCommandResult(false, destroyVirtualHost, broker, validation, plan);
    }

    private global::SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime.RabbitMqRuntimeServices CreateRuntimeServices(BrokerOptions brokerOptions)
        => _rabbitMqRuntimeServiceFactory.Create(new RabbitMqManagementOptions
        {
            BaseUri = new Uri(brokerOptions.ManagementUrl, UriKind.Absolute),
            Username = brokerOptions.Username,
            Password = brokerOptions.Password,
            ManagedVirtualHosts = brokerOptions.VirtualHosts,
        });

    private async Task<Application.Models.TopologyDocument> ParseTopologyDocumentAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        stream.Position = 0;
        return await _topologyParser.ParseAsync(stream, cancellationToken);
    }

    private async Task<Application.Models.TopologyDocument?> ParseTopologyDocumentOrDefaultAsync(
        string? filePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(filePath);
        return await ParseTopologyDocumentAsync(stream, cancellationToken);
    }

    private static BrokerResolutionResult ResolveBrokerOptions(
        BrokerOptionsInput brokerOptionsInput,
        Application.Models.BrokerDocument? brokerDocument,
        IReadOnlyList<Application.Models.VirtualHostDocument> topologyVirtualHosts)
    {
        var managementUrl = ResolveValue(
            brokerOptionsInput.ManagementUrl,
            Environment.GetEnvironmentVariable(ManagementUrlEnvironmentVariable),
            brokerDocument?.ManagementUrl,
            DefaultManagementUrl);
        var username = ResolveValue(
            brokerOptionsInput.Username,
            Environment.GetEnvironmentVariable(UsernameEnvironmentVariable),
            brokerDocument?.Username,
            DefaultUsername);
        var password = ResolveValue(
            brokerOptionsInput.Password,
            Environment.GetEnvironmentVariable(PasswordEnvironmentVariable),
            brokerDocument?.Password,
            DefaultPassword);
        var virtualHosts = ResolveVirtualHosts(brokerOptionsInput.VirtualHosts, brokerDocument?.VirtualHosts, topologyVirtualHosts);
        var options = new BrokerOptions(
            managementUrl.Value,
            username.Value,
            password.Value,
            virtualHosts.Value);

        return new BrokerResolutionResult(
            options,
            managementUrl,
            username,
            password.Source,
            virtualHosts);
    }

    private static BrokerOptionValue<string> ResolveValue(
        string? commandLineValue,
        string? environmentValue,
        string? documentValue,
        string fallbackValue)
    {
        if (!string.IsNullOrWhiteSpace(commandLineValue))
        {
            return new BrokerOptionValue<string>(commandLineValue, BrokerOptionSource.CommandLine);
        }

        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return new BrokerOptionValue<string>(environmentValue, BrokerOptionSource.Environment);
        }

        if (!string.IsNullOrWhiteSpace(documentValue))
        {
            return new BrokerOptionValue<string>(documentValue, BrokerOptionSource.Yaml);
        }

        return new BrokerOptionValue<string>(fallbackValue, BrokerOptionSource.Default);
    }

    private static BrokerOptionValue<IReadOnlyList<string>> ResolveVirtualHosts(
        IReadOnlyList<string>? commandLineVirtualHosts,
        IReadOnlyList<string>? documentVirtualHosts,
        IReadOnlyList<Application.Models.VirtualHostDocument> topologyVirtualHosts)
    {
        if (commandLineVirtualHosts is { Count: > 0 })
        {
            return new BrokerOptionValue<IReadOnlyList<string>>(commandLineVirtualHosts, BrokerOptionSource.CommandLine);
        }

        var environmentVirtualHosts = Environment.GetEnvironmentVariable(VirtualHostsEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentVirtualHosts))
        {
            var values = environmentVirtualHosts
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new BrokerOptionValue<IReadOnlyList<string>>(values, BrokerOptionSource.Environment);
        }

        if (documentVirtualHosts is { Count: > 0 })
        {
            return new BrokerOptionValue<IReadOnlyList<string>>(documentVirtualHosts, BrokerOptionSource.Yaml);
        }

        return new BrokerOptionValue<IReadOnlyList<string>>(
            topologyVirtualHosts.Select(vhost => vhost.Name).ToArray(),
            BrokerOptionSource.Derived);
    }

    private static BrokerResolutionResult CreateFailureBrokerResolutionResult()
        => new(
            new BrokerOptions(DefaultManagementUrl, DefaultUsername, DefaultPassword, Array.Empty<string>()),
            new BrokerOptionValue<string>(DefaultManagementUrl, BrokerOptionSource.Default),
            new BrokerOptionValue<string>(DefaultUsername, BrokerOptionSource.Default),
            BrokerOptionSource.Default,
            new BrokerOptionValue<IReadOnlyList<string>>(Array.Empty<string>(), BrokerOptionSource.Default));

    private static TopologyPlan CreateEmptyPlan()
        => new(Array.Empty<TopologyPlanOperation>());

    private static TopologyValidationResult ValidateBrokerVirtualHostAlignment(
        BrokerResolutionResult broker,
        IReadOnlyList<Application.Models.VirtualHostDocument> topologyVirtualHosts)
    {
        var declaredVirtualHosts = topologyVirtualHosts
            .Select(virtualHost => virtualHost.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var resolvedVirtualHosts = broker.VirtualHosts.Value
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (resolvedVirtualHosts.Length == 0 || declaredVirtualHosts.SequenceEqual(resolvedVirtualHosts, StringComparer.Ordinal))
        {
            return TopologyValidationResult.Success;
        }

        return new TopologyValidationResult(
        [
            new TopologyIssue(
                "broker-virtual-host-mismatch",
                $"Broker virtualHosts [{string.Join(", ", resolvedVirtualHosts)}] do not match declared topology virtualHosts [{string.Join(", ", declaredVirtualHosts)}]. Align both sections or omit broker.virtualHosts to derive them from the topology.",
                "/broker/virtualHosts",
                TopologyIssueSeverity.Error),
        ]);
    }

    private static string GetToolVersion()
        => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

    private void WriteConnection(TopologyOutputFormat outputFormat, BrokerResolutionResult broker)
    {
        if (outputFormat == TopologyOutputFormat.Json)
        {
            return;
        }

        _commandOutputWriter.WriteText($"Connecting to... {broker.Options.ManagementUrl}");
    }

    private void WriteVerbose(TopologyOutputFormat outputFormat, bool verbose, string content)
    {
        if (!verbose || outputFormat == TopologyOutputFormat.Json)
        {
            return;
        }

        _commandOutputWriter.WriteText(content);
    }

    private void WriteError(TopologyOutputFormat outputFormat, string phase, Exception exception)
    {
        if (outputFormat == TopologyOutputFormat.Json)
        {
            _commandOutputWriter.WriteJson(new
            {
                phase,
                error = exception.Message,
            });
            return;
        }

        _commandOutputWriter.WriteText($"Error during {phase}: {exception.Message}");
    }

    private void WriteStartup(TopologyOutputFormat outputFormat)
    {
        if (outputFormat == TopologyOutputFormat.Json)
        {
            return;
        }

        _commandOutputWriter.WriteText($"{ToolName} v{GetToolVersion()}.");
    }

    private void Write<T>(TopologyOutputFormat outputFormat, T result, string text)
    {
        if (outputFormat == TopologyOutputFormat.Json)
        {
            _commandOutputWriter.WriteJson(result);
            return;
        }

        _commandOutputWriter.WriteText(text);
    }
}
