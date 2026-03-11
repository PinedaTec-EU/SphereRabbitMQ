namespace SphereRabbitMQ.Abstractions.Topology;

public interface IRabbitMqTopologyValidator
{
    Task ValidateAsync(CancellationToken cancellationToken = default);
}
