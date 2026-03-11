using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SphereRabbitMQ.Abstractions.Configuration;

namespace SphereRabbitMQ.Infrastructure.RabbitMQ.Connection;

public sealed class RabbitMqConnectionProvider : IAsyncDisposable
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly ILogger<RabbitMqConnectionProvider> _logger;
    private readonly SphereRabbitMqOptions _options;
    private IConnection? _connection;

    public RabbitMqConnectionProvider(
        IOptions<SphereRabbitMqOptions> options,
        ILogger<RabbitMqConnectionProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async ValueTask<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _sync.WaitAsync(cancellationToken);

        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            _connection?.Dispose();

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                ClientProvidedName = _options.ClientProvidedName,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = false,
                ConsumerDispatchConcurrency = 1,
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            _logger.LogInformation("RabbitMQ connection established to {HostName}:{Port}/{VirtualHost}.", _options.HostName, _options.Port, _options.VirtualHost);
            return _connection;
        }
        finally
        {
            _sync.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _connection?.Dispose();
        _sync.Dispose();
        return ValueTask.CompletedTask;
    }
}
