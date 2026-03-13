using SphereRabbitMQ.Tests.Integration.EnvironmentFile;

namespace SphereRabbitMQ.Tests.Integration.Support;

internal sealed record RabbitMqRuntimeIntegrationSettings(
    string HostName,
    int Port,
    string UserName,
    string Password,
    string VirtualHost)
{
    private const int DefaultAmqpPort = 5672;
    private const int DefaultManagementPort = 15672;
    private const string BaseUriVariable = "SPHERE_RABBITMQ_MANAGEMENT_URL";
    private const string UsernameVariable = "SPHERE_RABBITMQ_USERNAME";
    private const string PasswordVariable = "SPHERE_RABBITMQ_PASSWORD";
    private const string AmqpHostNameVariable = "SPHERE_RABBITMQ_AMQP_HOST";
    private const string AmqpPortVariable = "SPHERE_RABBITMQ_AMQP_PORT";
    private const string AmqpVirtualHostVariable = "SPHERE_RABBITMQ_AMQP_VHOST";

    internal static bool TryCreate(out RabbitMqRuntimeIntegrationSettings? settings)
    {
        IntegrationEnvironmentLoader.EnsureLoaded();

        var managementUrl = Environment.GetEnvironmentVariable(BaseUriVariable);
        var username = Environment.GetEnvironmentVariable(UsernameVariable);
        var password = Environment.GetEnvironmentVariable(PasswordVariable);
        var amqpHostName = Environment.GetEnvironmentVariable(AmqpHostNameVariable);
        var amqpVirtualHost = Environment.GetEnvironmentVariable(AmqpVirtualHostVariable);
        var amqpPortRaw = Environment.GetEnvironmentVariable(AmqpPortVariable);

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            settings = null;
            return false;
        }

        if (string.IsNullOrWhiteSpace(amqpHostName) && string.IsNullOrWhiteSpace(managementUrl))
        {
            settings = null;
            return false;
        }

        var baseUri = string.IsNullOrWhiteSpace(managementUrl) ? null : new Uri(managementUrl, UriKind.Absolute);
        var hostName = string.IsNullOrWhiteSpace(amqpHostName) ? baseUri!.Host : amqpHostName;
        var port = ResolveAmqpPort(amqpPortRaw, baseUri);

        settings = new RabbitMqRuntimeIntegrationSettings(
            hostName,
            port,
            username,
            password,
            string.IsNullOrWhiteSpace(amqpVirtualHost) ? "/" : amqpVirtualHost);
        return true;
    }

    private static int ResolveAmqpPort(string? amqpPortRaw, Uri? managementUri)
    {
        if (int.TryParse(amqpPortRaw, out var configuredPort))
        {
            return configuredPort;
        }

        if (managementUri is null || managementUri.Port == DefaultManagementPort)
        {
            return DefaultAmqpPort;
        }

        throw new InvalidOperationException(
            $"Unable to infer AMQP port from {BaseUriVariable}='{managementUri}'. " +
            $"Set {AmqpPortVariable} explicitly when RabbitMQ management is exposed on a non-default port.");
    }
}
