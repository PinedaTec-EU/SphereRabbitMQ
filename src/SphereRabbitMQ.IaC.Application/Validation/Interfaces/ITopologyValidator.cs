using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Application.Validation.Interfaces;

/// <summary>
/// Validates a normalized topology definition.
/// </summary>
public interface ITopologyValidator
{
    /// <summary>
    /// Validates the specified normalized topology.
    /// </summary>
    ValueTask<TopologyValidationResult> ValidateAsync(
        TopologyDefinition definition,
        CancellationToken cancellationToken = default);
}
