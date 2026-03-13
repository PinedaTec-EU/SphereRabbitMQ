namespace SphereRabbitMQ.Tests.Integration.Support;

public sealed class RabbitMqRuntimeIntegrationSettingsTests
{
    [Fact]
    public void TryCreate_UsesDefaultAmqpPort_WhenManagementPortIsDefault()
    {
        using var scope = new RabbitMqEnvironmentVariableScope(
            ("SPHERE_RABBITMQ_MANAGEMENT_URL", "http://localhost:15672/api/"),
            ("SPHERE_RABBITMQ_USERNAME", "guest"),
            ("SPHERE_RABBITMQ_PASSWORD", "guest"),
            ("SPHERE_RABBITMQ_AMQP_HOST", null),
            ("SPHERE_RABBITMQ_AMQP_PORT", null),
            ("SPHERE_RABBITMQ_AMQP_VHOST", null));

        var created = RabbitMqRuntimeIntegrationSettings.TryCreate(out var settings);

        Assert.True(created);
        Assert.NotNull(settings);
        Assert.Equal("localhost", settings.HostName);
        Assert.Equal(5672, settings.Port);
        Assert.Equal("/", settings.VirtualHost);
    }

    [Fact]
    public void TryCreate_Throws_WhenManagementPortIsNonDefaultAndAmqpPortIsMissing()
    {
        using var scope = new RabbitMqEnvironmentVariableScope(
            ("SPHERE_RABBITMQ_MANAGEMENT_URL", "http://localhost:35672/api/"),
            ("SPHERE_RABBITMQ_USERNAME", "guest"),
            ("SPHERE_RABBITMQ_PASSWORD", "guest"),
            ("SPHERE_RABBITMQ_AMQP_HOST", null),
            ("SPHERE_RABBITMQ_AMQP_PORT", null),
            ("SPHERE_RABBITMQ_AMQP_VHOST", null));

        var exception = Assert.Throws<InvalidOperationException>(() => RabbitMqRuntimeIntegrationSettings.TryCreate(out _));

        Assert.Contains("SPHERE_RABBITMQ_AMQP_PORT", exception.Message);
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
