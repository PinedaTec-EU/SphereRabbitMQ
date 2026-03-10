namespace SphereRabbitMQ.IaC.Tests.Integration;

[CollectionDefinition(CollectionName)]
public sealed class RabbitMqManagementCollection : ICollectionFixture<RabbitMqManagementIntegrationFixture>
{
    public const string CollectionName = "rabbitmq-management";
}
