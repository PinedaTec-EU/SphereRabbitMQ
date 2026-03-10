using System.Text.Json.Serialization;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Models;

/// <summary>
/// RabbitMQ Management API binding payload.
/// </summary>
public sealed record ManagementBindingModel
{
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("destination")]
    public required string Destination { get; init; }

    [JsonPropertyName("destination_type")]
    public required string DestinationType { get; init; }

    [JsonPropertyName("routing_key")]
    public required string RoutingKey { get; init; }

    [JsonPropertyName("properties_key")]
    public required string PropertiesKey { get; init; }

    [JsonPropertyName("arguments")]
    public Dictionary<string, object?> Arguments { get; init; } = new(StringComparer.Ordinal);
}
