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
    /// Optional AMQP host override used by runtime-assisted migration flows.
    /// </summary>
    public string? AmqpHostName { get; init; }

    /// <summary>
    /// Optional AMQP port used by runtime-assisted migration flows. Defaults to <c>5672</c>.
    /// </summary>
    public int AmqpPort { get; init; } = 5672;

    /// <summary>
    /// Optional AMQP virtual host override used by runtime-assisted migration flows. Defaults to <c>/</c>.
    /// </summary>
    public string AmqpVirtualHost { get; init; } = "/";

    /// <summary>
    /// Optional explicit list of virtual hosts to manage.
    /// </summary>
    public IReadOnlyCollection<string> ManagedVirtualHosts { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Determines whether broker-provided system artifacts such as <c>amq.*</c> exchanges are included.
    /// </summary>
    public bool IncludeSystemArtifacts { get; init; }
}
