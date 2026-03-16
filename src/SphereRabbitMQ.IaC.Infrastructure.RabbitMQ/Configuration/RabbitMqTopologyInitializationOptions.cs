namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;

/// <summary>
/// Controls startup-time topology application from a YAML file.
/// </summary>
public sealed record RabbitMqTopologyInitializationOptions
{
    /// <summary>
    /// Enables topology initialization during host startup.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Validates that runtime subscriber error-handling expectations match the YAML topology contract.
    /// This validation is read-only and can run even when startup topology application is disabled.
    /// </summary>
    public bool ValidateRuntimeContractAgainstYaml { get; set; }

    /// <summary>
    /// Relative or absolute path to the topology YAML file.
    /// </summary>
    public string? YamlFilePath { get; set; }

    /// <summary>
    /// Optional RabbitMQ Management API URL. When omitted, a default is derived from the AMQP host.
    /// </summary>
    public string? ManagementUrl { get; set; }

    /// <summary>
    /// Optional management API username override. Defaults to the runtime RabbitMQ username.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Optional management API password override. Defaults to the runtime RabbitMQ password.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Optional explicit virtual hosts to manage through the management API.
    /// </summary>
    public IReadOnlyCollection<string> ManagedVirtualHosts { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Allows runtime-assisted migrations for destructive or otherwise unsupported topology changes.
    /// </summary>
    public bool AllowMigrations { get; set; }

    /// <summary>
    /// Determines whether system artifacts such as amq.* exchanges are included when reading broker state.
    /// </summary>
    public bool IncludeSystemArtifacts { get; set; }
}
