using SphereRabbitMQ.IaC.Domain.Internal;

namespace SphereRabbitMQ.IaC.Domain.Topology;

/// <summary>
/// Defines a binding from an exchange to a queue or another exchange.
/// </summary>
public sealed record BindingDefinition
{
    public BindingDefinition(
        string sourceExchange,
        string destination,
        BindingDestinationType destinationType = BindingDestinationType.Queue,
        string routingKey = "",
        IReadOnlyDictionary<string, object?>? arguments = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        SourceExchange = Guard.AgainstNullOrWhiteSpace(sourceExchange, nameof(sourceExchange));
        Destination = Guard.AgainstNullOrWhiteSpace(destination, nameof(destination));
        DestinationType = destinationType;
        RoutingKey = routingKey?.Trim() ?? string.Empty;
        Arguments = arguments ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        Metadata = metadata ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public string SourceExchange { get; }

    public string Destination { get; }

    public BindingDestinationType DestinationType { get; }

    public string RoutingKey { get; }

    public IReadOnlyDictionary<string, object?> Arguments { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    public string Key => $"{SourceExchange}|{DestinationType}|{Destination}|{RoutingKey}";
}
