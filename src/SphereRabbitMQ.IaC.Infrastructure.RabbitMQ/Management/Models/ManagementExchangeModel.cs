using System.Text.Json.Serialization;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Models;

/// <summary>
/// RabbitMQ Management API exchange payload.
/// </summary>
public sealed record ManagementExchangeModel
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("durable")]
    public bool Durable { get; init; }

    [JsonPropertyName("auto_delete")]
    public bool AutoDelete { get; init; }

    [JsonPropertyName("internal")]
    public bool Internal { get; init; }

    [JsonPropertyName("arguments")]
    public Dictionary<string, object?> Arguments { get; init; } = new(StringComparer.Ordinal);
}
