using SphereRabbitMQ.IaC.Application.Apply;
using SphereRabbitMQ.IaC.Application.Apply.Interfaces;
using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Interfaces;
using System.Text.Json;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Apply;

/// <summary>
/// Applies topology plans through the RabbitMQ Management HTTP API.
/// </summary>
public sealed class RabbitMqManagementTopologyApplier : ITopologyApplier
{
    private const int LockAcquireDelayMilliseconds = 250;
    private const int LockAcquireMaxAttempts = 120;
    private const string MigrationLockQueueName = "sprmq.migration.lock";
    private const string MigrationLockTokenPayload = "lock-token";
    private const int MessageDrainBatchSize = 100;
    private const string DefaultExchangeName = "";
    private const string GeneratedByMetadataKey = "generated-by";
    private const string GeneratedByMetadataValue = "SphereRabbitMQ.IaC";
    private const string SourceExchangeMetadataKey = "source-exchange";
    private const string SourceQueueMetadataKey = "source-queue";

    private readonly IRabbitMqManagementApiClient _apiClient;
    private readonly IQueueMigrationMessageMover? _queueMessageMover;

    /// <summary>
    /// Creates a new topology applier.
    /// </summary>
    public RabbitMqManagementTopologyApplier(
        IRabbitMqManagementApiClient apiClient,
        IQueueMigrationMessageMover? queueMessageMover = null)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        _apiClient = apiClient;
        _queueMessageMover = queueMessageMover;
    }

    /// <inheritdoc />
    public async ValueTask ApplyAsync(
        TopologyDefinition desired,
        TopologyPlan plan,
        TopologyApplyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(plan);

        var applyOptions = options ?? TopologyApplyOptions.Safe;
        if (!applyOptions.AllowMigrations)
        {
            if (plan.UnsupportedChanges.Count > 0 || plan.DestructiveChanges.Count > 0)
            {
                throw new InvalidOperationException("The execution plan contains unsupported or destructive changes and cannot be applied safely.");
            }

            await ApplySafeOperationsAsync(desired, plan.Operations, cancellationToken);
            return;
        }

        var migrationPlan = BuildMigrationPlan(desired, plan);
        await ApplySafeOperationsAsync(desired, migrationPlan.SafeOperations, cancellationToken);

        foreach (var virtualHostName in migrationPlan.VirtualHostNames)
        {
            await ExecuteWithMigrationLockAsync(
                virtualHostName,
                async () =>
                {
                    foreach (var exchangeMigration in migrationPlan.ExchangeMigrations.Where(migration => migration.VirtualHostName == virtualHostName))
                    {
                        await MigrateExchangeAsync(desired, exchangeMigration.VirtualHostName, exchangeMigration.ExchangeName, cancellationToken);
                    }

                    foreach (var queueMigration in migrationPlan.QueueMigrations.Where(migration => migration.VirtualHostName == virtualHostName))
                    {
                        await MigrateQueueAsync(desired, queueMigration.VirtualHostName, queueMigration.QueueName, queueMigration.Mode, cancellationToken);
                    }
                },
                cancellationToken);
        }
    }

    private async ValueTask ApplySafeOperationsAsync(
        TopologyDefinition desired,
        IEnumerable<TopologyPlanOperation> operations,
        CancellationToken cancellationToken)
    {
        foreach (var operation in operations.OrderBy(GetOperationOrder).ThenBy(operation => operation.ResourcePath, StringComparer.Ordinal))
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

        if (operation.Kind == TopologyPlanOperationKind.Destroy)
        {
            await DestroyOperationAsync(desired, operation, virtualHostName, pathSegments, cancellationToken);
            return;
        }

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

    private async ValueTask DestroyOperationAsync(
        TopologyDefinition desired,
        TopologyPlanOperation operation,
        string virtualHostName,
        string[] pathSegments,
        CancellationToken cancellationToken)
    {
        switch (operation.ResourceKind)
        {
            case TopologyResourceKind.VirtualHost:
                await _apiClient.DeleteVirtualHostAsync(virtualHostName, cancellationToken);
                break;
            case TopologyResourceKind.Exchange:
                await _apiClient.DeleteExchangeAsync(virtualHostName, pathSegments[^1], cancellationToken);
                break;
            case TopologyResourceKind.Queue:
                await _apiClient.DeleteQueueAsync(virtualHostName, pathSegments[^1], cancellationToken);
                break;
            case TopologyResourceKind.Binding:
                await _apiClient.DeleteBindingAsync(virtualHostName, FindBinding(desired, virtualHostName, pathSegments[^1]), cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Resource kind '{operation.ResourceKind}' is not supported.");
        }
    }

    private async ValueTask MigrateExchangeAsync(
        TopologyDefinition desired,
        string virtualHostName,
        string exchangeName,
        CancellationToken cancellationToken)
    {
        var exchange = FindExchange(desired, virtualHostName, exchangeName);
        await _apiClient.DeleteExchangeAsync(virtualHostName, exchangeName, cancellationToken);
        await _apiClient.UpsertExchangeAsync(virtualHostName, exchange, cancellationToken);

        foreach (var binding in GetDesiredExchangeBindings(desired, virtualHostName, exchangeName))
        {
            await _apiClient.CreateBindingAsync(virtualHostName, binding, cancellationToken);
        }
    }

    private async ValueTask MigrateQueueAsync(
        TopologyDefinition desired,
        string virtualHostName,
        string queueName,
        QueueMigrationMode mode,
        CancellationToken cancellationToken)
    {
        switch (mode)
        {
            case QueueMigrationMode.Generated:
                await RecreateQueueAsync(desired, virtualHostName, queueName, cancellationToken);
                break;
            case QueueMigrationMode.Mainstream:
                await MigrateMainstreamQueueAsync(desired, virtualHostName, queueName, cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Queue migration mode '{mode}' is not supported.");
        }
    }

    private async ValueTask RecreateQueueAsync(
        TopologyDefinition desired,
        string virtualHostName,
        string queueName,
        CancellationToken cancellationToken)
    {
        await _apiClient.DeleteQueueAsync(virtualHostName, queueName, cancellationToken);
        await _apiClient.UpsertQueueAsync(virtualHostName, FindQueue(desired, virtualHostName, queueName), cancellationToken);

        foreach (var binding in GetDesiredQueueBindings(desired, virtualHostName, queueName))
        {
            await _apiClient.CreateBindingAsync(virtualHostName, binding, cancellationToken);
        }
    }

    private async ValueTask MigrateMainstreamQueueAsync(
        TopologyDefinition desired,
        string virtualHostName,
        string queueName,
        CancellationToken cancellationToken)
    {
        var desiredQueue = FindQueue(desired, virtualHostName, queueName);
        var desiredBindings = GetDesiredQueueBindings(desired, virtualHostName, queueName).ToArray();
        var temporaryQueue = CreateTemporaryQueueDefinition(queueName);
        var temporaryBindings = desiredBindings
            .Select(binding => new BindingDefinition(
                binding.SourceExchange,
                temporaryQueue.Name,
                binding.DestinationType,
                binding.RoutingKey,
                binding.Arguments,
                binding.Metadata))
            .ToArray();

        await _apiClient.DeleteQueueAsync(virtualHostName, temporaryQueue.Name, cancellationToken);
        await _apiClient.UpsertQueueAsync(virtualHostName, temporaryQueue, cancellationToken);

        foreach (var temporaryBinding in temporaryBindings)
        {
            await _apiClient.CreateBindingAsync(virtualHostName, temporaryBinding, cancellationToken);
        }

        foreach (var binding in desiredBindings)
        {
            await _apiClient.DeleteBindingAsync(virtualHostName, binding, cancellationToken);
        }

        await DrainQueueAsync(virtualHostName, queueName, temporaryQueue.Name, cancellationToken);
        await _apiClient.DeleteQueueAsync(virtualHostName, queueName, cancellationToken);
        await _apiClient.UpsertQueueAsync(virtualHostName, desiredQueue, cancellationToken);

        await DrainQueueAsync(virtualHostName, temporaryQueue.Name, desiredQueue.Name, cancellationToken);

        foreach (var temporaryBinding in temporaryBindings)
        {
            await _apiClient.DeleteBindingAsync(virtualHostName, temporaryBinding, cancellationToken);
        }

        foreach (var binding in desiredBindings)
        {
            await _apiClient.CreateBindingAsync(virtualHostName, binding, cancellationToken);
        }

        await _apiClient.DeleteQueueAsync(virtualHostName, temporaryQueue.Name, cancellationToken);
    }

    private async ValueTask DrainQueueAsync(
        string virtualHostName,
        string sourceQueueName,
        string destinationQueueName,
        CancellationToken cancellationToken)
    {
        if (_queueMessageMover is not null)
        {
            await _queueMessageMover.MoveAsync(sourceQueueName, destinationQueueName, cancellationToken);
            return;
        }

        while (true)
        {
            var messages = await _apiClient.GetMessagesAsync(virtualHostName, sourceQueueName, MessageDrainBatchSize, cancellationToken);
            if (messages.Count == 0)
            {
                return;
            }

            foreach (var message in messages)
            {
                await _apiClient.PublishMessageAsync(
                    virtualHostName,
                    DefaultExchangeName,
                    destinationQueueName,
                    message.Payload,
                    message.PayloadEncoding,
                    ConvertMessageProperties(message.Properties),
                    cancellationToken);
            }
        }
    }

    private async ValueTask ExecuteWithMigrationLockAsync(
        string virtualHostName,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        await AcquireMigrationLockAsync(virtualHostName, cancellationToken);

        try
        {
            await action();
        }
        finally
        {
            await ReleaseMigrationLockAsync(virtualHostName, cancellationToken);
        }
    }

    private async ValueTask AcquireMigrationLockAsync(string virtualHostName, CancellationToken cancellationToken)
    {
        await _apiClient.UpsertQueueAsync(virtualHostName, CreateMigrationLockQueueDefinition(), cancellationToken);

        for (var attempt = 0; attempt < LockAcquireMaxAttempts; attempt++)
        {
            try
            {
                await _apiClient.PublishMessageAsync(
                    virtualHostName,
                    DefaultExchangeName,
                    MigrationLockQueueName,
                    MigrationLockTokenPayload,
                    "string",
                    null,
                    cancellationToken);
                return;
            }
            catch (HttpRequestException)
            {
                await Task.Delay(LockAcquireDelayMilliseconds, cancellationToken);
            }
        }

        throw new TimeoutException($"Unable to acquire the migration lock queue '{MigrationLockQueueName}' in virtual host '{virtualHostName}'.");
    }

    private async ValueTask ReleaseMigrationLockAsync(string virtualHostName, CancellationToken cancellationToken)
    {
        await _apiClient.GetMessagesAsync(virtualHostName, MigrationLockQueueName, 1, cancellationToken);
    }

    private static MigrationPlan BuildMigrationPlan(TopologyDefinition desired, TopologyPlan plan)
    {
        var exchangeMigrations = new List<ExchangeMigration>();
        var queueMigrations = new List<QueueMigration>();
        var safeOperations = new List<TopologyPlanOperation>();

        foreach (var operation in plan.Operations)
        {
            if (TryCreateExchangeMigration(desired, operation, out var exchangeMigration))
            {
                exchangeMigrations.Add(exchangeMigration);
                continue;
            }

            if (TryCreateQueueMigration(desired, operation, out var queueMigration))
            {
                queueMigrations.Add(queueMigration);
                continue;
            }

            if (operation.Kind is TopologyPlanOperationKind.DestructiveChange or TopologyPlanOperationKind.UnsupportedChange)
            {
                throw new InvalidOperationException($"The execution plan contains blocking changes that cannot be migrated automatically: '{operation.ResourcePath}'.");
            }

            safeOperations.Add(operation);
        }

        return new MigrationPlan(safeOperations, exchangeMigrations, queueMigrations);
    }

    private static bool TryCreateExchangeMigration(
        TopologyDefinition desired,
        TopologyPlanOperation operation,
        out ExchangeMigration migration)
    {
        migration = null!;
        if (operation.Kind != TopologyPlanOperationKind.UnsupportedChange || operation.ResourceKind != TopologyResourceKind.Exchange)
        {
            return false;
        }

        var pathSegments = operation.ResourcePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var virtualHostName = pathSegments[1];
        var exchangeName = pathSegments[^1];
        if (!HasDesiredExchange(desired, virtualHostName, exchangeName))
        {
            return false;
        }

        migration = new ExchangeMigration(virtualHostName, exchangeName);
        return true;
    }

    private static bool TryCreateQueueMigration(
        TopologyDefinition desired,
        TopologyPlanOperation operation,
        out QueueMigration migration)
    {
        migration = null!;
        if (operation.Kind != TopologyPlanOperationKind.DestructiveChange || operation.ResourceKind != TopologyResourceKind.Queue)
        {
            return false;
        }

        var pathSegments = operation.ResourcePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var virtualHostName = pathSegments[1];
        var queueName = pathSegments[^1];
        if (!HasDesiredQueue(desired, virtualHostName, queueName))
        {
            return false;
        }

        var desiredQueue = FindQueue(desired, virtualHostName, queueName);
        migration = new QueueMigration(virtualHostName, queueName, IsGeneratedQueue(desiredQueue) ? QueueMigrationMode.Generated : QueueMigrationMode.Mainstream);
        return true;
    }

    private static bool HasDesiredExchange(TopologyDefinition desired, string virtualHostName, string exchangeName)
        => desired.VirtualHosts.Any(vhost =>
            string.Equals(vhost.Name, virtualHostName, StringComparison.Ordinal) &&
            vhost.Exchanges.Any(exchange => string.Equals(exchange.Name, exchangeName, StringComparison.Ordinal)));

    private static bool HasDesiredQueue(TopologyDefinition desired, string virtualHostName, string queueName)
        => desired.VirtualHosts.Any(vhost =>
            string.Equals(vhost.Name, virtualHostName, StringComparison.Ordinal) &&
            vhost.Queues.Any(queue => string.Equals(queue.Name, queueName, StringComparison.Ordinal)));

    private static IEnumerable<BindingDefinition> GetDesiredExchangeBindings(TopologyDefinition desired, string virtualHostName, string exchangeName)
        => desired.VirtualHosts
            .Single(vhost => vhost.Name == virtualHostName)
            .Bindings
            .Where(binding =>
                string.Equals(binding.SourceExchange, exchangeName, StringComparison.Ordinal) ||
                (binding.DestinationType == BindingDestinationType.Exchange &&
                 string.Equals(binding.Destination, exchangeName, StringComparison.Ordinal)))
            .OrderBy(binding => binding.Key, StringComparer.Ordinal);

    private static IEnumerable<BindingDefinition> GetDesiredQueueBindings(TopologyDefinition desired, string virtualHostName, string queueName)
        => desired.VirtualHosts
            .Single(vhost => vhost.Name == virtualHostName)
            .Bindings
            .Where(binding =>
                binding.DestinationType == BindingDestinationType.Queue &&
                string.Equals(binding.Destination, queueName, StringComparison.Ordinal))
            .OrderBy(binding => binding.Key, StringComparer.Ordinal);

    private static QueueDefinition CreateTemporaryQueueDefinition(string queueName)
        => new(
            $"{queueName}.sprmq-migration-temp",
            QueueType.Classic,
            true,
            false,
            false,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["x-queue-type"] = "classic",
            });

    private static QueueDefinition CreateMigrationLockQueueDefinition()
        => new(
            MigrationLockQueueName,
            QueueType.Classic,
            true,
            false,
            false,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["x-queue-type"] = "classic",
                ["x-max-length"] = 1,
                ["x-overflow"] = "reject-publish",
            });

    private static bool IsGeneratedQueue(QueueDefinition queue)
        => queue.Metadata.TryGetValue(GeneratedByMetadataKey, out var generatedBy) &&
           string.Equals(generatedBy, GeneratedByMetadataValue, StringComparison.Ordinal) &&
           (queue.Metadata.ContainsKey(SourceExchangeMetadataKey) || queue.Metadata.ContainsKey(SourceQueueMetadataKey));

    private static IReadOnlyDictionary<string, object?> ConvertMessageProperties(JsonElement properties)
        => properties.ValueKind == JsonValueKind.Object
            ? properties.EnumerateObject().ToDictionary(property => property.Name, property => ConvertJsonElement(property.Value), StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);

    private static object? ConvertJsonElement(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.False => false,
            JsonValueKind.True => true,
            JsonValueKind.Number when element.TryGetInt64(out var int64Value) => int64Value,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToArray(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(property => property.Name, property => ConvertJsonElement(property.Value), StringComparer.Ordinal),
            _ => element.GetRawText(),
        };

    private static int GetOperationOrder(TopologyPlanOperation operation)
        => operation.Kind == TopologyPlanOperationKind.Destroy
            ? GetDestroyOperationOrder(operation)
            : operation.ResourceKind switch
            {
                TopologyResourceKind.VirtualHost => 0,
                TopologyResourceKind.Exchange => 1,
                TopologyResourceKind.Queue => 2,
                TopologyResourceKind.Binding => 3,
                _ => 10,
            };

    private static int GetDestroyOperationOrder(TopologyPlanOperation operation)
        => operation.ResourceKind switch
        {
            TopologyResourceKind.Binding => 0,
            TopologyResourceKind.Queue => 1,
            TopologyResourceKind.Exchange => 2,
            TopologyResourceKind.VirtualHost => 3,
            _ => 10,
        };

    private static ExchangeDefinition FindExchange(TopologyDefinition desired, string virtualHostName, string exchangeName)
        => desired.VirtualHosts.Single(vhost => vhost.Name == virtualHostName).Exchanges.Single(exchange => exchange.Name == exchangeName);

    private static QueueDefinition FindQueue(TopologyDefinition desired, string virtualHostName, string queueName)
        => desired.VirtualHosts.Single(vhost => vhost.Name == virtualHostName).Queues.Single(queue => queue.Name == queueName);

    private static BindingDefinition FindBinding(TopologyDefinition desired, string virtualHostName, string bindingKey)
        => desired.VirtualHosts.Single(vhost => vhost.Name == virtualHostName).Bindings.Single(binding => binding.Key == bindingKey);

    private sealed record MigrationPlan(
        IReadOnlyList<TopologyPlanOperation> SafeOperations,
        IReadOnlyList<ExchangeMigration> ExchangeMigrations,
        IReadOnlyList<QueueMigration> QueueMigrations)
    {
        public IReadOnlyList<string> VirtualHostNames { get; } = ExchangeMigrations
            .Select(migration => migration.VirtualHostName)
            .Concat(QueueMigrations.Select(migration => migration.VirtualHostName))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed record ExchangeMigration(string VirtualHostName, string ExchangeName);

    private sealed record QueueMigration(string VirtualHostName, string QueueName, QueueMigrationMode Mode);

    private enum QueueMigrationMode
    {
        Generated,
        Mainstream,
    }
}
