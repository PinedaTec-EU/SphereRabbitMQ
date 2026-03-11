namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Apply;

public interface IQueueMigrationMessageMover
{
    ValueTask MoveAsync(
        string sourceQueueName,
        string destinationQueueName,
        CancellationToken cancellationToken = default);
}
