namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Apply;

public interface IQueueMigrationMessageMover
{
    ValueTask MoveAsync(
        string virtualHostName,
        string sourceQueueName,
        string destinationQueueName,
        CancellationToken cancellationToken = default);
}
