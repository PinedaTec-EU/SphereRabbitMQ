using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Startup.Interfaces;

internal interface IRuntimeTopologyYamlContractValidator
{
    void Validate(TopologyDefinition definition);
}
