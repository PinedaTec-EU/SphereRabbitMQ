using SphereRabbitMQ.IaC.Application.Apply.Interfaces;
using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Interfaces;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Apply;

/// <summary>
/// Applies topology plans through the RabbitMQ Management HTTP API.
/// </summary>
public sealed class RabbitMqManagementTopologyApplier : ITopologyApplier
{
    private readonly IRabbitMqManagementApiClient _apiClient;

    /// <summary>
    /// Creates a new topology applier.
    /// </summary>
    public RabbitMqManagementTopologyApplier(IRabbitMqManagementApiClient apiClient)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        _apiClient = apiClient;
    }

    /// <inheritdoc />
    public async ValueTask ApplyAsync(
        TopologyDefinition desired,
        TopologyPlan plan,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.UnsupportedChanges.Count > 0 || plan.DestructiveChanges.Count > 0)
        {
            throw new InvalidOperationException("The execution plan contains unsupported or destructive changes and cannot be applied safely.");
        }

        foreach (var operation in plan.Operations.OrderBy(GetOperationOrder).ThenBy(operation => operation.ResourcePath, StringComparer.Ordinal))
        {
            if (operation.Kind is TopologyPlanOperationKind.NoOp)
            {
                continue;
            }

            await ApplyOperationAsync(desired, operation, cancellationToken);
        }
    }

    private async ValueTask ApplyOperationAsync(
        TopologyDefinition desired,
        TopologyPlanOperation operation,
        CancellationToken cancellationToken)
    {
        var pathSegments = operation.ResourcePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var virtualHostName = pathSegments[1];

        switch (operation.ResourceKind)
        {
            case TopologyResourceKind.VirtualHost:
                await _apiClient.CreateVirtualHostAsync(virtualHostName, cancellationToken);
                break;
            case TopologyResourceKind.Exchange:
                await _apiClient.UpsertExchangeAsync(virtualHostName, FindExchange(desired, virtualHostName, pathSegments[^1]), cancellationToken);
                break;
            case TopologyResourceKind.Queue:
                await _apiClient.UpsertQueueAsync(virtualHostName, FindQueue(desired, virtualHostName, pathSegments[^1]), cancellationToken);
                break;
            case TopologyResourceKind.Binding:
                var binding = FindBinding(desired, virtualHostName, pathSegments[^1]);
                if (operation.Kind == TopologyPlanOperationKind.Update)
                {
                    await _apiClient.RebindAsync(virtualHostName, binding, cancellationToken);
                }
                else
                {
                    await _apiClient.CreateBindingAsync(virtualHostName, binding, cancellationToken);
                }

                break;
            default:
                throw new InvalidOperationException($"Resource kind '{operation.ResourceKind}' is not supported.");
        }
    }

    private static int GetOperationOrder(TopologyPlanOperation operation)
        => operation.ResourceKind switch
        {
            TopologyResourceKind.VirtualHost => 0,
            TopologyResourceKind.Exchange => 1,
            TopologyResourceKind.Queue => 2,
            TopologyResourceKind.Binding => 3,
            _ => 10,
        };

    private static ExchangeDefinition FindExchange(TopologyDefinition desired, string virtualHostName, string exchangeName)
        => desired.VirtualHosts.Single(vhost => vhost.Name == virtualHostName).Exchanges.Single(exchange => exchange.Name == exchangeName);

    private static QueueDefinition FindQueue(TopologyDefinition desired, string virtualHostName, string queueName)
        => desired.VirtualHosts.Single(vhost => vhost.Name == virtualHostName).Queues.Single(queue => queue.Name == queueName);

    private static BindingDefinition FindBinding(TopologyDefinition desired, string virtualHostName, string bindingKey)
        => desired.VirtualHosts.Single(vhost => vhost.Name == virtualHostName).Bindings.Single(binding => binding.Key == bindingKey);
}
