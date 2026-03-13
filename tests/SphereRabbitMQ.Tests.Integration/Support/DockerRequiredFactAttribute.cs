namespace SphereRabbitMQ.Tests.Integration.Support;

public sealed class DockerRequiredFactAttribute : FactAttribute
{
    public DockerRequiredFactAttribute()
    {
        if (RabbitMqDockerAvailability.IsDockerAvailable())
        {
            return;
        }

        Skip = RabbitMqDockerAvailability.DockerRequiredMessage;
    }
}
