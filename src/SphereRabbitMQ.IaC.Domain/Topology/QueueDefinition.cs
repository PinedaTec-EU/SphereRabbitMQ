using SphereRabbitMQ.IaC.Domain.Internal;

namespace SphereRabbitMQ.IaC.Domain.Topology;

/// <summary>
/// Defines a queue within a RabbitMQ virtual host.
/// </summary>
public sealed record QueueDefinition
{
    public QueueDefinition(
        string name,
        QueueType type = QueueType.Classic,
        bool durable = true,
        bool exclusive = false,
        bool autoDelete = false,
        IReadOnlyDictionary<string, object?>? arguments = null,
        DeadLetterDefinition? deadLetter = null,
        RetryDefinition? retry = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Name = Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Type = type;
        Durable = durable;
        Exclusive = exclusive;
        AutoDelete = autoDelete;
        Arguments = arguments ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        DeadLetter = deadLetter;
        Retry = retry;
        Metadata = metadata ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public string Name { get; }

    public QueueType Type { get; }

    public bool Durable { get; }

    public bool Exclusive { get; }

    public bool AutoDelete { get; }

    public IReadOnlyDictionary<string, object?> Arguments { get; }

    public DeadLetterDefinition? DeadLetter { get; }

    public RetryDefinition? Retry { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }
}
