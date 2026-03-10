using SphereRabbitMQ.IaC.Application.Models;

namespace SphereRabbitMQ.IaC.Application.Parsing.Interfaces;

/// <summary>
/// Parses external topology documents into source-neutral application models.
/// </summary>
public interface ITopologyParser
{
    /// <summary>
    /// Parses a topology document from the provided input stream.
    /// </summary>
    ValueTask<TopologyDocument> ParseAsync(Stream stream, CancellationToken cancellationToken = default);
}
