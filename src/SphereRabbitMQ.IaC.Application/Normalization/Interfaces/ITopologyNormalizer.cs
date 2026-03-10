using SphereRabbitMQ.IaC.Application.Models;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Application.Normalization.Interfaces;

/// <summary>
/// Converts source-neutral topology documents into normalized domain definitions.
/// </summary>
public interface ITopologyNormalizer
{
    /// <summary>
    /// Normalizes a source-neutral topology document into the domain model.
    /// </summary>
    ValueTask<TopologyDefinition> NormalizeAsync(
        TopologyDocument document,
        CancellationToken cancellationToken = default);
}
