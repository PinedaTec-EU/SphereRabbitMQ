using SphereRabbitMQ.IaC.Tests.Integration.EnvironmentFile;

namespace SphereRabbitMQ.IaC.Tests.Integration;

internal sealed record RabbitMqIntegrationTestSettings(
    Uri BaseUri,
    string Username,
    string Password)
{
    private const string BaseUriVariable = "SPHERE_RABBITMQ_MANAGEMENT_URL";
    private const string UsernameVariable = "SPHERE_RABBITMQ_USERNAME";
    private const string PasswordVariable = "SPHERE_RABBITMQ_PASSWORD";

    internal static bool TryCreate(out RabbitMqIntegrationTestSettings? settings)
    {
        IntegrationEnvironmentLoader.EnsureLoaded();

        var baseUri = Environment.GetEnvironmentVariable(BaseUriVariable);
        var username = Environment.GetEnvironmentVariable(UsernameVariable);
        var password = Environment.GetEnvironmentVariable(PasswordVariable);

        if (string.IsNullOrWhiteSpace(baseUri) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            settings = null;
            return false;
        }

        settings = new RabbitMqIntegrationTestSettings(new Uri(baseUri, UriKind.Absolute), username, password);
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
}
