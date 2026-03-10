using SphereRabbitMQ.IaC.Domain.Internal;

namespace SphereRabbitMQ.IaC.Domain.Topology;

/// <summary>
/// Defines all topology artifacts scoped to a RabbitMQ virtual host.
/// </summary>
public sealed record VirtualHostDefinition
{
    public VirtualHostDefinition(
        string name,
        IReadOnlyList<ExchangeDefinition>? exchanges = null,
        IReadOnlyList<QueueDefinition>? queues = null,
        IReadOnlyList<BindingDefinition>? bindings = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Name = Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Exchanges = exchanges ?? Array.Empty<ExchangeDefinition>();
        Queues = queues ?? Array.Empty<QueueDefinition>();
        Bindings = bindings ?? Array.Empty<BindingDefinition>();
        Metadata = metadata ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public string Name { get; }

    public IReadOnlyList<ExchangeDefinition> Exchanges { get; }

    public IReadOnlyList<QueueDefinition> Queues { get; }

    public IReadOnlyList<BindingDefinition> Bindings { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }
}
