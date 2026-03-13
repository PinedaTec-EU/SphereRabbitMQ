using SphereRabbitMQ.IaC.Application.Models;

namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;

/// <summary>
/// Maps YAML transport contracts into source-neutral application documents.
/// </summary>
public static class YamlTopologyDocumentMapper
{
    public static TopologyDocument Map(TopologyYamlDocument yamlDocument)
    {
        ArgumentNullException.ThrowIfNull(yamlDocument);

        return new TopologyDocument
        {
            Broker = yamlDocument.Broker is null
                ? null
                : new BrokerDocument
                {
                    ManagementUrl = yamlDocument.Broker.ManagementUrl,
                    Username = yamlDocument.Broker.Username,
                    Password = yamlDocument.Broker.Password,
                    VirtualHosts = yamlDocument.Broker.VirtualHosts.ToArray(),
                },
            Decommission = yamlDocument.Decommission is null
                ? null
                : new DecommissionDocument
                {
                    VirtualHosts = yamlDocument.Decommission.VirtualHosts.Select(Map).ToArray(),
                },
            DebugQueues = yamlDocument.DebugQueues is null
                ? null
                : new DebugQueuesDocument
                {
                    Enabled = yamlDocument.DebugQueues.Enabled,
                    QueueSuffix = yamlDocument.DebugQueues.QueueSuffix,
                },
            Metadata = new Dictionary<string, string>(yamlDocument.Metadata, StringComparer.Ordinal),
            Naming = yamlDocument.Naming is null
                ? null
                : new NamingConventionDocument
                {
                    Separator = yamlDocument.Naming.Separator,
                    RetryExchangeSuffix = yamlDocument.Naming.RetryExchangeSuffix,
                    RetryQueueSuffix = yamlDocument.Naming.RetryQueueSuffix,
                    DeadLetterExchangeSuffix = yamlDocument.Naming.DeadLetterExchangeSuffix,
                    DeadLetterQueueSuffix = yamlDocument.Naming.DeadLetterQueueSuffix,
                    StepTokenPrefix = yamlDocument.Naming.StepTokenPrefix,
                },
            VirtualHosts = yamlDocument.VirtualHosts.Select(Map).ToArray(),
        };
    }

    private static VirtualHostDocument Map(VirtualHostYamlDocument yamlDocument)
        => new()
        {
            Name = yamlDocument.Name,
            Metadata = new Dictionary<string, string>(yamlDocument.Metadata, StringComparer.Ordinal),
            Exchanges = yamlDocument.Exchanges.Select(Map).ToArray(),
            Queues = yamlDocument.Queues.Select(Map).ToArray(),
            Bindings = yamlDocument.Bindings.Select(Map).ToArray(),
        };

    private static DecommissionVirtualHostDocument Map(DecommissionVirtualHostYamlDocument yamlDocument)
        => new()
        {
            Name = yamlDocument.Name,
            Exchanges = yamlDocument.Exchanges.ToArray(),
            Queues = yamlDocument.Queues.ToArray(),
            Bindings = yamlDocument.Bindings.Select(Map).ToArray(),
        };

    private static ExchangeDocument Map(ExchangeYamlDocument yamlDocument)
        => new()
        {
            Name = yamlDocument.Name,
            Type = yamlDocument.Type,
            Durable = yamlDocument.Durable,
            AutoDelete = yamlDocument.AutoDelete,
            Internal = yamlDocument.Internal,
            Arguments = new Dictionary<string, object?>(yamlDocument.Arguments, StringComparer.Ordinal),
            Metadata = new Dictionary<string, string>(yamlDocument.Metadata, StringComparer.Ordinal),
        };

    private static QueueDocument Map(QueueYamlDocument yamlDocument)
        => new()
        {
            Name = yamlDocument.Name,
            Type = yamlDocument.Type,
            Durable = yamlDocument.Durable,
            Exclusive = yamlDocument.Exclusive,
            AutoDelete = yamlDocument.AutoDelete,
            Ttl = yamlDocument.Ttl,
            Arguments = new Dictionary<string, object?>(yamlDocument.Arguments, StringComparer.Ordinal),
            Metadata = new Dictionary<string, string>(yamlDocument.Metadata, StringComparer.Ordinal),
            DeadLetter = yamlDocument.DeadLetter is null
                ? null
                : new DeadLetterDocument
                {
                    Enabled = yamlDocument.DeadLetter.Enabled,
                    ExchangeName = yamlDocument.DeadLetter.ExchangeName,
                    QueueName = yamlDocument.DeadLetter.QueueName,
                    RoutingKey = yamlDocument.DeadLetter.RoutingKey,
                    Ttl = yamlDocument.DeadLetter.Ttl,
                },
            Retry = yamlDocument.Retry is null
                ? null
                : new RetryDocument
                {
                    Enabled = yamlDocument.Retry.Enabled,
                    AutoGenerateArtifacts = yamlDocument.Retry.AutoGenerateArtifacts,
                    ExchangeName = yamlDocument.Retry.ExchangeName,
                    Steps = yamlDocument.Retry.Steps.Select(step => new RetryStepDocument
                    {
                        Delay = step.Delay,
                        Name = step.Name,
                        QueueName = step.QueueName,
                        RoutingKey = step.RoutingKey,
                    }).ToArray(),
                },
        };

    private static BindingDocument Map(BindingYamlDocument yamlDocument)
        => new()
        {
            SourceExchange = yamlDocument.SourceExchange,
            Destination = yamlDocument.Destination,
            DestinationType = yamlDocument.DestinationType,
            RoutingKey = yamlDocument.RoutingKey,
            Arguments = new Dictionary<string, object?>(yamlDocument.Arguments, StringComparer.Ordinal),
            Metadata = new Dictionary<string, string>(yamlDocument.Metadata, StringComparer.Ordinal),
        };
}
