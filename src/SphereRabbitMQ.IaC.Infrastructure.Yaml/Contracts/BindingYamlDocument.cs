using System.Diagnostics.CodeAnalysis;

namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

/// <summary>
/// YAML contract for a binding definition.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record BindingYamlDocument
{
    public required string SourceExchange { get; init; }

    public required string Destination { get; init; }

    public string DestinationType { get; init; } = "queue";

    public string RoutingKey { get; init; } = string.Empty;

    public Dictionary<string, object?> Arguments { get; init; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);
}
