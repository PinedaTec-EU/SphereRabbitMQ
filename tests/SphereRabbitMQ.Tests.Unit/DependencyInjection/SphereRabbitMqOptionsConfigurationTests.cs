using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.DependencyInjection;

namespace SphereRabbitMQ.Tests.Unit.DependencyInjection;

[Collection(SharedStateTestCollection.Name)]
public sealed class SphereRabbitMqOptionsConfigurationTests
{
    [Fact]
    public void AddSphereRabbitMq_UsesEnvironmentVariables_WhenNoExplicitConfigurationIsProvided()
    {
        using var scope = new RabbitMqEnvironmentVariableScope(
            ("SPHERE_RABBITMQ_CONNECTION_STRING", null),
            ("SPHERE_RABBITMQ_MANAGEMENT_URL", "http://rabbit-admin.internal:15672/api/"),
            ("SPHERE_RABBITMQ_AMQP_HOST", null),
            ("SPHERE_RABBITMQ_AMQP_PORT", "5679"),
            ("SPHERE_RABBITMQ_AMQP_VHOST", "orders"),
            ("SPHERE_RABBITMQ_USERNAME", "app-user"),
            ("SPHERE_RABBITMQ_PASSWORD", "app-password"));

        var services = new ServiceCollection();
        services.AddSphereRabbitMq();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SphereRabbitMqOptions>>().Value;

        Assert.Equal("rabbit-admin.internal", options.HostName);
        Assert.Equal(5679, options.Port);
        Assert.Equal("orders", options.VirtualHost);
        Assert.Equal("app-user", options.UserName);
        Assert.Equal("app-password", options.Password);
        Assert.Null(options.ConnectionString);
    }

    [Fact]
    public void AddSphereRabbitMq_UsesEnvironmentConnectionString_WhenPresent()
    {
        const string connectionString = "amqp://env-user:env-password@rabbitmq.internal:5680/env-vhost";

        using var scope = new RabbitMqEnvironmentVariableScope(
            ("SPHERE_RABBITMQ_CONNECTION_STRING", connectionString),
            ("SPHERE_RABBITMQ_MANAGEMENT_URL", null),
            ("SPHERE_RABBITMQ_AMQP_HOST", "ignored-host"),
            ("SPHERE_RABBITMQ_AMQP_PORT", "5672"),
            ("SPHERE_RABBITMQ_AMQP_VHOST", "ignored-vhost"),
            ("SPHERE_RABBITMQ_USERNAME", "ignored-user"),
            ("SPHERE_RABBITMQ_PASSWORD", "ignored-password"));

        var services = new ServiceCollection();
        services.AddSphereRabbitMq();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SphereRabbitMqOptions>>().Value;

        Assert.Equal(connectionString, options.ConnectionString);
        Assert.Equal("rabbitmq.internal", options.HostName);
        Assert.Equal(5680, options.Port);
        Assert.Equal("env-vhost", options.VirtualHost);
        Assert.Equal("env-user", options.UserName);
        Assert.Equal("env-password", options.Password);
    }

    [Fact]
    public void AddSphereRabbitMq_AllowsExplicitConfigurationToOverrideEnvironmentVariables()
    {
        using var scope = new RabbitMqEnvironmentVariableScope(
            ("SPHERE_RABBITMQ_CONNECTION_STRING", null),
            ("SPHERE_RABBITMQ_MANAGEMENT_URL", null),
            ("SPHERE_RABBITMQ_AMQP_HOST", "env-host"),
            ("SPHERE_RABBITMQ_AMQP_PORT", "5679"),
            ("SPHERE_RABBITMQ_AMQP_VHOST", "env-vhost"),
            ("SPHERE_RABBITMQ_USERNAME", "env-user"),
            ("SPHERE_RABBITMQ_PASSWORD", "env-password"));

        var services = new ServiceCollection();
        services.AddSphereRabbitMq(options =>
        {
            options.SetConnectionString("amqp://explicit-user:explicit-password@explicit-host:5690/explicit-vhost");
            options.ValidateTopologyOnStartup = true;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SphereRabbitMqOptions>>().Value;

        Assert.Equal("amqp://explicit-user:explicit-password@explicit-host:5690/explicit-vhost", options.ConnectionString);
        Assert.Equal("explicit-host", options.HostName);
        Assert.Equal(5690, options.Port);
        Assert.Equal("explicit-vhost", options.VirtualHost);
        Assert.Equal("explicit-user", options.UserName);
        Assert.Equal("explicit-password", options.Password);
        Assert.True(options.ValidateTopologyOnStartup);
    }

    private sealed class RabbitMqEnvironmentVariableScope : IDisposable
    {
        private readonly IReadOnlyDictionary<string, string?> _previousValues;

        public RabbitMqEnvironmentVariableScope(params (string Name, string? Value)[] variables)
        {
            _previousValues = variables.ToDictionary(
                variable => variable.Name,
                variable => Environment.GetEnvironmentVariable(variable.Name));

            foreach (var variable in variables)
            {
                Environment.SetEnvironmentVariable(variable.Name, variable.Value);
            }
        }

        public void Dispose()
        {
            foreach (var previousValue in _previousValues)
            {
                Environment.SetEnvironmentVariable(previousValue.Key, previousValue.Value);
            }
        }
    }
}
