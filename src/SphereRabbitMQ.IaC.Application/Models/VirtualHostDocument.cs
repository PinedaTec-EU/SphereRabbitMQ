using System.Diagnostics.CodeAnalysis;

namespace SphereRabbitMQ.IaC.Application.Models;

/// <summary>
/// Source-neutral virtual host document.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record VirtualHostDocument
{
    public required string Name { get; init; }

    public IReadOnlyList<ExchangeDocument> Exchanges { get; init; } = Array.Empty<ExchangeDocument>();

    public IReadOnlyList<QueueDocument> Queues { get; init; } = Array.Empty<QueueDocument>();

    public IReadOnlyList<BindingDocument> Bindings { get; init; } = Array.Empty<BindingDocument>();

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
