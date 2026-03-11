using SphereRabbitMQ.IaC.Application.Apply;
using SphereRabbitMQ.IaC.Application.Models;
using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Application.Workflows.Interfaces;

/// <summary>
/// Orchestrates end-to-end topology workflows used by adapters such as the CLI.
/// </summary>
public interface ITopologyWorkflowService
{
    /// <summary>
    /// Parses, normalizes, and validates a topology file.
    /// </summary>
    ValueTask<(TopologyDefinition Definition, TopologyValidationResult Validation)> ValidateAsync(
        Stream stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses, normalizes, validates, and plans a topology file against the broker.
    /// </summary>
    ValueTask<(TopologyDefinition Definition, TopologyValidationResult Validation, TopologyPlan Plan)> PlanAsync(
        Stream stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a topology file to the broker after validation and planning.
    /// </summary>
    ValueTask<(TopologyDefinition Definition, TopologyValidationResult Validation, TopologyPlan Plan)> ApplyAsync(
        Stream stream,
        TopologyApplyOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses, normalizes, validates, and plans topology destruction against the broker.
    /// </summary>
    ValueTask<(TopologyDefinition Definition, TopologyValidationResult Validation, TopologyPlan Plan)> PlanDestroyAsync(
        Stream stream,
        bool destroyVirtualHosts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a destroy plan to the broker after validation.
    /// </summary>
    ValueTask<(TopologyDefinition Definition, TopologyValidationResult Validation, TopologyPlan Plan)> DestroyAsync(
        Stream stream,
        bool destroyVirtualHosts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses, normalizes, validates, and plans topology destruction against the broker.
    /// </summary>
    ValueTask<(TopologyDefinition Definition, TopologyValidationResult Validation, TopologyPlan Plan)> PlanDestroyAsync(
        Stream stream,
        bool destroyVirtualHosts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a destroy plan to the broker after validation.
    /// </summary>
    ValueTask<(TopologyDefinition Definition, TopologyValidationResult Validation, TopologyPlan Plan)> DestroyAsync(
        Stream stream,
        bool destroyVirtualHosts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports broker topology to the source-neutral application document model.
    /// </summary>
    ValueTask<TopologyDocument> ExportAsync(CancellationToken cancellationToken = default);
}
