using SphereRabbitMQ.IaC.Domain.Internal;

namespace SphereRabbitMQ.IaC.Domain.Topology;

/// <summary>
/// Defines explicit cleanup directives for a RabbitMQ virtual host.
/// </summary>
public sealed record DecommissionVirtualHostDefinition
{
    public DecommissionVirtualHostDefinition(
        string name,
        IReadOnlyList<string>? exchanges = null,
        IReadOnlyList<string>? queues = null,
        IReadOnlyList<BindingDefinition>? bindings = null)
    {
        Name = Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Exchanges = exchanges ?? Array.Empty<string>();
        Queues = queues ?? Array.Empty<string>();
        Bindings = bindings ?? Array.Empty<BindingDefinition>();
    }

    public string Name { get; }

    public IReadOnlyList<string> Exchanges { get; }

    public IReadOnlyList<string> Queues { get; }

    public IReadOnlyList<BindingDefinition> Bindings { get; }
}
