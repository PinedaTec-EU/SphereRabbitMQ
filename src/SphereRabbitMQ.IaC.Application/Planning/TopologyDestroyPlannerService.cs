using SphereRabbitMQ.IaC.Application.Planning.Interfaces;
using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Application.Planning;

/// <summary>
/// Produces deterministic destroy plans for the declared topology resources.
/// </summary>
public sealed class TopologyDestroyPlannerService : ITopologyDestroyPlanner
{
    /// <inheritdoc />
    public ValueTask<TopologyPlan> PlanAsync(
        TopologyDefinition desired,
        TopologyDefinition actual,
        bool destroyVirtualHosts,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(actual);

        var operations = new List<TopologyPlanOperation>();
        var actualVirtualHosts = actual.VirtualHosts.ToDictionary(vhost => vhost.Name, StringComparer.Ordinal);

        foreach (var desiredVirtualHost in desired.VirtualHosts)
        {
            if (!actualVirtualHosts.TryGetValue(desiredVirtualHost.Name, out var actualVirtualHost))
            {
                AppendMissingVirtualHostOperations(desiredVirtualHost, operations, destroyVirtualHosts);
                continue;
            }

            if (destroyVirtualHosts)
            {
                operations.Add(new TopologyPlanOperation(
                    TopologyPlanOperationKind.Destroy,
                    TopologyResourceKind.VirtualHost,
                    $"/virtualHosts/{desiredVirtualHost.Name}",
                    $"Delete virtual host '{desiredVirtualHost.Name}'."));
                continue;
            }

            AppendBindingOperations(desiredVirtualHost, actualVirtualHost, operations);
            AppendQueueOperations(desiredVirtualHost, actualVirtualHost, operations);
            AppendExchangeOperations(desiredVirtualHost, actualVirtualHost, operations);
        }

        var orderedOperations = operations
            .OrderBy(GetOperationOrder)
            .ThenBy(operation => operation.ResourcePath, StringComparer.Ordinal)
            .ToArray();

        return ValueTask.FromResult(new TopologyPlan(orderedOperations));
    }

    private static void AppendMissingVirtualHostOperations(
        VirtualHostDefinition desiredVirtualHost,
        ICollection<TopologyPlanOperation> operations,
        bool destroyVirtualHosts)
    {
        if (destroyVirtualHosts)
        {
            operations.Add(new TopologyPlanOperation(
                TopologyPlanOperationKind.NoOp,
                TopologyResourceKind.VirtualHost,
                $"/virtualHosts/{desiredVirtualHost.Name}",
                $"Virtual host '{desiredVirtualHost.Name}' is already absent."));
            return;
        }

        foreach (var binding in desiredVirtualHost.Bindings)
        {
            operations.Add(CreateNoOp(
                TopologyResourceKind.Binding,
                $"/virtualHosts/{desiredVirtualHost.Name}/bindings/{binding.Key}",
                $"Binding '{binding.Key}' is already absent."));
        }

        foreach (var queue in desiredVirtualHost.Queues)
        {
            operations.Add(CreateNoOp(
                TopologyResourceKind.Queue,
                $"/virtualHosts/{desiredVirtualHost.Name}/queues/{queue.Name}",
                $"Queue '{queue.Name}' is already absent."));
        }

        foreach (var exchange in desiredVirtualHost.Exchanges)
        {
            operations.Add(CreateNoOp(
                TopologyResourceKind.Exchange,
                $"/virtualHosts/{desiredVirtualHost.Name}/exchanges/{exchange.Name}",
                $"Exchange '{exchange.Name}' is already absent."));
        }
    }

    private static void AppendBindingOperations(
        VirtualHostDefinition desired,
        VirtualHostDefinition actual,
        ICollection<TopologyPlanOperation> operations)
    {
        var actualBindings = actual.Bindings.ToDictionary(binding => binding.Key, StringComparer.Ordinal);

        foreach (var desiredBinding in desired.Bindings)
        {
            var resourcePath = $"/virtualHosts/{desired.Name}/bindings/{desiredBinding.Key}";
            operations.Add(actualBindings.ContainsKey(desiredBinding.Key)
                ? new TopologyPlanOperation(
                    TopologyPlanOperationKind.Destroy,
                    TopologyResourceKind.Binding,
                    resourcePath,
                    DescribeDestroy("binding", desiredBinding.Metadata))
                : CreateNoOp(
                    TopologyResourceKind.Binding,
                    resourcePath,
                    $"Binding '{desiredBinding.Key}' is already absent."));
        }
    }

    private static void AppendQueueOperations(
        VirtualHostDefinition desired,
        VirtualHostDefinition actual,
        ICollection<TopologyPlanOperation> operations)
    {
        var actualQueues = actual.Queues.ToDictionary(queue => queue.Name, StringComparer.Ordinal);

        foreach (var desiredQueue in desired.Queues)
        {
            var resourcePath = $"/virtualHosts/{desired.Name}/queues/{desiredQueue.Name}";
            operations.Add(actualQueues.ContainsKey(desiredQueue.Name)
                ? new TopologyPlanOperation(
                    TopologyPlanOperationKind.Destroy,
                    TopologyResourceKind.Queue,
                    resourcePath,
                    DescribeDestroy("queue", desiredQueue.Metadata))
                : CreateNoOp(
                    TopologyResourceKind.Queue,
                    resourcePath,
                    $"Queue '{desiredQueue.Name}' is already absent."));
        }
    }

    private static void AppendExchangeOperations(
        VirtualHostDefinition desired,
        VirtualHostDefinition actual,
        ICollection<TopologyPlanOperation> operations)
    {
        var actualExchanges = actual.Exchanges.ToDictionary(exchange => exchange.Name, StringComparer.Ordinal);

        foreach (var desiredExchange in desired.Exchanges)
        {
            var resourcePath = $"/virtualHosts/{desired.Name}/exchanges/{desiredExchange.Name}";
            operations.Add(actualExchanges.ContainsKey(desiredExchange.Name)
                ? new TopologyPlanOperation(
                    TopologyPlanOperationKind.Destroy,
                    TopologyResourceKind.Exchange,
                    resourcePath,
                    DescribeDestroy("exchange", desiredExchange.Metadata))
                : CreateNoOp(
                    TopologyResourceKind.Exchange,
                    resourcePath,
                    $"Exchange '{desiredExchange.Name}' is already absent."));
        }
    }

    private static TopologyPlanOperation CreateNoOp(
        TopologyResourceKind resourceKind,
        string resourcePath,
        string description)
        => new(
            TopologyPlanOperationKind.NoOp,
            resourceKind,
            resourcePath,
            description);

    private static string DescribeDestroy(string resourceName, IReadOnlyDictionary<string, string> metadata)
        => IsGenerated(metadata)
            ? $"Delete generated {resourceName}."
            : $"Delete {resourceName}.";

    private static int GetOperationOrder(TopologyPlanOperation operation)
        => operation.ResourceKind switch
        {
            TopologyResourceKind.Binding => 0,
            TopologyResourceKind.Queue => 1,
            TopologyResourceKind.Exchange => 2,
            TopologyResourceKind.VirtualHost => 3,
            _ => 10,
        };

    private static bool IsGenerated(IReadOnlyDictionary<string, string> metadata)
        => metadata.TryGetValue(TopologyPlannerConsts.GeneratedMetadataKey, out var value) &&
           string.Equals(value, TopologyPlannerConsts.GeneratedMetadataValue, StringComparison.Ordinal);
}
