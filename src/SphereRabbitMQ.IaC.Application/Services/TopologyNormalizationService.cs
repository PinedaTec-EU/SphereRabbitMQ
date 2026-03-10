using SphereRabbitMQ.IaC.Application.Abstractions;
using SphereRabbitMQ.IaC.Application.Models;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Application.Services;

/// <summary>
/// Converts source-neutral documents into deterministic, normalized domain topology models.
/// </summary>
public sealed class TopologyNormalizationService : ITopologyNormalizer
{
    public ValueTask<TopologyDefinition> NormalizeAsync(
        TopologyDocument document,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(document);

        var issues = new List<TopologyIssue>();
        var namingPolicy = CreateNamingPolicy(document.Naming);
        var virtualHosts = document.VirtualHosts
            .Select(vhost => NormalizeVirtualHost(vhost, issues))
            .OrderBy(vhost => vhost.Name, StringComparer.Ordinal)
            .ToArray();

        if (issues.Count > 0)
        {
            throw new TopologyNormalizationException(issues);
        }

        return ValueTask.FromResult(new TopologyDefinition(
            virtualHosts,
            namingPolicy,
            NormalizeStringDictionary(document.Metadata)));
    }

    private static VirtualHostDefinition NormalizeVirtualHost(
        VirtualHostDocument document,
        ICollection<TopologyIssue> issues)
    {
        try
        {
            return new VirtualHostDefinition(
                document.Name.Trim(),
                document.Exchanges.Select(exchange => NormalizeExchange(document.Name, exchange, issues))
                    .OrderBy(exchange => exchange.Name, StringComparer.Ordinal)
                    .ToArray(),
                document.Queues.Select(queue => NormalizeQueue(document.Name, queue, issues))
                    .OrderBy(queue => queue.Name, StringComparer.Ordinal)
                    .ToArray(),
                document.Bindings.Select(binding => NormalizeBinding(document.Name, binding, issues))
                    .OrderBy(binding => binding.Key, StringComparer.Ordinal)
                    .ToArray(),
                NormalizeStringDictionary(document.Metadata));
        }
        catch (ArgumentException exception)
        {
            issues.Add(new TopologyIssue(
                "invalid-vhost",
                exception.Message,
                $"/virtualHosts/{document.Name}",
                TopologyIssueSeverity.Error));

            return new VirtualHostDefinition("__invalid__");
        }
    }

    private static ExchangeDefinition NormalizeExchange(
        string virtualHostName,
        ExchangeDocument document,
        ICollection<TopologyIssue> issues)
    {
        var exchangeType = ParseExchangeType(document.Type, $"/virtualHosts/{virtualHostName}/exchanges/{document.Name}", issues);

        return new ExchangeDefinition(
            document.Name.Trim(),
            exchangeType,
            document.Durable,
            document.AutoDelete,
            document.Internal,
            NormalizeObjectDictionary(document.Arguments),
            NormalizeStringDictionary(document.Metadata));
    }

    private static QueueDefinition NormalizeQueue(
        string virtualHostName,
        QueueDocument document,
        ICollection<TopologyIssue> issues)
    {
        var queueType = ParseQueueType(document.Type, $"/virtualHosts/{virtualHostName}/queues/{document.Name}", issues);

        return new QueueDefinition(
            document.Name.Trim(),
            queueType,
            document.Durable,
            document.Exclusive,
            document.AutoDelete,
            NormalizeObjectDictionary(document.Arguments),
            NormalizeDeadLetter(document.DeadLetter),
            NormalizeRetry(virtualHostName, document.Name, document.Retry, issues),
            NormalizeStringDictionary(document.Metadata));
    }

    private static BindingDefinition NormalizeBinding(
        string virtualHostName,
        BindingDocument document,
        ICollection<TopologyIssue> issues)
    {
        var destinationType = ParseDestinationType(
            document.DestinationType,
            $"/virtualHosts/{virtualHostName}/bindings/{document.SourceExchange}->{document.Destination}",
            issues);

        return new BindingDefinition(
            document.SourceExchange.Trim(),
            document.Destination.Trim(),
            destinationType,
            document.RoutingKey,
            NormalizeObjectDictionary(document.Arguments),
            NormalizeStringDictionary(document.Metadata));
    }

    private static DeadLetterDefinition? NormalizeDeadLetter(DeadLetterDocument? document)
        => document is null
            ? null
            : new DeadLetterDefinition(document.Enabled, document.ExchangeName, document.QueueName, document.RoutingKey);

    private static RetryDefinition? NormalizeRetry(
        string virtualHostName,
        string queueName,
        RetryDocument? document,
        ICollection<TopologyIssue> issues)
    {
        if (document is null)
        {
            return null;
        }

        var steps = new List<RetryStepDefinition>();
        for (var index = 0; index < document.Steps.Count; index++)
        {
            var step = document.Steps[index];
            var path = $"/virtualHosts/{virtualHostName}/queues/{queueName}/retry/steps/{index}";
            if (!TimeSpan.TryParse(step.Delay, out var delay) || delay <= TimeSpan.Zero)
            {
                issues.Add(new TopologyIssue(
                    "invalid-retry-delay",
                    $"Retry delay '{step.Delay}' is invalid. Use a positive TimeSpan value.",
                    path,
                    TopologyIssueSeverity.Error));
                continue;
            }

            steps.Add(new RetryStepDefinition(delay, step.Name, step.QueueName, step.RoutingKey));
        }

        return new RetryDefinition(
            steps,
            document.Enabled,
            document.AutoGenerateArtifacts,
            document.ExchangeName,
            document.ParkingLotQueueName);
    }

    private static ExchangeType ParseExchangeType(string value, string path, ICollection<TopologyIssue> issues)
        => value.Trim().ToLowerInvariant() switch
        {
            "direct" => ExchangeType.Direct,
            "topic" => ExchangeType.Topic,
            "fanout" => ExchangeType.Fanout,
            "headers" => ExchangeType.Headers,
            _ => AddInvalidEnumIssue(path, "invalid-exchange-type", $"Exchange type '{value}' is not supported.", issues, ExchangeType.Direct),
        };

    private static QueueType ParseQueueType(string value, string path, ICollection<TopologyIssue> issues)
        => value.Trim().ToLowerInvariant() switch
        {
            "classic" => QueueType.Classic,
            "quorum" => QueueType.Quorum,
            _ => AddInvalidEnumIssue(path, "invalid-queue-type", $"Queue type '{value}' is not supported.", issues, QueueType.Classic),
        };

    private static BindingDestinationType ParseDestinationType(
        string value,
        string path,
        ICollection<TopologyIssue> issues)
        => value.Trim().ToLowerInvariant() switch
        {
            "queue" => BindingDestinationType.Queue,
            "exchange" => BindingDestinationType.Exchange,
            _ => AddInvalidEnumIssue(
                path,
                "invalid-binding-destination-type",
                $"Binding destination type '{value}' is not supported.",
                issues,
                BindingDestinationType.Queue),
        };

    private static T AddInvalidEnumIssue<T>(
        string path,
        string code,
        string message,
        ICollection<TopologyIssue> issues,
        T fallback)
    {
        issues.Add(new TopologyIssue(code, message, path, TopologyIssueSeverity.Error));
        return fallback;
    }

    private static NamingConventionPolicy CreateNamingPolicy(NamingConventionDocument? document)
        => document is null
            ? NamingConventionPolicy.Default
            : new NamingConventionPolicy
            {
                Separator = document.Separator ?? NamingConventionPolicy.Default.Separator,
                RetryExchangeSuffix = document.RetryExchangeSuffix ?? NamingConventionPolicy.Default.RetryExchangeSuffix,
                RetryQueueSuffix = document.RetryQueueSuffix ?? NamingConventionPolicy.Default.RetryQueueSuffix,
                DeadLetterExchangeSuffix = document.DeadLetterExchangeSuffix ?? NamingConventionPolicy.Default.DeadLetterExchangeSuffix,
                DeadLetterQueueSuffix = document.DeadLetterQueueSuffix ?? NamingConventionPolicy.Default.DeadLetterQueueSuffix,
                ParkingLotQueueSuffix = document.ParkingLotQueueSuffix ?? NamingConventionPolicy.Default.ParkingLotQueueSuffix,
                StepTokenPrefix = document.StepTokenPrefix ?? NamingConventionPolicy.Default.StepTokenPrefix,
            };

    private static IReadOnlyDictionary<string, string> NormalizeStringDictionary(IReadOnlyDictionary<string, string>? source)
        => source is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : source
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, object?> NormalizeObjectDictionary(IReadOnlyDictionary<string, object?>? source)
        => source is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : source
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
