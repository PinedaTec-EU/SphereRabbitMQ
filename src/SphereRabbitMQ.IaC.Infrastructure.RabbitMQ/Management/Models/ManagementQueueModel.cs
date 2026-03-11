using System.Text.Json.Serialization;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Models;

/// <summary>
/// RabbitMQ Management API queue payload.
/// </summary>
public sealed record ManagementQueueModel
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("durable")]
    public bool Durable { get; init; }

    [JsonPropertyName("exclusive")]
    public bool Exclusive { get; init; }

    [JsonPropertyName("auto_delete")]
    public bool AutoDelete { get; init; }

    [JsonPropertyName("arguments")]
    public Dictionary<string, object?> Arguments { get; init; } = new(StringComparer.Ordinal);

    [JsonPropertyName("messages")]
    public int Messages { get; init; }
}
