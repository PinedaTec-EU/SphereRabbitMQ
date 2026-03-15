using Microsoft.Extensions.Options;
using SphereRabbitMQ.Abstractions.Configuration;

namespace SphereRabbitMQ.DependencyInjection;

internal sealed class EnvironmentVariableSphereRabbitMqOptionsConfigurator : IConfigureOptions<SphereRabbitMqOptions>
{
    private const string AmqpHostEnvironmentVariable = "SPHERE_RABBITMQ_AMQP_HOST";
    private const string AmqpPortEnvironmentVariable = "SPHERE_RABBITMQ_AMQP_PORT";
    private const string AmqpVirtualHostEnvironmentVariable = "SPHERE_RABBITMQ_AMQP_VHOST";
    private const string ConnectionStringEnvironmentVariable = "SPHERE_RABBITMQ_CONNECTION_STRING";
    private const string ManagementUrlEnvironmentVariable = "SPHERE_RABBITMQ_MANAGEMENT_URL";
    private const string PasswordEnvironmentVariable = "SPHERE_RABBITMQ_PASSWORD";
    private const string UsernameEnvironmentVariable = "SPHERE_RABBITMQ_USERNAME";

    public void Configure(SphereRabbitMqOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            options.SetConnectionString(connectionString);
            return;
        }

        var hostName = Environment.GetEnvironmentVariable(AmqpHostEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(hostName))
        {
            hostName = TryResolveHostNameFromManagementUrl();
        }

        if (!string.IsNullOrWhiteSpace(hostName))
        {
            options.HostName = hostName;
        }

        var portValue = Environment.GetEnvironmentVariable(AmqpPortEnvironmentVariable);
        if (int.TryParse(portValue, out var port))
        {
            options.Port = port;
        }

        var virtualHost = Environment.GetEnvironmentVariable(AmqpVirtualHostEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(virtualHost))
        {
            options.VirtualHost = virtualHost;
        }

        var userName = Environment.GetEnvironmentVariable(UsernameEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(userName))
        {
            options.UserName = userName;
        }

        var password = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(password))
        {
            options.Password = password;
        }
    }

    private static string? TryResolveHostNameFromManagementUrl()
    {
        var managementUrl = Environment.GetEnvironmentVariable(ManagementUrlEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(managementUrl))
        {
            return null;
        }

        return Uri.TryCreate(managementUrl, UriKind.Absolute, out var uri) ? uri.Host : null;
    }
}
