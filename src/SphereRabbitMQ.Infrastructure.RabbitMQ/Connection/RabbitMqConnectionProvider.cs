using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RabbitMQ.Client;

using SphereRabbitMQ.Abstractions.Configuration;

namespace SphereRabbitMQ.Infrastructure.RabbitMQ.Connection;

internal sealed class RabbitMqConnectionProvider : IAsyncDisposable
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

            var factory = CreateConnectionFactory();

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

    private ConnectionFactory CreateConnectionFactory()
    {
        var factory = new ConnectionFactory
        {
            ClientProvidedName = _options.ClientProvidedName,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = false,
            ConsumerDispatchConcurrency = 1,
        };

        if (!string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            factory.Uri = new Uri(_options.ConnectionString, UriKind.Absolute);
        }
        else
        {
            factory.HostName = _options.HostName;
            factory.Port = _options.Port;
            factory.UserName = _options.UserName;
            factory.Password = _options.Password;
            factory.VirtualHost = _options.VirtualHost;
        }

        factory.ClientProvidedName = _options.ClientProvidedName;
        return factory;
    }
}
