using SphereRabbitMQ.IaC.Application.Models;
using SphereRabbitMQ.IaC.Application.Parsing.Interfaces;
using SphereRabbitMQ.IaC.Application.Variables.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.Yaml.Contracts;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SphereRabbitMQ.IaC.Infrastructure.Yaml.Parsing;

/// <summary>
/// Parses RabbitMQ topology definitions from YAML documents.
/// </summary>
public sealed class TopologyYamlParser : ITopologyParser
{
    private readonly IDeserializer _deserializer;
    private readonly IVariableResolver _variableResolver;

    /// <summary>
    /// Creates a new YAML topology parser.
    /// </summary>
    public TopologyYamlParser(IVariableResolver variableResolver)
    {
        ArgumentNullException.ThrowIfNull(variableResolver);

        _variableResolver = variableResolver;
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <inheritdoc />
    public async ValueTask<TopologyDocument> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream, leaveOpen: true);
        var yamlContent = await reader.ReadToEndAsync(cancellationToken);
        var yamlDocument = _deserializer.Deserialize<TopologyYamlDocument>(yamlContent) ?? new TopologyYamlDocument();
        var resolvedDocument = ResolveVariables(yamlDocument);
        return YamlTopologyDocumentMapper.Map(resolvedDocument);
    }

    private TopologyYamlDocument ResolveVariables(TopologyYamlDocument document)
        => document with
        {
            Broker = ResolveBroker(document.Broker, document.Variables),
            DebugQueues = ResolveDebugQueues(document.DebugQueues, document.Variables),
            Metadata = ResolveStringDictionary(document.Metadata, document.Variables),
            Variables = ResolveNullableStringDictionary(document.Variables, document.Variables),
            Naming = ResolveNaming(document.Naming, document.Variables),
            VirtualHosts = document.VirtualHosts.Select(vhost => ResolveVirtualHost(vhost, document.Variables)).ToList(),
        };

    private BrokerYamlDocument? ResolveBroker(
        BrokerYamlDocument? document,
        IReadOnlyDictionary<string, string?> variables)
        => document is null
            ? null
            : document with
            {
                ManagementUrl = ResolveOptional(document.ManagementUrl, variables),
                Username = ResolveOptional(document.Username, variables),
                Password = ResolveOptional(document.Password, variables),
                VirtualHosts = document.VirtualHosts.Select(vhost => Resolve(vhost, variables)).ToList(),
            };

    private VirtualHostYamlDocument ResolveVirtualHost(
        VirtualHostYamlDocument document,
        IReadOnlyDictionary<string, string?> variables)
        => document with
        {
            Name = Resolve(document.Name, variables),
            Metadata = ResolveStringDictionary(document.Metadata, variables),
            Exchanges = document.Exchanges.Select(exchange => ResolveExchange(exchange, variables)).ToList(),
            Queues = document.Queues.Select(queue => ResolveQueue(queue, variables)).ToList(),
            Bindings = document.Bindings.Select(binding => ResolveBinding(binding, variables)).ToList(),
        };

    private ExchangeYamlDocument ResolveExchange(
        ExchangeYamlDocument document,
        IReadOnlyDictionary<string, string?> variables)
        => document with
        {
            Name = Resolve(document.Name, variables),
            Type = Resolve(document.Type, variables),
            Arguments = ResolveObjectDictionary(document.Arguments, variables),
            Metadata = ResolveStringDictionary(document.Metadata, variables),
        };

    private QueueYamlDocument ResolveQueue(
        QueueYamlDocument document,
        IReadOnlyDictionary<string, string?> variables)
        => document with
        {
            Name = Resolve(document.Name, variables),
            Type = Resolve(document.Type, variables),
            Ttl = ResolveOptional(document.Ttl, variables),
            Arguments = ResolveObjectDictionary(document.Arguments, variables),
            Metadata = ResolveStringDictionary(document.Metadata, variables),
            DeadLetter = ResolveDeadLetter(document.DeadLetter, variables),
            Retry = ResolveRetry(document.Retry, variables),
        };

    private DebugQueuesYamlDocument? ResolveDebugQueues(
        DebugQueuesYamlDocument? document,
        IReadOnlyDictionary<string, string?> variables)
        => document is null
            ? null
            : document with
            {
                QueueSuffix = Resolve(document.QueueSuffix, variables),
            };

    private BindingYamlDocument ResolveBinding(
        BindingYamlDocument document,
        IReadOnlyDictionary<string, string?> variables)
        => document with
        {
            SourceExchange = Resolve(document.SourceExchange, variables),
            Destination = Resolve(document.Destination, variables),
            DestinationType = Resolve(document.DestinationType, variables),
            RoutingKey = Resolve(document.RoutingKey, variables),
            Arguments = ResolveObjectDictionary(document.Arguments, variables),
            Metadata = ResolveStringDictionary(document.Metadata, variables),
        };

    private DeadLetterYamlDocument? ResolveDeadLetter(
        DeadLetterYamlDocument? document,
        IReadOnlyDictionary<string, string?> variables)
        => document is null
            ? null
            : document with
            {
                ExchangeName = ResolveOptional(document.ExchangeName, variables),
                QueueName = ResolveOptional(document.QueueName, variables),
                RoutingKey = ResolveOptional(document.RoutingKey, variables),
                Ttl = ResolveOptional(document.Ttl, variables),
            };

    private RetryYamlDocument? ResolveRetry(
        RetryYamlDocument? document,
        IReadOnlyDictionary<string, string?> variables)
        => document is null
            ? null
            : document with
            {
                ExchangeName = ResolveOptional(document.ExchangeName, variables),
                Steps = document.Steps.Select(step => step with
                {
                    Delay = Resolve(step.Delay, variables),
                    Name = ResolveOptional(step.Name, variables),
                    QueueName = ResolveOptional(step.QueueName, variables),
                    RoutingKey = ResolveOptional(step.RoutingKey, variables),
                }).ToList(),
            };

    private NamingConventionYamlDocument? ResolveNaming(
        NamingConventionYamlDocument? document,
        IReadOnlyDictionary<string, string?> variables)
        => document is null
            ? null
            : document with
            {
                Separator = ResolveOptional(document.Separator, variables),
                RetryExchangeSuffix = ResolveOptional(document.RetryExchangeSuffix, variables),
                RetryQueueSuffix = ResolveOptional(document.RetryQueueSuffix, variables),
                DeadLetterExchangeSuffix = ResolveOptional(document.DeadLetterExchangeSuffix, variables),
                DeadLetterQueueSuffix = ResolveOptional(document.DeadLetterQueueSuffix, variables),
                StepTokenPrefix = ResolveOptional(document.StepTokenPrefix, variables),
            };

    private Dictionary<string, string> ResolveStringDictionary(
        IReadOnlyDictionary<string, string> dictionary,
        IReadOnlyDictionary<string, string?> variables)
        => dictionary.ToDictionary(
            pair => Resolve(pair.Key, variables),
            pair => Resolve(pair.Value, variables),
            StringComparer.Ordinal);

    private Dictionary<string, string?> ResolveNullableStringDictionary(
        IReadOnlyDictionary<string, string?> dictionary,
        IReadOnlyDictionary<string, string?> variables)
        => dictionary.ToDictionary(
            pair => Resolve(pair.Key, variables),
            pair => pair.Value is null ? null : Resolve(pair.Value, variables),
            StringComparer.Ordinal);

    private Dictionary<string, object?> ResolveObjectDictionary(
        IReadOnlyDictionary<string, object?> dictionary,
        IReadOnlyDictionary<string, string?> variables)
        => dictionary.ToDictionary(
            pair => Resolve(pair.Key, variables),
            pair => ResolveObject(pair.Value, variables),
            StringComparer.Ordinal);

    private object? ResolveObject(object? value, IReadOnlyDictionary<string, string?> variables)
        => value switch
        {
            null => null,
            string stringValue => Resolve(stringValue, variables),
            _ => value,
        };

    private string Resolve(string value, IReadOnlyDictionary<string, string?> variables)
        => _variableResolver.Resolve(value, variables);

    private string? ResolveOptional(string? value, IReadOnlyDictionary<string, string?> variables)
        => value is null ? null : Resolve(value, variables);
}
