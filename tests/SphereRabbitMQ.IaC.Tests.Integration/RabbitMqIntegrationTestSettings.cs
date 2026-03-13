using SphereRabbitMQ.IaC.Tests.Integration.EnvironmentFile;

namespace SphereRabbitMQ.IaC.Tests.Integration;

internal sealed record RabbitMqIntegrationTestSettings(
    Uri BaseUri,
    string Username,
    string Password,
    string? AmqpHostName,
    int AmqpPort,
    string AmqpVirtualHost)
{
    private const int DefaultAmqpPort = 5672;
    private const int DefaultManagementPort = 15672;
    private const string BaseUriVariable = "SPHERE_RABBITMQ_MANAGEMENT_URL";
    private const string UsernameVariable = "SPHERE_RABBITMQ_USERNAME";
    private const string PasswordVariable = "SPHERE_RABBITMQ_PASSWORD";
    private const string AmqpHostNameVariable = "SPHERE_RABBITMQ_AMQP_HOST";
    private const string AmqpPortVariable = "SPHERE_RABBITMQ_AMQP_PORT";
    private const string AmqpVirtualHostVariable = "SPHERE_RABBITMQ_AMQP_VHOST";

    internal static bool TryCreate(out RabbitMqIntegrationTestSettings? settings)
    {
        IntegrationEnvironmentLoader.EnsureLoaded();

        var baseUri = Environment.GetEnvironmentVariable(BaseUriVariable);
        var username = Environment.GetEnvironmentVariable(UsernameVariable);
        var password = Environment.GetEnvironmentVariable(PasswordVariable);
        var amqpHostName = Environment.GetEnvironmentVariable(AmqpHostNameVariable);
        var amqpVirtualHost = Environment.GetEnvironmentVariable(AmqpVirtualHostVariable);
        var amqpPortRaw = Environment.GetEnvironmentVariable(AmqpPortVariable);

        if (string.IsNullOrWhiteSpace(baseUri) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            settings = null;
            return false;
        }

        settings = new RabbitMqIntegrationTestSettings(
            new Uri(baseUri, UriKind.Absolute),
            username,
            password,
            string.IsNullOrWhiteSpace(amqpHostName) ? null : amqpHostName,
            ResolveAmqpPort(amqpPortRaw, new Uri(baseUri, UriKind.Absolute)),
            string.IsNullOrWhiteSpace(amqpVirtualHost) ? "/" : amqpVirtualHost);
        return true;
    }

    internal static RabbitMqIntegrationTestSettings Create()
    {
        if (TryCreate(out var settings) && settings is not null)
        {
            return settings;
        }

        throw new InvalidOperationException("RabbitMQ integration test settings are not configured.");
    }

    private static int ResolveAmqpPort(string? amqpPortRaw, Uri managementUri)
    {
        if (int.TryParse(amqpPortRaw, out var configuredPort))
        {
            return configuredPort;
        }

        if (managementUri.Port == DefaultManagementPort)
        {
            return DefaultAmqpPort;
        }

        throw new InvalidOperationException(
            $"Unable to infer AMQP port from {BaseUriVariable}='{managementUri}'. " +
            $"Set {AmqpPortVariable} explicitly when RabbitMQ management is exposed on a non-default port.");
    }
}
