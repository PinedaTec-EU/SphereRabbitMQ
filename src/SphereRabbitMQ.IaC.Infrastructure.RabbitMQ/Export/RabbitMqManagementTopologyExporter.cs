using SphereRabbitMQ.IaC.Application.Broker.Interfaces;
using SphereRabbitMQ.IaC.Application.Export.Interfaces;
using SphereRabbitMQ.IaC.Application.Models;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Export;

/// <summary>
/// Exports broker topology into the source-neutral application document model.
/// </summary>
public sealed class RabbitMqManagementTopologyExporter : ITopologyExporter
{
    private readonly IBrokerTopologyReader _brokerTopologyReader;

    /// <summary>
    /// Creates a new topology exporter.
    /// </summary>
    public RabbitMqManagementTopologyExporter(IBrokerTopologyReader brokerTopologyReader)
    {
        ArgumentNullException.ThrowIfNull(brokerTopologyReader);
        _brokerTopologyReader = brokerTopologyReader;
    }

    /// <inheritdoc />
    public async ValueTask<TopologyDocument> ExportAsync(CancellationToken cancellationToken = default)
    {
        var topologyDefinition = await _brokerTopologyReader.ReadAsync(cancellationToken);

        return new TopologyDocument
        {
            Metadata = new Dictionary<string, string>(topologyDefinition.Metadata, StringComparer.Ordinal),
            VirtualHosts = topologyDefinition.VirtualHosts
                .Select(vhost => new VirtualHostDocument
                {
                    Name = vhost.Name,
                    Metadata = new Dictionary<string, string>(vhost.Metadata, StringComparer.Ordinal),
                    Exchanges = vhost.Exchanges.Select(exchange => new ExchangeDocument
                    {
                        Name = exchange.Name,
                        Type = exchange.Type.ToString().ToLowerInvariant(),
                        Durable = exchange.Durable,
                        AutoDelete = exchange.AutoDelete,
                        Internal = exchange.Internal,
                        Arguments = new Dictionary<string, object?>(exchange.Arguments, StringComparer.Ordinal),
                        Metadata = new Dictionary<string, string>(exchange.Metadata, StringComparer.Ordinal),
                    }).ToArray(),
                    Queues = vhost.Queues.Select(queue => new QueueDocument
                    {
                        Name = queue.Name,
                        Type = queue.Type.ToString().ToLowerInvariant(),
                        Durable = queue.Durable,
                        Exclusive = queue.Exclusive,
                        AutoDelete = queue.AutoDelete,
                        Arguments = new Dictionary<string, object?>(queue.Arguments, StringComparer.Ordinal),
                        Metadata = new Dictionary<string, string>(queue.Metadata, StringComparer.Ordinal),
                    }).ToArray(),
                    Bindings = vhost.Bindings.Select(binding => new BindingDocument
                    {
                        SourceExchange = binding.SourceExchange,
                        Destination = binding.Destination,
                        DestinationType = binding.DestinationType == Domain.Topology.BindingDestinationType.Queue ? "queue" : "exchange",
                        RoutingKey = binding.RoutingKey,
                        Arguments = new Dictionary<string, object?>(binding.Arguments, StringComparer.Ordinal),
                        Metadata = new Dictionary<string, string>(binding.Metadata, StringComparer.Ordinal),
                    }).ToArray(),
                })
                .ToArray(),
        };
    }
}
