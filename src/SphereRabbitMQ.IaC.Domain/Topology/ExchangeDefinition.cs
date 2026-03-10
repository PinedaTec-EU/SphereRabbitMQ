using SphereRabbitMQ.IaC.Domain.Internal;

namespace SphereRabbitMQ.IaC.Domain.Topology;

/// <summary>
/// Defines an exchange within a RabbitMQ virtual host.
/// </summary>
public sealed record ExchangeDefinition
{
    public ExchangeDefinition(
        string name,
        ExchangeType type,
        bool durable = true,
        bool autoDelete = false,
        bool internalExchange = false,
        IReadOnlyDictionary<string, object?>? arguments = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Name = Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Type = type;
        Durable = durable;
        AutoDelete = autoDelete;
        Internal = internalExchange;
        Arguments = arguments ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        Metadata = metadata ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public string Name { get; }

    public ExchangeType Type { get; }

    public bool Durable { get; }

    public bool AutoDelete { get; }

    public bool Internal { get; }

    public IReadOnlyDictionary<string, object?> Arguments { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }
}
