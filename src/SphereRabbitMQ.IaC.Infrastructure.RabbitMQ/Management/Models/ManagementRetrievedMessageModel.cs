using System.Text.Json.Serialization;
using System.Text.Json;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Models;

/// <summary>
/// RabbitMQ Management API message payload returned by queue get operations.
/// </summary>
public sealed record ManagementRetrievedMessageModel
{
    [JsonPropertyName("payload")]
    public string? Payload { get; init; }

    [JsonPropertyName("payload_encoding")]
    public string PayloadEncoding { get; init; } = "string";

    [JsonPropertyName("routing_key")]
    public string RoutingKey { get; init; } = string.Empty;

    [JsonPropertyName("properties")]
    public JsonElement Properties { get; init; }
}
