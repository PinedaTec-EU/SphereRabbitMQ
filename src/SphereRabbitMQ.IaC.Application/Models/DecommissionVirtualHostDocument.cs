using System.Diagnostics.CodeAnalysis;

namespace SphereRabbitMQ.IaC.Application.Models;

/// <summary>
/// Source-neutral explicit cleanup scoped to a virtual host.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record DecommissionVirtualHostDocument
{
    public required string Name { get; init; }

    public IReadOnlyList<string> Exchanges { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Queues { get; init; } = Array.Empty<string>();

    public IReadOnlyList<BindingDocument> Bindings { get; init; } = Array.Empty<BindingDocument>();
}
