using System.Text.Json.Serialization;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Models;

/// <summary>
/// RabbitMQ Management API publish result payload.
/// </summary>
public sealed record ManagementPublishedMessageResult
{
    [JsonPropertyName("routed")]
    public bool Routed { get; init; }
}
