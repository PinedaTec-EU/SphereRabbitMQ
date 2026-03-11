namespace SphereRabbitMQ.Infrastructure.RabbitMQ.Migration.Interfaces;

internal interface IQueueMessageMover
{
    Task<QueueMessageMoveResult> MoveAsync(
        string sourceQueueName,
        string destinationQueueName,
        int? maxMessages = null,
        CancellationToken cancellationToken = default);
}
