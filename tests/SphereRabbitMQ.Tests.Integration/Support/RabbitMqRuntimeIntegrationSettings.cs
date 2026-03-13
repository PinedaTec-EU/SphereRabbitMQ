using SphereRabbitMQ.Tests.Integration.EnvironmentFile;

namespace SphereRabbitMQ.Tests.Integration.Support;

internal sealed record RabbitMqRuntimeIntegrationSettings(
    string HostName,
    int Port,
    string UserName,
    string Password,
    string VirtualHost)
{
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
        var port = int.TryParse(amqpPortRaw, out var configuredPort)
            ? configuredPort
            : baseUri is null ? 5672 : DeriveAmqpPort(baseUri);

        settings = new RabbitMqRuntimeIntegrationSettings(
            hostName,
            port,
            username,
            password,
            string.IsNullOrWhiteSpace(amqpVirtualHost) ? "/" : amqpVirtualHost);
        return true;
    }

    private static int DeriveAmqpPort(Uri managementUri)
    {
        if (managementUri.Port == 15672)
        {
            return 5672;
        }

        var managementPort = managementUri.Port.ToString();
        if (managementPort.EndsWith("1672", StringComparison.Ordinal) && managementPort.Length > 4)
        {
            return int.Parse($"{managementPort[..^4]}5672");
        }

        return 5672;
    }
}
