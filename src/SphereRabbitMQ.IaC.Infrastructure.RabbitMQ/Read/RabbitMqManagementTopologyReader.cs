using SphereRabbitMQ.IaC.Application.Broker.Interfaces;
using SphereRabbitMQ.IaC.Domain.Topology;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Interfaces;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Read;

/// <summary>
/// Reads RabbitMQ topology through the Management HTTP API.
/// </summary>
public sealed class RabbitMqManagementTopologyReader : IBrokerTopologyReader
{
    private readonly IRabbitMqManagementApiClient _apiClient;
    private readonly RabbitMqManagementOptions _options;

    /// <summary>
    /// Creates a topology reader backed by the management API.
    /// </summary>
    public RabbitMqManagementTopologyReader(
        IRabbitMqManagementApiClient apiClient,
        RabbitMqManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        ArgumentNullException.ThrowIfNull(options);

        _apiClient = apiClient;
        _options = options;
    }

    /// <inheritdoc />
    public async ValueTask<TopologyDefinition> ReadAsync(CancellationToken cancellationToken = default)
    {
        var virtualHostNames = await ReadManagedVirtualHostNamesAsync(cancellationToken);
        var existingVirtualHostNames = await ReadExistingVirtualHostNamesAsync(cancellationToken);
        var virtualHosts = new List<VirtualHostDefinition>();

        foreach (var virtualHostName in virtualHostNames.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!existingVirtualHostNames.Contains(virtualHostName))
            {
                continue;
            }

            var exchanges = await _apiClient.GetExchangesAsync(virtualHostName, cancellationToken);
            var queues = await _apiClient.GetQueuesAsync(virtualHostName, cancellationToken);
            var bindings = await _apiClient.GetBindingsAsync(virtualHostName, cancellationToken);

            virtualHosts.Add(new VirtualHostDefinition(
                virtualHostName,
                exchanges
                    .Where(exchange => ShouldIncludeExchange(exchange.Name))
                    .Select(exchange => new ExchangeDefinition(
                        exchange.Name,
                        ParseExchangeType(exchange.Type),
                        exchange.Durable,
                        exchange.AutoDelete,
                        exchange.Internal,
                        exchange.Arguments))
                    .OrderBy(exchange => exchange.Name, StringComparer.Ordinal)
                    .ToArray(),
                queues
                    .Select(queue => new QueueDefinition(
                        queue.Name,
                        ParseQueueType(queue.Arguments),
                        queue.Durable,
                        queue.Exclusive,
                        queue.AutoDelete,
                        queue.Arguments))
                    .OrderBy(queue => queue.Name, StringComparer.Ordinal)
                    .ToArray(),
                bindings
                    .Where(binding => ShouldIncludeBinding(binding.Source))
                    .Select(binding => new BindingDefinition(
                        binding.Source,
                        binding.Destination,
                        binding.DestinationType == "queue" ? BindingDestinationType.Queue : BindingDestinationType.Exchange,
                        binding.RoutingKey,
                        binding.Arguments))
                    .OrderBy(binding => binding.Key, StringComparer.Ordinal)
                    .ToArray()));
        }

        return new TopologyDefinition(virtualHosts.ToArray());
    }

    private async ValueTask<IReadOnlyList<string>> ReadManagedVirtualHostNamesAsync(CancellationToken cancellationToken)
    {
        if (_options.ManagedVirtualHosts.Count > 0)
        {
            return _options.ManagedVirtualHosts.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        }

        var virtualHosts = await _apiClient.GetVirtualHostsAsync(cancellationToken);
        return virtualHosts.Select(vhost => vhost.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private async ValueTask<HashSet<string>> ReadExistingVirtualHostNamesAsync(CancellationToken cancellationToken)
    {
        var virtualHosts = await _apiClient.GetVirtualHostsAsync(cancellationToken);
        return virtualHosts
            .Select(vhost => vhost.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    private bool ShouldIncludeExchange(string exchangeName)
        => _options.IncludeSystemArtifacts || (!string.IsNullOrWhiteSpace(exchangeName) && !exchangeName.StartsWith("amq.", StringComparison.Ordinal));

    private bool ShouldIncludeBinding(string sourceExchangeName)
        => _options.IncludeSystemArtifacts || (!string.IsNullOrWhiteSpace(sourceExchangeName) && !sourceExchangeName.StartsWith("amq.", StringComparison.Ordinal));

    private static ExchangeType ParseExchangeType(string exchangeType)
        => exchangeType.ToLowerInvariant() switch
        {
            "direct" => ExchangeType.Direct,
            "topic" => ExchangeType.Topic,
            "fanout" => ExchangeType.Fanout,
            "headers" => ExchangeType.Headers,
            _ => ExchangeType.Direct,
        };

    private static QueueType ParseQueueType(IReadOnlyDictionary<string, object?> arguments)
        => arguments.TryGetValue("x-queue-type", out var queueType) &&
           string.Equals(queueType?.ToString(), "quorum", StringComparison.OrdinalIgnoreCase)
            ? QueueType.Quorum
            : QueueType.Classic;
}
