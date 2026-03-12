namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Apply;

public interface ITopologyMigrationLock
{
    ValueTask<IAsyncDisposable> AcquireAsync(string virtualHostName, CancellationToken cancellationToken = default);
}
