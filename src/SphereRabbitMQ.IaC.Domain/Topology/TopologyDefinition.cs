using SphereRabbitMQ.IaC.Domain.Internal;

namespace SphereRabbitMQ.IaC.Domain.Topology;

/// <summary>
/// Root normalized topology model used for validation, planning, and reconciliation.
/// </summary>
public sealed record TopologyDefinition
{
    public TopologyDefinition(
        IReadOnlyList<VirtualHostDefinition> virtualHosts,
        NamingConventionPolicy? namingPolicy = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        VirtualHosts = Guard.AgainstNull(virtualHosts, nameof(virtualHosts));
        NamingPolicy = namingPolicy ?? NamingConventionPolicy.Default;
        Metadata = metadata ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public IReadOnlyList<VirtualHostDefinition> VirtualHosts { get; }

    public NamingConventionPolicy NamingPolicy { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Validates structural and semantic consistency for the topology.
    /// </summary>
    public TopologyValidationResult Validate()
    {
        var issues = new List<TopologyIssue>();
        var duplicateVirtualHosts = VirtualHosts
            .GroupBy(vhost => vhost.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1);

        foreach (var group in duplicateVirtualHosts)
        {
            issues.Add(new TopologyIssue(
                "duplicate-vhost",
                $"Virtual host '{group.Key}' is declared more than once.",
                $"/virtualHosts/{group.Key}",
                TopologyIssueSeverity.Error));
        }

        foreach (var virtualHost in VirtualHosts)
        {
            ValidateVirtualHost(virtualHost, NamingPolicy, issues);
        }

        return issues.Count == 0
            ? TopologyValidationResult.Success
            : new TopologyValidationResult(issues.OrderBy(issue => issue.Path, StringComparer.Ordinal).ToArray());
    }

    private static void ValidateVirtualHost(
        VirtualHostDefinition virtualHost,
        NamingConventionPolicy namingPolicy,
        ICollection<TopologyIssue> issues)
    {
        ValidateDuplicateNames(
            virtualHost.Exchanges.Select(exchange => exchange.Name),
            $"/virtualHosts/{virtualHost.Name}/exchanges",
            "exchange",
            issues);
        ValidateDuplicateNames(
            virtualHost.Queues.Select(queue => queue.Name),
            $"/virtualHosts/{virtualHost.Name}/queues",
            "queue",
            issues);
        ValidateDuplicateNames(
            virtualHost.Bindings.Select(binding => binding.Key),
            $"/virtualHosts/{virtualHost.Name}/bindings",
            "binding",
            issues);

        var exchanges = virtualHost.Exchanges.ToDictionary(exchange => exchange.Name, StringComparer.Ordinal);
        var queues = virtualHost.Queues.ToDictionary(queue => queue.Name, StringComparer.Ordinal);
        var generatedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var queue in virtualHost.Queues)
        {
            ValidateQueue(virtualHost.Name, queue, namingPolicy, generatedNames, issues);
        }

        foreach (var binding in virtualHost.Bindings)
        {
            var path = $"/virtualHosts/{virtualHost.Name}/bindings/{binding.Key}";
            if (!exchanges.ContainsKey(binding.SourceExchange))
            {
                issues.Add(new TopologyIssue(
                    "missing-source-exchange",
                    $"Binding source exchange '{binding.SourceExchange}' does not exist.",
                    path,
                    TopologyIssueSeverity.Error));
            }

            var destinationExists = binding.DestinationType switch
            {
                BindingDestinationType.Queue => queues.ContainsKey(binding.Destination),
                BindingDestinationType.Exchange => exchanges.ContainsKey(binding.Destination),
                _ => false,
            };

            if (!destinationExists)
            {
                issues.Add(new TopologyIssue(
                    "missing-binding-destination",
                    $"Binding destination '{binding.Destination}' does not exist.",
                    path,
                    TopologyIssueSeverity.Error));
            }
        }
    }

    private static void ValidateQueue(
        string virtualHostName,
        QueueDefinition queue,
        NamingConventionPolicy namingPolicy,
        ISet<string> generatedNames,
        ICollection<TopologyIssue> issues)
    {
        var path = $"/virtualHosts/{virtualHostName}/queues/{queue.Name}";
        ValidateQuorumQueueFlags(queue, path, issues);
        ValidateQueueTypeArgument(queue, path, issues);
        RegisterDeadLetterGeneratedNames(queue, namingPolicy, path, generatedNames, issues);
        ValidateRetryConfiguration(queue, namingPolicy, path, generatedNames, issues);
    }

    private static void ValidateQuorumQueueFlags(
        QueueDefinition queue,
        string path,
        ICollection<TopologyIssue> issues)
    {
        if (queue.Type != QueueType.Quorum || (!queue.Exclusive && !queue.AutoDelete))
        {
            return;
        }

        issues.Add(new TopologyIssue(
            "invalid-quorum-queue",
            "Quorum queues cannot be exclusive or auto-delete.",
            path,
            TopologyIssueSeverity.Error));
    }

    private static void ValidateQueueTypeArgument(
        QueueDefinition queue,
        string path,
        ICollection<TopologyIssue> issues)
    {
        if (!queue.Arguments.TryGetValue("x-queue-type", out var queueTypeArgument) ||
            queueTypeArgument is not string stringValue ||
            string.Equals(stringValue, queue.Type.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        issues.Add(new TopologyIssue(
            "queue-type-argument-mismatch",
            "Queue type conflicts with x-queue-type argument.",
            path,
            TopologyIssueSeverity.Error));
    }

    private static void RegisterDeadLetterGeneratedNames(
        QueueDefinition queue,
        NamingConventionPolicy namingPolicy,
        string path,
        ISet<string> generatedNames,
        ICollection<TopologyIssue> issues)
    {
        if (queue.DeadLetter is not { Enabled: true })
        {
            return;
        }

        RegisterGeneratedName(queue.DeadLetter.ExchangeName ?? namingPolicy.GetDeadLetterExchangeName(queue.Name), path, generatedNames, issues);
        RegisterGeneratedName(queue.DeadLetter.QueueName ?? namingPolicy.GetDeadLetterQueueName(queue.Name), path, generatedNames, issues);
    }

    private static void ValidateRetryConfiguration(
        QueueDefinition queue,
        NamingConventionPolicy namingPolicy,
        string path,
        ISet<string> generatedNames,
        ICollection<TopologyIssue> issues)
    {
        if (queue.Retry is not { Enabled: true } retry)
        {
            return;
        }

        if (queue.DeadLetter is not { Enabled: true })
        {
            issues.Add(new TopologyIssue(
                "retry-requires-dead-letter",
                "Retry configuration requires dead-letter to be enabled for the same queue.",
                path,
                TopologyIssueSeverity.Error));
        }

        ValidateRetrySteps(queue.Name, retry, namingPolicy, path, generatedNames, issues);
        RegisterGeneratedName(retry.ExchangeName ?? namingPolicy.GetRetryExchangeName(queue.Name), path, generatedNames, issues);
    }

    private static void ValidateRetrySteps(
        string queueName,
        RetryDefinition retry,
        NamingConventionPolicy namingPolicy,
        string path,
        ISet<string> generatedNames,
        ICollection<TopologyIssue> issues)
    {
        if (retry.Steps.Count == 0)
        {
            issues.Add(new TopologyIssue(
                "retry-missing-steps",
                "Retry configuration must declare at least one step when enabled.",
                path,
                TopologyIssueSeverity.Error));
        }

        var stepNames = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < retry.Steps.Count; index++)
        {
            var step = retry.Steps[index];
            var stepName = step.Name ?? namingPolicy.GetRetryStepToken(index);
            if (!stepNames.Add(stepName))
            {
                issues.Add(new TopologyIssue(
                    "duplicate-retry-step",
                    $"Retry step '{stepName}' is declared more than once.",
                    path,
                    TopologyIssueSeverity.Error));
            }

            RegisterGeneratedName(step.QueueName ?? namingPolicy.GetRetryQueueName(queueName, stepName), path, generatedNames, issues);
        }
    }

    private static void ValidateDuplicateNames(
        IEnumerable<string> names,
        string path,
        string resourceName,
        ICollection<TopologyIssue> issues)
    {
        var duplicateNames = names
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1);

        foreach (var group in duplicateNames)
        {
            issues.Add(new TopologyIssue(
                $"duplicate-{resourceName}",
                $"The {resourceName} '{group.Key}' is declared more than once.",
                path,
                TopologyIssueSeverity.Error));
        }
    }

    private static void RegisterGeneratedName(
        string generatedName,
        string path,
        ISet<string> generatedNames,
        ICollection<TopologyIssue> issues)
    {
        if (!generatedNames.Add(generatedName))
        {
            issues.Add(new TopologyIssue(
                "generated-name-collision",
                $"Generated artifact name '{generatedName}' collides with another generated artifact.",
                path,
                TopologyIssueSeverity.Error));
        }
    }
}
