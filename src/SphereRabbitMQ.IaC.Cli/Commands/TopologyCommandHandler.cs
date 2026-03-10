using SphereRabbitMQ.IaC.Application.Normalization.Interfaces;
using SphereRabbitMQ.IaC.Application.Export.Interfaces;
using SphereRabbitMQ.IaC.Application.Normalization;
using SphereRabbitMQ.IaC.Application.Parsing.Interfaces;
using SphereRabbitMQ.IaC.Application.Validation.Interfaces;
using SphereRabbitMQ.IaC.Cli.Commands.Interfaces;
using SphereRabbitMQ.IaC.Cli.Commands.Models;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime.Interfaces;

namespace SphereRabbitMQ.IaC.Cli.Commands;

internal sealed class TopologyCommandHandler
{
    private const string JsonOutputPath = "-";

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
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            var document = await _topologyParser.ParseAsync(stream, cancellationToken);
            var definition = await _topologyNormalizer.NormalizeAsync(document, cancellationToken);
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
            _commandOutputWriter.WriteText(exception.Message);
            return CommandExitCodes.ExecutionFailed;
        }
    }

    public async Task<int> PlanAsync(
        string filePath,
        BrokerOptions brokerOptions,
        TopologyOutputFormat outputFormat,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            using var runtimeServices = CreateRuntimeServices(brokerOptions);
            var (_, validation, plan) = await runtimeServices.TopologyWorkflowService.PlanAsync(stream, cancellationToken);
            var result = new PlanCommandResult(validation, plan);
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
            var result = new PlanCommandResult(new Domain.Topology.TopologyValidationResult(exception.Issues), new Domain.Planning.TopologyPlan(Array.Empty<Domain.Planning.TopologyPlanOperation>()));
            Write(outputFormat, result, CommandOutputRenderer.RenderPlan(result));
            return CommandExitCodes.ValidationFailed;
        }
        catch (Exception exception)
        {
            _commandOutputWriter.WriteText(exception.Message);
            return CommandExitCodes.ExecutionFailed;
        }
    }

    public async Task<int> ApplyAsync(
        string filePath,
        BrokerOptions brokerOptions,
        TopologyOutputFormat outputFormat,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            using var runtimeServices = CreateRuntimeServices(brokerOptions);
            var result = dryRun
                ? await BuildDryRunResultAsync(runtimeServices, stream, cancellationToken)
                : await BuildApplyResultAsync(runtimeServices, stream, cancellationToken);

            Write(outputFormat, result, CommandOutputRenderer.RenderApply(result));

            if (!result.Validation.IsValid)
            {
                return CommandExitCodes.ValidationFailed;
            }

            return result.Plan.CanApply && result.Plan.DestructiveChanges.Count == 0
                ? CommandExitCodes.Success
                : CommandExitCodes.UnsupportedPlan;
        }
        catch (TopologyNormalizationException exception)
        {
            var result = new ApplyCommandResult(
                dryRun,
                new Domain.Topology.TopologyValidationResult(exception.Issues),
                new Domain.Planning.TopologyPlan(Array.Empty<Domain.Planning.TopologyPlanOperation>()));
            Write(outputFormat, result, CommandOutputRenderer.RenderApply(result));
            return CommandExitCodes.ValidationFailed;
        }
        catch (Exception exception)
        {
            _commandOutputWriter.WriteText(exception.Message);
            return CommandExitCodes.ExecutionFailed;
        }
    }

    public async Task<int> ExportAsync(
        BrokerOptions brokerOptions,
        string outputPath,
        TopologyOutputFormat outputFormat,
        CancellationToken cancellationToken)
    {
        try
        {
            using var runtimeServices = CreateRuntimeServices(brokerOptions);
            var document = await runtimeServices.TopologyWorkflowService.ExportAsync(cancellationToken);
            var content = await _topologyDocumentWriter.WriteAsync(document, cancellationToken);
            var result = new ExportCommandResult(content, document);

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
                _commandOutputWriter.WriteText(content);
            }
            else
            {
                _commandOutputWriter.WriteText($"Exported topology to '{outputPath}'.");
            }

            return CommandExitCodes.Success;
        }
        catch (Exception exception)
        {
            _commandOutputWriter.WriteText(exception.Message);
            return CommandExitCodes.ExecutionFailed;
        }
    }

    private static async Task<ApplyCommandResult> BuildDryRunResultAsync(
        global::SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime.RabbitMqRuntimeServices runtimeServices,
        Stream stream,
        CancellationToken cancellationToken)
    {
        var (_, validation, plan) = await runtimeServices.TopologyWorkflowService.PlanAsync(stream, cancellationToken);
        return new ApplyCommandResult(true, validation, plan);
    }

    private static async Task<ApplyCommandResult> BuildApplyResultAsync(
        global::SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime.RabbitMqRuntimeServices runtimeServices,
        Stream stream,
        CancellationToken cancellationToken)
    {
        var (_, validation, plan) = await runtimeServices.TopologyWorkflowService.ApplyAsync(stream, cancellationToken);
        return new ApplyCommandResult(false, validation, plan);
    }

    private global::SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime.RabbitMqRuntimeServices CreateRuntimeServices(BrokerOptions brokerOptions)
        => _rabbitMqRuntimeServiceFactory.Create(new RabbitMqManagementOptions
        {
            BaseUri = new Uri(brokerOptions.ManagementUrl, UriKind.Absolute),
            Username = brokerOptions.Username,
            Password = brokerOptions.Password,
            ManagedVirtualHosts = brokerOptions.VirtualHosts,
        });

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
