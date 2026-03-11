using SphereRabbitMQ.IaC.Domain.Topology;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Models;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Interfaces;

/// <summary>
/// Abstraction over the RabbitMQ Management HTTP API.
/// </summary>
public interface IRabbitMqManagementApiClient
{
    /// <summary>
    /// Reads available virtual hosts.
    /// </summary>
    ValueTask<IReadOnlyList<ManagementVirtualHostModel>> GetVirtualHostsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads exchanges for the specified virtual host.
    /// </summary>
    ValueTask<IReadOnlyList<ManagementExchangeModel>> GetExchangesAsync(string virtualHostName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads queues for the specified virtual host.
    /// </summary>
    ValueTask<IReadOnlyList<ManagementQueueModel>> GetQueuesAsync(string virtualHostName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single queue for the specified virtual host.
    /// </summary>
    ValueTask<ManagementQueueModel?> GetQueueAsync(string virtualHostName, string queueName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads bindings for the specified virtual host.
    /// </summary>
    ValueTask<IReadOnlyList<ManagementBindingModel>> GetBindingsAsync(string virtualHostName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a virtual host when it does not exist.
    /// </summary>
    ValueTask CreateVirtualHostAsync(string virtualHostName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a virtual host when it exists.
    /// </summary>
    ValueTask DeleteVirtualHostAsync(string virtualHostName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates an exchange definition.
    /// </summary>
    ValueTask UpsertExchangeAsync(string virtualHostName, ExchangeDefinition exchange, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an exchange when it exists.
    /// </summary>
    ValueTask DeleteExchangeAsync(string virtualHostName, string exchangeName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a queue definition.
    /// </summary>
    ValueTask UpsertQueueAsync(string virtualHostName, QueueDefinition queue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a queue when it exists.
    /// </summary>
    ValueTask DeleteQueueAsync(string virtualHostName, string queueName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a binding.
    /// </summary>
    ValueTask CreateBindingAsync(string virtualHostName, BindingDefinition binding, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a binding when it exists.
    /// </summary>
    ValueTask DeleteBindingAsync(string virtualHostName, BindingDefinition binding, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recreates a binding after removing existing bindings with the same key.
    /// </summary>
    ValueTask RebindAsync(string virtualHostName, BindingDefinition binding, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads and removes a batch of messages from the specified queue.
    /// </summary>
    ValueTask<IReadOnlyList<ManagementRetrievedMessageModel>> GetMessagesAsync(
        string virtualHostName,
        string queueName,
        int count,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a message to an exchange.
    /// </summary>
    ValueTask PublishMessageAsync(
        string virtualHostName,
        string exchangeName,
        string routingKey,
        string? payload,
        string payloadEncoding,
        IReadOnlyDictionary<string, object?>? properties,
        CancellationToken cancellationToken = default);
}
