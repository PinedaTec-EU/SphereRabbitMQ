using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Connection;

namespace SphereRabbitMQ.Infrastructure.RabbitMQ.Publishing;

public sealed class RabbitMqChannelPool : IAsyncDisposable
{
    private readonly SemaphoreSlim _channelLock = new(1, 1);
    private readonly ILogger<RabbitMqChannelPool> _logger;
    private readonly SphereRabbitMqOptions _options;
    private readonly RabbitMqConnectionProvider _connectionProvider;
    private IChannel? _channel;

    public RabbitMqChannelPool(
        RabbitMqConnectionProvider connectionProvider,
        IOptions<SphereRabbitMqOptions> options,
        ILogger<RabbitMqChannelPool> logger)
    {
        _connectionProvider = connectionProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async ValueTask<RabbitMqChannelLease> RentAsync(CancellationToken cancellationToken)
    {
        await _channelLock.WaitAsync(cancellationToken);

        try
        {
            if (_channel is not { IsOpen: true })
            {
                if (_channel is not null)
                {
                    await _channel.DisposeAsync();
                }

                var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
                _channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(
                        publisherConfirmationsEnabled: _options.EnablePublisherConfirms,
                        publisherConfirmationTrackingEnabled: _options.EnablePublisherConfirms,
                        consumerDispatchConcurrency: 1),
                    cancellationToken);

                _logger.LogDebug("Created dedicated RabbitMQ publishing channel.");
            }

            return new RabbitMqChannelLease(_channel, ReleaseAsync);
        }
        catch
        {
            _channelLock.Release();
            throw;
        }
    }

    private ValueTask ReleaseAsync(IChannel channel)
    {
        if (!channel.IsOpen)
        {
            _channel = null;
        }

        _channelLock.Release();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _channelLock.Dispose();
    }
}
