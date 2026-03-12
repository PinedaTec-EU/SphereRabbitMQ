using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Apply;

public sealed class RabbitMqRuntimeTopologyMigrationLock : ITopologyMigrationLock
{
    private const int LockAcquireDelayMilliseconds = 250;
    private const int LockAcquireMaxAttempts = 120;
    private const int ResourceLockedReplyCode = 405;
    private const string MigrationLockQueueName = "sprmq.migration.lock";

    private readonly RabbitMqManagementOptions _options;

    public RabbitMqRuntimeTopologyMigrationLock(RabbitMqManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public async ValueTask<IAsyncDisposable> AcquireAsync(string virtualHostName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualHostName);

        for (var attempt = 0; attempt < LockAcquireMaxAttempts; attempt++)
        {
            RabbitMqMigrationLockHandle? handle = null;

            try
            {
                var connectionFactory = CreateConnectionFactory(virtualHostName);
                var connection = await connectionFactory.CreateConnectionAsync(cancellationToken);
                var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
                handle = new RabbitMqMigrationLockHandle(connection, channel);

                await channel.QueueDeclareAsync(
                    MigrationLockQueueName,
                    false,
                    true,
                    true,
                    new Dictionary<string, object?>
                    {
                        ["x-queue-type"] = "classic",
                    },
                    false,
                    false,
                    cancellationToken);

                return handle;
            }
            catch (OperationInterruptedException exception) when (IsResourceLocked(exception))
            {
                if (handle is not null)
                {
                    await handle.DisposeAsync();
                }

                await Task.Delay(LockAcquireDelayMilliseconds, cancellationToken);
            }
        }

        throw new TimeoutException($"Unable to acquire the migration lock queue '{MigrationLockQueueName}' in virtual host '{virtualHostName}'.");
    }

    private ConnectionFactory CreateConnectionFactory(string virtualHostName)
        => new()
        {
            HostName = string.IsNullOrWhiteSpace(_options.AmqpHostName) ? _options.BaseUri.Host : _options.AmqpHostName,
            Port = _options.AmqpPort,
            VirtualHost = virtualHostName,
            UserName = _options.Username,
            Password = _options.Password,
            AutomaticRecoveryEnabled = false,
            TopologyRecoveryEnabled = false,
            ClientProvidedName = $"SphereRabbitMQ.IaC.Lock.{virtualHostName}",
        };

    private static bool IsResourceLocked(OperationInterruptedException exception)
        => exception.ShutdownReason?.ReplyCode == ResourceLockedReplyCode;

    private sealed class RabbitMqMigrationLockHandle : IAsyncDisposable
    {
        private readonly IConnection _connection;
        private readonly IChannel _channel;

        public RabbitMqMigrationLockHandle(IConnection connection, IChannel channel)
        {
            _connection = connection;
            _channel = channel;
        }

        public async ValueTask DisposeAsync()
        {
            await _channel.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
