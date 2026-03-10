namespace SphereRabbitMQ.IaC.Domain.Topology;

/// <summary>
/// Supported RabbitMQ exchange types.
/// </summary>
public enum ExchangeType
{
    Direct,
    Topic,
    Fanout,
    Headers,
}
