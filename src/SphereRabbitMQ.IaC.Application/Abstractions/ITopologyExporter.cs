using SphereRabbitMQ.IaC.Application.Models;

namespace SphereRabbitMQ.IaC.Application.Abstractions;

/// <summary>
/// Exports current broker topology into a serializable document model.
/// </summary>
public interface ITopologyExporter
{
    /// <summary>
    /// Exports the current broker topology.
    /// </summary>
    ValueTask<TopologyDocument> ExportAsync(CancellationToken cancellationToken = default);
}
