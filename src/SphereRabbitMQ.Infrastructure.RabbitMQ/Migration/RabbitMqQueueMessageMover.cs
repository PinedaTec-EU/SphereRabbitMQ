using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Connection;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Migration.Interfaces;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Publishing;

namespace SphereRabbitMQ.Infrastructure.RabbitMQ.Migration;

internal sealed class RabbitMqQueueMessageMover : IQueueMessageMover
{
    private const string DefaultExchangeName = "";

    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly ILogger<RabbitMqQueueMessageMover> _logger;
    private readonly RabbitMqChannelPool _publisherChannelPool;

    public RabbitMqQueueMessageMover(
        RabbitMqConnectionProvider connectionProvider,
        RabbitMqChannelPool publisherChannelPool,
        ILogger<RabbitMqQueueMessageMover> logger)
    {
        _connectionProvider = connectionProvider;
        _publisherChannelPool = publisherChannelPool;
        _logger = logger;
    }

    public async Task<QueueMessageMoveResult> MoveAsync(
        string sourceQueueName,
        string destinationQueueName,
        int? maxMessages = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceQueueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationQueueName);

        if (maxMessages is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMessages), "maxMessages must be greater than zero when specified.");
        }

        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
        await using var sourceChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await using var publisherLease = await _publisherChannelPool.RentAsync(cancellationToken);
        var publishChannel = publisherLease.Channel;

        await EnsureQueueExistsAsync(sourceChannel, sourceQueueName, cancellationToken);
        await EnsureQueueExistsAsync(sourceChannel, destinationQueueName, cancellationToken);

        var movedMessagesCount = 0;
        while (!maxMessages.HasValue || movedMessagesCount < maxMessages.Value)
        {
            var result = await sourceChannel.BasicGetAsync(sourceQueueName, autoAck: false, cancellationToken);
            if (result is null)
            {
                break;
            }

            try
            {
                await publishChannel.BasicPublishAsync(
                    DefaultExchangeName,
                    destinationQueueName,
                    mandatory: true,
                    CloneProperties(result.BasicProperties),
                    result.Body,
                    cancellationToken);
                await sourceChannel.BasicAckAsync(result.DeliveryTag, false, cancellationToken);
                movedMessagesCount++;
            }
            catch
            {
                await sourceChannel.BasicNackAsync(result.DeliveryTag, false, true, cancellationToken);
                throw;
            }
        }

        _logger.LogInformation(
            "Moved {MovedMessagesCount} messages from queue {SourceQueueName} to queue {DestinationQueueName}.",
            movedMessagesCount,
            sourceQueueName,
            destinationQueueName);

        return new QueueMessageMoveResult(movedMessagesCount);
    }

    private static async Task EnsureQueueExistsAsync(IChannel channel, string queueName, CancellationToken cancellationToken)
    {
        try
        {
            await channel.QueueDeclarePassiveAsync(queueName, cancellationToken);
        }
        catch (OperationInterruptedException exception)
        {
            throw new InvalidOperationException($"Queue '{queueName}' does not exist. SphereRabbitMQ does not create topology automatically.", exception);
        }
    }

    private static BasicProperties CloneProperties(IReadOnlyBasicProperties source)
        => new()
        {
            AppId = source.AppId,
            ClusterId = source.ClusterId,
            ContentEncoding = source.ContentEncoding,
            ContentType = source.ContentType,
            CorrelationId = source.CorrelationId,
            DeliveryMode = source.DeliveryMode,
            Expiration = source.Expiration,
            Headers = source.Headers is null ? null : new Dictionary<string, object?>(source.Headers, StringComparer.Ordinal),
            MessageId = source.MessageId,
            Persistent = source.Persistent,
            Priority = source.Priority,
            ReplyTo = source.ReplyTo,
            ReplyToAddress = source.ReplyToAddress,
            Timestamp = source.Timestamp,
            Type = source.Type,
            UserId = source.UserId,
        };
}
