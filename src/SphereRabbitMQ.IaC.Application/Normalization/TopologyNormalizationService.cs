using SphereRabbitMQ.IaC.Application.Models;
using SphereRabbitMQ.IaC.Application.Normalization.Interfaces;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Application.Normalization;

/// <summary>
/// Converts source-neutral documents into deterministic, normalized domain topology models.
/// </summary>
public sealed class TopologyNormalizationService : ITopologyNormalizer
{
    /// <inheritdoc />
    public ValueTask<TopologyDefinition> NormalizeAsync(
        TopologyDocument document,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(document);

        var issues = new List<TopologyIssue>();
        var namingPolicy = CreateNamingPolicy(document.Naming);
        var virtualHosts = document.VirtualHosts
            .Select(vhost => NormalizeVirtualHost(vhost, document.DebugQueues, namingPolicy, issues))
            .OrderBy(vhost => vhost.Name, StringComparer.Ordinal)
            .ToArray();
        var decommission = NormalizeDecommission(document.Decommission, issues);

        if (issues.Count > 0)
        {
            throw new TopologyNormalizationException(issues);
        }

        return ValueTask.FromResult(new TopologyDefinition(
            virtualHosts,
            decommission,
            namingPolicy,
            NormalizeStringDictionary(document.Metadata)));
    }

    private static VirtualHostDefinition NormalizeVirtualHost(
        VirtualHostDocument document,
        DebugQueuesDocument? debugQueues,
        NamingConventionPolicy namingPolicy,
        ICollection<TopologyIssue> issues)
    {
        var explicitExchanges = document.Exchanges
            .Select(exchange => NormalizeExchange(document.Name, exchange, issues))
            .ToList();
        var explicitQueues = document.Queues
            .Select(queue => NormalizeQueue(document.Name, queue, namingPolicy, issues))
            .ToList();
        var explicitBindings = document.Bindings
            .Select(binding => NormalizeBinding(document.Name, binding, issues))
            .ToList();

        var generatedExchanges = new List<ExchangeDefinition>();
        var generatedQueues = new List<QueueDefinition>();
        var generatedBindings = new List<BindingDefinition>();

        foreach (var queue in explicitQueues)
        {
            AppendDerivedArtifacts(queue, namingPolicy, generatedExchanges, generatedQueues, generatedBindings);
        }

        AppendDebugArtifacts(
            debugQueues,
            explicitExchanges.Concat(generatedExchanges),
            generatedQueues,
            generatedBindings);

        return new VirtualHostDefinition(
            document.Name.Trim(),
            explicitExchanges
                .Concat(generatedExchanges)
                .OrderBy(exchange => exchange.Name, StringComparer.Ordinal)
                .ToArray(),
            explicitQueues
                .Concat(generatedQueues)
                .OrderBy(queue => queue.Name, StringComparer.Ordinal)
                .ToArray(),
            explicitBindings
                .Concat(generatedBindings)
                .OrderBy(binding => binding.Key, StringComparer.Ordinal)
                .ToArray(),
            NormalizeStringDictionary(document.Metadata));
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
        NamingConventionPolicy namingPolicy,
        ICollection<TopologyIssue> issues)
    {
        var queueType = ParseQueueType(document.Type, $"/virtualHosts/{virtualHostName}/queues/{document.Name}", issues);
        var normalizedArguments = NormalizeObjectDictionary(document.Arguments).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var ttl = NormalizeQueueTtl(virtualHostName, document.Name, document.Ttl, issues);
        var deadLetter = NormalizeDeadLetter(virtualHostName, document.Name, document.DeadLetter, issues);
        var retry = NormalizeRetry(virtualHostName, document.Name, document.Retry, issues);

        ApplyDerivedQueueArguments(document.Name, normalizedArguments, ttl, deadLetter, retry, namingPolicy, issues);
        EnsureQueueTypeArgument(queueType, normalizedArguments);

        return new QueueDefinition(
            document.Name.Trim(),
            queueType,
            document.Durable,
            document.Exclusive,
            document.AutoDelete,
            normalizedArguments,
            deadLetter,
            retry,
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

    private static IReadOnlyList<DecommissionVirtualHostDefinition> NormalizeDecommission(
        DecommissionDocument? document,
        ICollection<TopologyIssue> issues)
        => document?.VirtualHosts
            .Select(vhost => NormalizeDecommissionVirtualHost(vhost, issues))
            .OrderBy(vhost => vhost.Name, StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<DecommissionVirtualHostDefinition>();

    private static DecommissionVirtualHostDefinition NormalizeDecommissionVirtualHost(
        DecommissionVirtualHostDocument document,
        ICollection<TopologyIssue> issues)
        => new(
            document.Name.Trim(),
            document.Exchanges
                .Select(exchange => exchange.Trim())
                .OrderBy(exchange => exchange, StringComparer.Ordinal)
                .ToArray(),
            document.Queues
                .Select(queue => queue.Trim())
                .OrderBy(queue => queue, StringComparer.Ordinal)
                .ToArray(),
            document.Bindings
                .Select(binding => NormalizeBinding(document.Name, binding, issues))
                .OrderBy(binding => binding.Key, StringComparer.Ordinal)
                .ToArray());

    private static void AppendDerivedArtifacts(
        QueueDefinition queue,
        NamingConventionPolicy namingPolicy,
        ICollection<ExchangeDefinition> exchanges,
        ICollection<QueueDefinition> queues,
        ICollection<BindingDefinition> bindings)
    {
        if (queue.DeadLetter is { Enabled: true } deadLetter && queue.Retry is not { Enabled: true })
        {
            AppendDeadLetterArtifacts(queue, deadLetter, namingPolicy, exchanges, queues, bindings);
        }

        if (queue.Retry is { Enabled: true } retry)
        {
            AppendRetryArtifacts(queue, retry, namingPolicy, exchanges, queues, bindings);

            if (queue.DeadLetter is { Enabled: true } retryDeadLetter)
            {
                AppendDeadLetterArtifacts(queue, retryDeadLetter, namingPolicy, exchanges, queues, bindings);
            }
        }
    }

    private static void AppendDeadLetterArtifacts(
        QueueDefinition queue,
        DeadLetterDefinition deadLetter,
        NamingConventionPolicy namingPolicy,
        ICollection<ExchangeDefinition> exchanges,
        ICollection<QueueDefinition> queues,
        ICollection<BindingDefinition> bindings)
    {
        var exchangeName = deadLetter.ExchangeName ?? namingPolicy.GetDeadLetterExchangeName(queue.Name);
        var queueName = deadLetter.QueueName ?? namingPolicy.GetDeadLetterQueueName(queue.Name);
        var routingKey = deadLetter.RoutingKey ?? queue.Name;

        exchanges.Add(CreateGeneratedExchange(exchangeName, queue.Name));
        queues.Add(CreateGeneratedQueue(queueName, queue.Name, deadLetter.Ttl, null));
        bindings.Add(CreateGeneratedBinding(exchangeName, queueName, routingKey, queue.Name));
    }

    private static void AppendRetryArtifacts(
        QueueDefinition queue,
        RetryDefinition retry,
        NamingConventionPolicy namingPolicy,
        ICollection<ExchangeDefinition> exchanges,
        ICollection<QueueDefinition> queues,
        ICollection<BindingDefinition> bindings)
    {
        var retryExchangeName = retry.ExchangeName ?? namingPolicy.GetRetryExchangeName(queue.Name);
        exchanges.Add(CreateGeneratedExchange(retryExchangeName, queue.Name));

        for (var index = 0; index < retry.Steps.Count; index++)
        {
            var step = retry.Steps[index];
            var stepName = step.Name ?? namingPolicy.GetRetryStepToken(index);
            var retryQueueName = step.QueueName ?? namingPolicy.GetRetryQueueName(queue.Name, stepName);
            var retryRoutingKey = step.RoutingKey ?? retryQueueName;
            var deadLetterExchange = index < retry.Steps.Count - 1 ? retryExchangeName : TopologyNormalizationConsts.EmptyExchangeName;
            var deadLetterRoutingKey = index < retry.Steps.Count - 1
                ? retry.Steps[index + 1].RoutingKey ?? retry.Steps[index + 1].QueueName ?? namingPolicy.GetRetryQueueName(queue.Name, retry.Steps[index + 1].Name ?? namingPolicy.GetRetryStepToken(index + 1))
                : queue.Name;

            queues.Add(CreateGeneratedQueue(retryQueueName, queue.Name, step.Delay, (deadLetterExchange, deadLetterRoutingKey)));
            bindings.Add(CreateGeneratedBinding(retryExchangeName, retryQueueName, retryRoutingKey, queue.Name));
        }
    }

    private static void AppendDebugArtifacts(
        DebugQueuesDocument? debugQueues,
        IEnumerable<ExchangeDefinition> exchanges,
        ICollection<QueueDefinition> queues,
        ICollection<BindingDefinition> bindings)
    {
        if (debugQueues is not { Enabled: true })
        {
            return;
        }

        foreach (var exchange in exchanges.OrderBy(exchange => exchange.Name, StringComparer.Ordinal))
        {
            var debugQueueName = $"{exchange.Name}.{debugQueues.QueueSuffix}";
            queues.Add(CreateGeneratedDebugQueue(debugQueueName, exchange.Name));
            bindings.Add(CreateGeneratedDebugBinding(exchange.Name, debugQueueName));
        }
    }

    private static ExchangeDefinition CreateGeneratedExchange(string exchangeName, string sourceQueueName)
        => new(
            exchangeName,
            ExchangeType.Direct,
            arguments: new Dictionary<string, object?>(StringComparer.Ordinal),
            metadata: CreateGeneratedMetadata(sourceQueueName));

    private static QueueDefinition CreateGeneratedQueue(
        string queueName,
        string sourceQueueName,
        TimeSpan? ttl,
        (string Exchange, string RoutingKey)? deadLetterTarget)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        EnsureQueueTypeArgument(QueueType.Classic, arguments);

        if (ttl is not null)
        {
            arguments[TopologyNormalizationConsts.MessageTtlArgument] = Convert.ToInt64(ttl.Value.TotalMilliseconds);
        }

        if (deadLetterTarget is not null)
        {
            arguments[TopologyNormalizationConsts.DeadLetterExchangeArgument] = deadLetterTarget.Value.Exchange;
            arguments[TopologyNormalizationConsts.DeadLetterRoutingKeyArgument] = deadLetterTarget.Value.RoutingKey;
        }

        return new QueueDefinition(
            queueName,
            arguments: arguments,
            metadata: CreateGeneratedMetadata(sourceQueueName));
    }

    private static QueueDefinition CreateGeneratedDebugQueue(
        string queueName,
        string sourceExchangeName)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        EnsureQueueTypeArgument(QueueType.Classic, arguments);

        return new QueueDefinition(
            queueName,
            QueueType.Classic,
            true,
            false,
            false,
            arguments,
            null,
            null,
            CreateGeneratedExchangeMetadata(sourceExchangeName));
    }

    private static BindingDefinition CreateGeneratedBinding(
        string sourceExchange,
        string destinationQueue,
        string routingKey,
        string sourceQueueName)
        => new(
            sourceExchange,
            destinationQueue,
            BindingDestinationType.Queue,
            routingKey,
            metadata: CreateGeneratedMetadata(sourceQueueName));

    private static BindingDefinition CreateGeneratedDebugBinding(
        string sourceExchange,
        string destinationQueue)
        => new(
            sourceExchange,
            destinationQueue,
            BindingDestinationType.Queue,
            TopologyNormalizationConsts.DebugQueueRoutingKey,
            metadata: CreateGeneratedExchangeMetadata(sourceExchange));

    private static IReadOnlyDictionary<string, string> CreateGeneratedMetadata(string sourceQueueName)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TopologyNormalizationConsts.GeneratedMetadataKey] = TopologyNormalizationConsts.GeneratedMetadataValue,
            [TopologyNormalizationConsts.SourceQueueMetadataKey] = sourceQueueName,
        };

    private static IReadOnlyDictionary<string, string> CreateGeneratedExchangeMetadata(string sourceExchangeName)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TopologyNormalizationConsts.GeneratedMetadataKey] = TopologyNormalizationConsts.GeneratedMetadataValue,
            [TopologyNormalizationConsts.SourceExchangeMetadataKey] = sourceExchangeName,
        };

    private static void ApplyDerivedQueueArguments(
        string queueName,
        IDictionary<string, object?> arguments,
        TimeSpan? ttl,
        DeadLetterDefinition? deadLetter,
        RetryDefinition? retry,
        NamingConventionPolicy namingPolicy,
        ICollection<TopologyIssue> issues)
    {
        if (ttl is not null)
        {
            SetArgument(arguments, TopologyNormalizationConsts.MessageTtlArgument, Convert.ToInt64(ttl.Value.TotalMilliseconds), queueName, issues);
        }

        if (retry is { Enabled: true })
        {
            var retryExchangeName = retry.ExchangeName ?? namingPolicy.GetRetryExchangeName(queueName);
            var firstStep = retry.Steps.FirstOrDefault();
            var firstRoutingKey = firstStep?.RoutingKey
                ?? firstStep?.QueueName
                ?? (firstStep is null ? null : namingPolicy.GetRetryQueueName(queueName, firstStep.Name ?? namingPolicy.GetRetryStepToken(0)));

            if (firstRoutingKey is not null)
            {
                SetArgument(arguments, TopologyNormalizationConsts.DeadLetterExchangeArgument, retryExchangeName, queueName, issues);
                SetArgument(arguments, TopologyNormalizationConsts.DeadLetterRoutingKeyArgument, firstRoutingKey, queueName, issues);
            }

            return;
        }

        if (deadLetter is { Enabled: true })
        {
            var deadLetterExchangeName = deadLetter.ExchangeName ?? namingPolicy.GetDeadLetterExchangeName(queueName);
            var deadLetterRoutingKey = deadLetter.RoutingKey ?? queueName;
            SetArgument(arguments, TopologyNormalizationConsts.DeadLetterExchangeArgument, deadLetterExchangeName, queueName, issues);
            SetArgument(arguments, TopologyNormalizationConsts.DeadLetterRoutingKeyArgument, deadLetterRoutingKey, queueName, issues);
        }
    }

    private static TimeSpan? NormalizeQueueTtl(
        string virtualHostName,
        string queueName,
        string? ttl,
        ICollection<TopologyIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(ttl))
        {
            return null;
        }

        var path = $"/virtualHosts/{virtualHostName}/queues/{queueName}/ttl";
        if (!TimeSpan.TryParse(ttl, out var parsedTtl) || parsedTtl <= TimeSpan.Zero)
        {
            issues.Add(new TopologyIssue(
                "invalid-queue-ttl",
                $"Queue ttl '{ttl}' is invalid. Use a positive TimeSpan value.",
                path,
                TopologyIssueSeverity.Error));
            return null;
        }

        return parsedTtl;
    }

    private static TimeSpan? NormalizeDeadLetterTtl(
        string virtualHostName,
        string queueName,
        string? ttl,
        ICollection<TopologyIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(ttl))
        {
            return null;
        }

        var path = $"/virtualHosts/{virtualHostName}/queues/{queueName}/deadLetter/ttl";
        if (!TimeSpan.TryParse(ttl, out var parsedTtl) || parsedTtl <= TimeSpan.Zero)
        {
            issues.Add(new TopologyIssue(
                "invalid-dead-letter-ttl",
                $"Dead-letter ttl '{ttl}' is invalid. Use a positive TimeSpan value.",
                path,
                TopologyIssueSeverity.Error));
            return null;
        }

        return parsedTtl;
    }

    private static void EnsureQueueTypeArgument(QueueType queueType, IDictionary<string, object?> arguments)
    {
        var queueTypeValue = queueType switch
        {
            QueueType.Classic => "classic",
            QueueType.Quorum => "quorum",
            _ => "classic",
        };

        arguments[TopologyNormalizationConsts.QueueTypeArgument] = queueTypeValue;
    }

    private static void SetArgument(
        IDictionary<string, object?> arguments,
        string key,
        object value,
        string queueName,
        ICollection<TopologyIssue> issues)
    {
        if (arguments.TryGetValue(key, out var existingValue) &&
            existingValue is not null &&
            !string.Equals(existingValue.ToString(), value.ToString(), StringComparison.Ordinal))
        {
            issues.Add(new TopologyIssue(
                "conflicting-derived-argument",
                $"Queue '{queueName}' defines argument '{key}' with a value that conflicts with derived topology.",
                $"/queues/{queueName}/arguments/{key}",
                TopologyIssueSeverity.Error));
            return;
        }

        arguments[key] = value;
    }

    private static DeadLetterDefinition? NormalizeDeadLetter(
        string virtualHostName,
        string queueName,
        DeadLetterDocument? document,
        ICollection<TopologyIssue> issues)
    {
        if (document is null)
        {
            return null;
        }

        var ttl = NormalizeDeadLetterTtl(virtualHostName, queueName, document.Ttl, issues);
        return new DeadLetterDefinition(document.Enabled, document.ExchangeName, document.QueueName, document.RoutingKey, ttl);
    }

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
            document.ExchangeName);
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
