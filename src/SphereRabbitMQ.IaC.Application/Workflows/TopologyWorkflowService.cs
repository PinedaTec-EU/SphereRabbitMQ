using SphereRabbitMQ.IaC.Application.Apply;
using SphereRabbitMQ.IaC.Application.Apply.Interfaces;
using SphereRabbitMQ.IaC.Application.Broker.Interfaces;
using SphereRabbitMQ.IaC.Application.Export.Interfaces;
using SphereRabbitMQ.IaC.Application.Models;
using SphereRabbitMQ.IaC.Application.Normalization.Interfaces;
using SphereRabbitMQ.IaC.Application.Parsing.Interfaces;
using SphereRabbitMQ.IaC.Application.Planning.Interfaces;
using SphereRabbitMQ.IaC.Application.Validation.Interfaces;
using SphereRabbitMQ.IaC.Application.Workflows.Interfaces;
using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Application.Workflows;

/// <summary>
/// Default orchestration service for CLI and automation workflows.
/// </summary>
public sealed class TopologyWorkflowService : ITopologyWorkflowService
{
    private readonly ITopologyParser _topologyParser;
    private readonly ITopologyNormalizer _topologyNormalizer;
    private readonly ITopologyValidator _topologyValidator;
    private readonly IBrokerTopologyReader _brokerTopologyReader;
    private readonly ITopologyPlanner _topologyPlanner;
    private readonly ITopologyDestroyPlanner _topologyDestroyPlanner;
    private readonly ITopologyApplier _topologyApplier;
    private readonly ITopologyExporter _topologyExporter;

    public TopologyWorkflowService(
        ITopologyParser topologyParser,
        ITopologyNormalizer topologyNormalizer,
        ITopologyValidator topologyValidator,
        IBrokerTopologyReader brokerTopologyReader,
        ITopologyPlanner topologyPlanner,
        ITopologyDestroyPlanner topologyDestroyPlanner,
        ITopologyApplier topologyApplier,
        ITopologyExporter topologyExporter)
    {
        ArgumentNullException.ThrowIfNull(topologyParser);
        ArgumentNullException.ThrowIfNull(topologyNormalizer);
        ArgumentNullException.ThrowIfNull(topologyValidator);
        ArgumentNullException.ThrowIfNull(brokerTopologyReader);
        ArgumentNullException.ThrowIfNull(topologyPlanner);
        ArgumentNullException.ThrowIfNull(topologyDestroyPlanner);
        ArgumentNullException.ThrowIfNull(topologyApplier);
        ArgumentNullException.ThrowIfNull(topologyExporter);

        _topologyParser = topologyParser;
        _topologyNormalizer = topologyNormalizer;
        _topologyValidator = topologyValidator;
        _brokerTopologyReader = brokerTopologyReader;
        _topologyPlanner = topologyPlanner;
        _topologyDestroyPlanner = topologyDestroyPlanner;
        _topologyApplier = topologyApplier;
        _topologyExporter = topologyExporter;
    }

    public async ValueTask<(TopologyDefinition Definition, TopologyValidationResult Validation)> ValidateAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var definition = await ParseAndNormalizeAsync(stream, cancellationToken);
        var validation = await _topologyValidator.ValidateAsync(definition, cancellationToken);
        return (definition, validation);
    }

    public async ValueTask<(TopologyDefinition Definition, TopologyValidationResult Validation, TopologyPlan Plan)> PlanAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var (definition, validation) = await ValidateAsync(stream, cancellationToken);
        if (!validation.IsValid)
        {
            return (definition, validation, new TopologyPlan(Array.Empty<TopologyPlanOperation>()));
        }

        var actual = await _brokerTopologyReader.ReadAsync(cancellationToken);
        var plan = await _topologyPlanner.PlanAsync(definition, actual, cancellationToken);
        return (definition, validation, plan);
    }

    public async ValueTask<(TopologyDefinition Definition, TopologyValidationResult Validation, TopologyPlan Plan)> ApplyAsync(
        Stream stream,
        TopologyApplyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var (definition, validation, plan) = await PlanAsync(stream, cancellationToken);
        if (!validation.IsValid)
        {
            return (definition, validation, plan);
        }

        await _topologyApplier.ApplyAsync(definition, plan, options ?? TopologyApplyOptions.Safe, cancellationToken);
        return (definition, validation, plan);
    }

    public async ValueTask<(TopologyDefinition Definition, TopologyValidationResult Validation, TopologyPlan Plan)> PlanDestroyAsync(
        Stream stream,
        bool destroyVirtualHosts,
        CancellationToken cancellationToken = default)
    {
        var (definition, validation) = await ValidateAsync(stream, cancellationToken);
        if (!validation.IsValid)
        {
            return (definition, validation, new TopologyPlan(Array.Empty<TopologyPlanOperation>()));
        }

        var actual = await _brokerTopologyReader.ReadAsync(cancellationToken);
        var plan = await _topologyDestroyPlanner.PlanAsync(definition, actual, destroyVirtualHosts, cancellationToken);
        return (definition, validation, plan);
    }

    public async ValueTask<(TopologyDefinition Definition, TopologyValidationResult Validation, TopologyPlan Plan)> DestroyAsync(
        Stream stream,
        bool destroyVirtualHosts,
        CancellationToken cancellationToken = default)
    {
        var (definition, validation, plan) = await PlanDestroyAsync(stream, destroyVirtualHosts, cancellationToken);
        if (!validation.IsValid)
        {
            return (definition, validation, plan);
        }

        await _topologyApplier.ApplyAsync(definition, plan, TopologyApplyOptions.Safe, cancellationToken);
        return (definition, validation, plan);
    }


    public ValueTask<TopologyDocument> ExportAsync(CancellationToken cancellationToken = default)
        => _topologyExporter.ExportAsync(cancellationToken);

    private async ValueTask<TopologyDefinition> ParseAndNormalizeAsync(Stream stream, CancellationToken cancellationToken)
    {
        var document = await _topologyParser.ParseAsync(stream, cancellationToken);
        return await _topologyNormalizer.NormalizeAsync(document, cancellationToken);
    }
}
