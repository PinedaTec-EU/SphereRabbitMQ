using SphereRabbitMQ.IaC.Tests.Integration.EnvironmentFile;

namespace SphereRabbitMQ.IaC.Tests.Integration;

public sealed class RabbitMqConfiguredFactAttribute : FactAttribute
{
    private const string BaseUriVariable = "SPHERE_RABBITMQ_MANAGEMENT_URL";
    private const string UsernameVariable = "SPHERE_RABBITMQ_USERNAME";
    private const string PasswordVariable = "SPHERE_RABBITMQ_PASSWORD";

    public RabbitMqConfiguredFactAttribute()
    {
        IntegrationEnvironmentLoader.EnsureLoaded();

        if (IsConfigured())
        {
            return;
        }

        Skip = $"Integration test requires {BaseUriVariable}, {UsernameVariable}, and {PasswordVariable}.";
    }

    private static bool IsConfigured()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(BaseUriVariable)) &&
           !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(UsernameVariable)) &&
           !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PasswordVariable));
}
