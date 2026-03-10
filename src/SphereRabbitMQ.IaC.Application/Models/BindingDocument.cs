namespace SphereRabbitMQ.IaC.Application.Models;

/// <summary>
/// Source-neutral binding document.
/// </summary>
public sealed record BindingDocument
{
    public required string SourceExchange { get; init; }

    public required string Destination { get; init; }

    public string DestinationType { get; init; } = "queue";

    public string RoutingKey { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, object?> Arguments { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
