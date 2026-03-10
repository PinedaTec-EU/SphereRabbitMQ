using System.Text.Json.Serialization;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Models;

/// <summary>
/// RabbitMQ Management API virtual host payload.
/// </summary>
public sealed record ManagementVirtualHostModel
{
    /// <summary>
    /// Virtual host name.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}
