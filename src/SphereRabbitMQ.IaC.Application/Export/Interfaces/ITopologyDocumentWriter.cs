using SphereRabbitMQ.IaC.Application.Models;

namespace SphereRabbitMQ.IaC.Application.Export.Interfaces;

/// <summary>
/// Serializes topology documents to an external textual representation.
/// </summary>
public interface ITopologyDocumentWriter
{
    /// <summary>
    /// Writes the supplied topology document to a string.
    /// </summary>
    ValueTask<string> WriteAsync(TopologyDocument document, CancellationToken cancellationToken = default);
}
