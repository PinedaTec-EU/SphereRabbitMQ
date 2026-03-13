using System.Diagnostics.CodeAnalysis;

namespace SphereRabbitMQ.IaC.Application.Models;

/// <summary>
/// Source-neutral cleanup document for explicit resource removal.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record DecommissionDocument
{
    public IReadOnlyList<DecommissionVirtualHostDocument> VirtualHosts { get; init; } = Array.Empty<DecommissionVirtualHostDocument>();
}
