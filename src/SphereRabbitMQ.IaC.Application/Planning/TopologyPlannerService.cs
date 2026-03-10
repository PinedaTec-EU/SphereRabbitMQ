using System.Text.Json;
using System.Text.Json.Nodes;
using SphereRabbitMQ.IaC.Application.Planning.Interfaces;
using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Application.Planning;

/// <summary>
/// Produces deterministic topology plans by comparing desired and actual broker state.
/// </summary>
public sealed class TopologyPlannerService : ITopologyPlanner
{
    /// <inheritdoc />
    public ValueTask<TopologyPlan> PlanAsync(
        TopologyDefinition desired,
        TopologyDefinition actual,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(actual);

        var operations = new List<TopologyPlanOperation>();
        var unsupportedChanges = new List<UnsupportedChange>();
        var destructiveChanges = new List<DestructiveChangeWarning>();

        CompareVirtualHosts(desired, actual, operations, unsupportedChanges, destructiveChanges);

        var orderedOperations = operations
            .OrderBy(operation => operation.ResourcePath, StringComparer.Ordinal)
            .ThenBy(operation => operation.Kind)
            .ToArray();

        return ValueTask.FromResult(new TopologyPlan(orderedOperations, unsupportedChanges.ToArray(), destructiveChanges.ToArray()));
    }

    private static void CompareVirtualHosts(
        TopologyDefinition desired,
        TopologyDefinition actual,
        ICollection<TopologyPlanOperation> operations,
        ICollection<UnsupportedChange> unsupportedChanges,
        ICollection<DestructiveChangeWarning> destructiveChanges)
    {
        var actualVirtualHosts = actual.VirtualHosts.ToDictionary(vhost => vhost.Name, StringComparer.Ordinal);

        foreach (var desiredVirtualHost in desired.VirtualHosts)
        {
            if (!actualVirtualHosts.TryGetValue(desiredVirtualHost.Name, out var actualVirtualHost))
            {
                AppendCreateOperationsForMissingVirtualHost(desiredVirtualHost, operations);
                continue;
            }

            CompareExchanges(desiredVirtualHost, actualVirtualHost, operations, unsupportedChanges, destructiveChanges);
            CompareQueues(desiredVirtualHost, actualVirtualHost, operations, unsupportedChanges, destructiveChanges);
            CompareBindings(desiredVirtualHost, actualVirtualHost, operations, destructiveChanges);
        }

        foreach (var actualVirtualHost in actual.VirtualHosts.Where(vhost => desired.VirtualHosts.All(desiredVHost => desiredVHost.Name != vhost.Name)))
        {
            var resourcePath = $"/virtualHosts/{actualVirtualHost.Name}";
            var warning = new DestructiveChangeWarning(resourcePath, $"Virtual host '{actualVirtualHost.Name}' exists on the broker but not in the desired topology.");
            destructiveChanges.Add(warning);
            operations.Add(CreateIssueOperation(TopologyResourceKind.VirtualHost, resourcePath, TopologyPlanOperationKind.DestructiveChange, warning.Reason, warning));
        }
    }

    private static void AppendCreateOperationsForMissingVirtualHost(
        VirtualHostDefinition virtualHost,
        ICollection<TopologyPlanOperation> operations)
    {
        operations.Add(new TopologyPlanOperation(
            TopologyPlanOperationKind.Create,
            TopologyResourceKind.VirtualHost,
            $"/virtualHosts/{virtualHost.Name}",
            $"Create virtual host '{virtualHost.Name}'."));

        foreach (var exchange in virtualHost.Exchanges)
        {
            operations.Add(new TopologyPlanOperation(
                TopologyPlanOperationKind.Create,
                TopologyResourceKind.Exchange,
                $"/virtualHosts/{virtualHost.Name}/exchanges/{exchange.Name}",
                DescribeCreate("exchange", exchange.Metadata)));
        }

        foreach (var queue in virtualHost.Queues)
        {
            operations.Add(new TopologyPlanOperation(
                TopologyPlanOperationKind.Create,
                TopologyResourceKind.Queue,
                $"/virtualHosts/{virtualHost.Name}/queues/{queue.Name}",
                DescribeCreate("queue", queue.Metadata)));
        }

        foreach (var binding in virtualHost.Bindings)
        {
            operations.Add(new TopologyPlanOperation(
                TopologyPlanOperationKind.Create,
                TopologyResourceKind.Binding,
                $"/virtualHosts/{virtualHost.Name}/bindings/{binding.Key}",
                DescribeCreate("binding", binding.Metadata)));
        }
    }

    private static void CompareExchanges(
        VirtualHostDefinition desired,
        VirtualHostDefinition actual,
        ICollection<TopologyPlanOperation> operations,
        ICollection<UnsupportedChange> unsupportedChanges,
        ICollection<DestructiveChangeWarning> destructiveChanges)
    {
        var actualExchanges = actual.Exchanges.ToDictionary(exchange => exchange.Name, StringComparer.Ordinal);
        foreach (var desiredExchange in desired.Exchanges)
        {
            var resourcePath = $"/virtualHosts/{desired.Name}/exchanges/{desiredExchange.Name}";
            if (!actualExchanges.TryGetValue(desiredExchange.Name, out var actualExchange))
            {
                operations.Add(new TopologyPlanOperation(
                    TopologyPlanOperationKind.Create,
                    TopologyResourceKind.Exchange,
                    resourcePath,
                    DescribeCreate("exchange", desiredExchange.Metadata)));
                continue;
            }

            var diffs = BuildExchangeDiffs(desiredExchange, actualExchange, resourcePath);
            if (diffs.Count == 0)
            {
                operations.Add(new TopologyPlanOperation(
                    TopologyPlanOperationKind.NoOp,
                    TopologyResourceKind.Exchange,
                    resourcePath,
                    $"Exchange '{desiredExchange.Name}' is up to date."));
                continue;
            }

            var unsupportedChange = new UnsupportedChange(resourcePath, $"Exchange '{desiredExchange.Name}' requires redeclaration because immutable properties changed.");
            unsupportedChanges.Add(unsupportedChange);
            operations.Add(CreateIssueOperation(
                TopologyResourceKind.Exchange,
                resourcePath,
                TopologyPlanOperationKind.UnsupportedChange,
                unsupportedChange.Reason,
                unsupportedChange,
                diffs));
        }

        foreach (var actualExchange in actual.Exchanges.Where(exchange => desired.Exchanges.All(desiredExchange => desiredExchange.Name != exchange.Name)))
        {
            AddDestructiveOperation(
                operations,
                destructiveChanges,
                TopologyResourceKind.Exchange,
                $"/virtualHosts/{desired.Name}/exchanges/{actualExchange.Name}",
                $"Exchange '{actualExchange.Name}' exists on the broker but not in the desired topology.");
        }
    }

    private static void CompareQueues(
        VirtualHostDefinition desired,
        VirtualHostDefinition actual,
        ICollection<TopologyPlanOperation> operations,
        ICollection<UnsupportedChange> unsupportedChanges,
        ICollection<DestructiveChangeWarning> destructiveChanges)
    {
        var actualQueues = actual.Queues.ToDictionary(queue => queue.Name, StringComparer.Ordinal);
        foreach (var desiredQueue in desired.Queues)
        {
            var resourcePath = $"/virtualHosts/{desired.Name}/queues/{desiredQueue.Name}";
            if (!actualQueues.TryGetValue(desiredQueue.Name, out var actualQueue))
            {
                operations.Add(new TopologyPlanOperation(
                    TopologyPlanOperationKind.Create,
                    TopologyResourceKind.Queue,
                    resourcePath,
                    DescribeCreate("queue", desiredQueue.Metadata)));
                continue;
            }

            var diffs = BuildQueueDiffs(desiredQueue, actualQueue, resourcePath);
            if (diffs.Count == 0)
            {
                operations.Add(new TopologyPlanOperation(
                    TopologyPlanOperationKind.NoOp,
                    TopologyResourceKind.Queue,
                    resourcePath,
                    $"Queue '{desiredQueue.Name}' is up to date."));
                continue;
            }

            if (HasImmutableQueueChange(diffs))
            {
                AddDestructiveOperation(
                    operations,
                    destructiveChanges,
                    TopologyResourceKind.Queue,
                    resourcePath,
                    $"Queue '{desiredQueue.Name}' requires delete/recreate because immutable properties changed.",
                    diffs);
                continue;
            }

            var unsupportedChange = new UnsupportedChange(resourcePath, $"Queue '{desiredQueue.Name}' contains changes that are not safely reconcilable in place.");
            unsupportedChanges.Add(unsupportedChange);
            operations.Add(CreateIssueOperation(
                TopologyResourceKind.Queue,
                resourcePath,
                TopologyPlanOperationKind.UnsupportedChange,
                unsupportedChange.Reason,
                unsupportedChange,
                diffs));
        }

        foreach (var actualQueue in actual.Queues.Where(queue => desired.Queues.All(desiredQueue => desiredQueue.Name != queue.Name)))
        {
            AddDestructiveOperation(
                operations,
                destructiveChanges,
                TopologyResourceKind.Queue,
                $"/virtualHosts/{desired.Name}/queues/{actualQueue.Name}",
                $"Queue '{actualQueue.Name}' exists on the broker but not in the desired topology.");
        }
    }

    private static void CompareBindings(
        VirtualHostDefinition desired,
        VirtualHostDefinition actual,
        ICollection<TopologyPlanOperation> operations,
        ICollection<DestructiveChangeWarning> destructiveChanges)
    {
        var actualBindings = actual.Bindings.ToDictionary(binding => binding.Key, StringComparer.Ordinal);
        foreach (var desiredBinding in desired.Bindings)
        {
            var resourcePath = $"/virtualHosts/{desired.Name}/bindings/{desiredBinding.Key}";
            if (!actualBindings.TryGetValue(desiredBinding.Key, out var actualBinding))
            {
                operations.Add(new TopologyPlanOperation(
                    TopologyPlanOperationKind.Create,
                    TopologyResourceKind.Binding,
                    resourcePath,
                    DescribeCreate("binding", desiredBinding.Metadata)));
                continue;
            }

            var diffs = BuildBindingDiffs(desiredBinding, actualBinding, resourcePath);
            operations.Add(new TopologyPlanOperation(
                diffs.Count == 0 ? TopologyPlanOperationKind.NoOp : TopologyPlanOperationKind.Update,
                TopologyResourceKind.Binding,
                resourcePath,
                diffs.Count == 0
                    ? $"Binding '{desiredBinding.Key}' is up to date."
                    : $"Binding '{desiredBinding.Key}' requires rebind to reconcile arguments.",
                diffs));
        }

        foreach (var actualBinding in actual.Bindings.Where(binding => desired.Bindings.All(desiredBinding => desiredBinding.Key != binding.Key)))
        {
            AddDestructiveOperation(
                operations,
                destructiveChanges,
                TopologyResourceKind.Binding,
                $"/virtualHosts/{desired.Name}/bindings/{actualBinding.Key}",
                $"Binding '{actualBinding.Key}' exists on the broker but not in the desired topology.");
        }
    }

    private static List<TopologyDiff> BuildExchangeDiffs(ExchangeDefinition desired, ExchangeDefinition actual, string resourcePath)
    {
        var diffs = new List<TopologyDiff>();
        AddDiffIfChanged(diffs, resourcePath, TopologyResourceKind.Exchange, "type", desired.Type.ToString(), actual.Type.ToString());
        AddDiffIfChanged(diffs, resourcePath, TopologyResourceKind.Exchange, "durable", desired.Durable.ToString(), actual.Durable.ToString());
        AddDiffIfChanged(diffs, resourcePath, TopologyResourceKind.Exchange, "autoDelete", desired.AutoDelete.ToString(), actual.AutoDelete.ToString());
        AddDiffIfChanged(diffs, resourcePath, TopologyResourceKind.Exchange, "internal", desired.Internal.ToString(), actual.Internal.ToString());
        AddDiffIfChanged(diffs, resourcePath, TopologyResourceKind.Exchange, "arguments", Serialize(desired.Arguments), Serialize(actual.Arguments));
        return diffs;
    }

    private static List<TopologyDiff> BuildQueueDiffs(QueueDefinition desired, QueueDefinition actual, string resourcePath)
    {
        var diffs = new List<TopologyDiff>();
        AddDiffIfChanged(diffs, resourcePath, TopologyResourceKind.Queue, "type", desired.Type.ToString(), actual.Type.ToString());
        AddDiffIfChanged(diffs, resourcePath, TopologyResourceKind.Queue, "durable", desired.Durable.ToString(), actual.Durable.ToString());
        AddDiffIfChanged(diffs, resourcePath, TopologyResourceKind.Queue, "exclusive", desired.Exclusive.ToString(), actual.Exclusive.ToString());
        AddDiffIfChanged(diffs, resourcePath, TopologyResourceKind.Queue, "autoDelete", desired.AutoDelete.ToString(), actual.AutoDelete.ToString());
        AddDiffIfChanged(diffs, resourcePath, TopologyResourceKind.Queue, "arguments", Serialize(desired.Arguments), Serialize(actual.Arguments));
        return diffs;
    }

    private static List<TopologyDiff> BuildBindingDiffs(BindingDefinition desired, BindingDefinition actual, string resourcePath)
    {
        var diffs = new List<TopologyDiff>();
        AddDiffIfChanged(diffs, resourcePath, TopologyResourceKind.Binding, "arguments", Serialize(desired.Arguments), Serialize(actual.Arguments));
        return diffs;
    }

    private static bool HasImmutableQueueChange(IEnumerable<TopologyDiff> diffs)
        => diffs.Any(diff =>
            diff.PropertyName is "type" or "durable" or "exclusive" or "autoDelete" or "arguments");

    private static void AddDiffIfChanged(
        ICollection<TopologyDiff> diffs,
        string resourcePath,
        TopologyResourceKind resourceKind,
        string propertyName,
        string? desiredValue,
        string? actualValue)
    {
        if (!string.Equals(desiredValue, actualValue, StringComparison.Ordinal))
        {
            diffs.Add(new TopologyDiff(resourcePath, resourceKind, propertyName, desiredValue, actualValue));
        }
    }

    private static string DescribeCreate(string resourceName, IReadOnlyDictionary<string, string> metadata)
        => IsGenerated(metadata)
            ? $"Create generated {resourceName}."
            : $"Create {resourceName}.";

    private static bool IsGenerated(IReadOnlyDictionary<string, string> metadata)
        => metadata.TryGetValue(TopologyPlannerConsts.GeneratedMetadataKey, out var value) &&
           string.Equals(value, TopologyPlannerConsts.GeneratedMetadataValue, StringComparison.Ordinal);

    private static TopologyPlanOperation CreateIssueOperation(
        TopologyResourceKind resourceKind,
        string resourcePath,
        TopologyPlanOperationKind operationKind,
        string description,
        TopologyIssue issue,
        IReadOnlyList<TopologyDiff>? diffs = null)
        => new(operationKind, resourceKind, resourcePath, description, diffs, [issue]);

    private static void AddDestructiveOperation(
        ICollection<TopologyPlanOperation> operations,
        ICollection<DestructiveChangeWarning> destructiveChanges,
        TopologyResourceKind resourceKind,
        string resourcePath,
        string reason,
        IReadOnlyList<TopologyDiff>? diffs = null)
    {
        var warning = new DestructiveChangeWarning(resourcePath, reason);
        destructiveChanges.Add(warning);
        operations.Add(CreateIssueOperation(resourceKind, resourcePath, TopologyPlanOperationKind.DestructiveChange, reason, warning, diffs));
    }

    private static string Serialize<T>(T value)
        => CanonicalizeNode(JsonSerializer.SerializeToNode(value))?.ToJsonString() ?? "null";

    private static JsonNode? CanonicalizeNode(JsonNode? node)
        => node switch
        {
            null => null,
            JsonObject jsonObject => CanonicalizeObject(jsonObject),
            JsonArray jsonArray => CanonicalizeArray(jsonArray),
            _ => node.DeepClone(),
        };

    private static JsonObject CanonicalizeObject(JsonObject jsonObject)
    {
        var normalized = new JsonObject();

        foreach (var property in jsonObject.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            normalized[property.Key] = CanonicalizeNode(property.Value);
        }

        return normalized;
    }

    private static JsonArray CanonicalizeArray(JsonArray jsonArray)
    {
        var normalized = new JsonArray();

        foreach (var item in jsonArray)
        {
            normalized.Add(CanonicalizeNode(item));
        }

        return normalized;
    }
}
