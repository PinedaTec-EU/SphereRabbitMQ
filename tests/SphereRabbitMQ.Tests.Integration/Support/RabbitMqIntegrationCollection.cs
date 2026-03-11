namespace SphereRabbitMQ.Tests.Integration.Support;

[CollectionDefinition(CollectionName)]
public sealed class RabbitMqIntegrationCollection : ICollectionFixture<RabbitMqDockerFixture>
{
    public const string CollectionName = "RabbitMQ integration";
}
