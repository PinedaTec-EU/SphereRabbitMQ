namespace SphereRabbitMQ.Domain.Topology;

public sealed record TopologyExpectation(
    IReadOnlyCollection<string> Exchanges,
    IReadOnlyCollection<string> Queues);
