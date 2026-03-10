namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;

/// <summary>
/// Configuration for the RabbitMQ Management HTTP API.
/// </summary>
public sealed record RabbitMqManagementOptions
{
    /// <summary>
    /// Base URI of the RabbitMQ Management API, for example <c>http://localhost:15672/api/</c>.
    /// </summary>
    public required Uri BaseUri { get; init; }

    /// <summary>
    /// Username used for HTTP basic authentication.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// Password used for HTTP basic authentication.
    /// </summary>
    public required string Password { get; init; }

    /// <summary>
    /// Optional explicit list of virtual hosts to manage.
    /// </summary>
    public IReadOnlyCollection<string> ManagedVirtualHosts { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Determines whether broker-provided system artifacts such as <c>amq.*</c> exchanges are included.
    /// </summary>
    public bool IncludeSystemArtifacts { get; init; }
}
