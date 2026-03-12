using SphereRabbitMQ.Domain.Topology;

namespace SphereRabbitMQ.Abstractions.Topology;

public interface ISubscriberTopologyExpectationProvider
{
    TopologyExpectation BuildExpectation();
}
