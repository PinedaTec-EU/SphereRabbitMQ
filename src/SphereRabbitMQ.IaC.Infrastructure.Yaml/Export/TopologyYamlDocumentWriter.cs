using SphereRabbitMQ.IaC.Application.Export.Interfaces;
using SphereRabbitMQ.IaC.Application.Models;
using SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Export;

/// <summary>
/// Serializes topology documents to YAML.
/// </summary>
public sealed class TopologyYamlDocumentWriter : ITopologyDocumentWriter
{
    private readonly ISerializer _serializer;

    /// <summary>
    /// Creates a new YAML writer for topology documents.
    /// </summary>
    public TopologyYamlDocumentWriter()
    {
        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
    }

    /// <inheritdoc />
    public ValueTask<string> WriteAsync(TopologyDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(document);

        var yamlDocument = new TopologyYamlDocument
        {
            Broker = document.Broker is null
                ? null
                : new BrokerYamlDocument
                {
                    ManagementUrl = document.Broker.ManagementUrl,
                    Username = document.Broker.Username,
                    Password = document.Broker.Password,
                    VirtualHosts = document.Broker.VirtualHosts.ToList(),
                },
            Decommission = document.Decommission is null
                ? null
                : new DecommissionYamlDocument
                {
                    VirtualHosts = document.Decommission.VirtualHosts.Select(MapDecommissionVirtualHost).ToList(),
                },
            DebugQueues = document.DebugQueues is null
                ? null
                : new DebugQueuesYamlDocument
                {
                    Enabled = document.DebugQueues.Enabled,
                    QueueSuffix = document.DebugQueues.QueueSuffix,
                },
            Metadata = new Dictionary<string, string>(document.Metadata, StringComparer.Ordinal),
            Naming = document.Naming is null
                ? null
                : new NamingConventionYamlDocument
                {
                    Separator = document.Naming.Separator,
                    RetryExchangeSuffix = document.Naming.RetryExchangeSuffix,
                    RetryQueueSuffix = document.Naming.RetryQueueSuffix,
                    DeadLetterExchangeSuffix = document.Naming.DeadLetterExchangeSuffix,
                    DeadLetterQueueSuffix = document.Naming.DeadLetterQueueSuffix,
                    StepTokenPrefix = document.Naming.StepTokenPrefix,
                },
            VirtualHosts = document.VirtualHosts.Select(MapVirtualHost).ToList(),
        };

        return ValueTask.FromResult(_serializer.Serialize(yamlDocument));
    }

    private static VirtualHostYamlDocument MapVirtualHost(VirtualHostDocument document)
        => new()
        {
            Name = document.Name,
            Metadata = new Dictionary<string, string>(document.Metadata, StringComparer.Ordinal),
            Exchanges = document.Exchanges.Select(MapExchange).ToList(),
            Queues = document.Queues.Select(MapQueue).ToList(),
            Bindings = document.Bindings.Select(MapBinding).ToList(),
        };

    private static DecommissionVirtualHostYamlDocument MapDecommissionVirtualHost(DecommissionVirtualHostDocument document)
        => new()
        {
            Name = document.Name,
            Exchanges = document.Exchanges.ToList(),
            Queues = document.Queues.ToList(),
            Bindings = document.Bindings.Select(MapBinding).ToList(),
        };

    private static ExchangeYamlDocument MapExchange(ExchangeDocument document)
        => new()
        {
            Name = document.Name,
            Type = document.Type,
            Durable = document.Durable,
            AutoDelete = document.AutoDelete,
            Internal = document.Internal,
            DebugQueue = document.DebugQueue,
            Arguments = new Dictionary<string, object?>(document.Arguments, StringComparer.Ordinal),
            Metadata = new Dictionary<string, string>(document.Metadata, StringComparer.Ordinal),
        };

    private static QueueYamlDocument MapQueue(QueueDocument document)
        => new()
        {
            Name = document.Name,
            Type = document.Type,
            Durable = document.Durable,
            Exclusive = document.Exclusive,
            AutoDelete = document.AutoDelete,
            DebugQueue = document.DebugQueue,
            Arguments = new Dictionary<string, object?>(document.Arguments, StringComparer.Ordinal),
            Metadata = new Dictionary<string, string>(document.Metadata, StringComparer.Ordinal),
            DeadLetter = document.DeadLetter is null
                ? null
                : new DeadLetterYamlDocument
                {
                    Enabled = document.DeadLetter.Enabled,
                    DestinationType = document.DeadLetter.DestinationType,
                    ExchangeName = document.DeadLetter.ExchangeName,
                    QueueName = document.DeadLetter.QueueName,
                    RoutingKey = document.DeadLetter.RoutingKey,
                    Ttl = document.DeadLetter.Ttl,
                },
            Retry = document.Retry is null
                ? null
                : new RetryYamlDocument
                {
                    Enabled = document.Retry.Enabled,
                    AutoGenerateArtifacts = document.Retry.AutoGenerateArtifacts,
                    ExchangeName = document.Retry.ExchangeName,
                    Steps = document.Retry.Steps.Select(step => new RetryStepYamlDocument
                    {
                        Delay = step.Delay.ToString(),
                        Name = step.Name,
                        QueueName = step.QueueName,
                        RoutingKey = step.RoutingKey,
                    }).ToList(),
                },
        };

    private static BindingYamlDocument MapBinding(BindingDocument document)
        => new()
        {
            SourceExchange = document.SourceExchange,
            Destination = document.Destination,
            DestinationType = document.DestinationType,
            RoutingKey = document.RoutingKey,
            Arguments = new Dictionary<string, object?>(document.Arguments, StringComparer.Ordinal),
            Metadata = new Dictionary<string, string>(document.Metadata, StringComparer.Ordinal),
        };
}
