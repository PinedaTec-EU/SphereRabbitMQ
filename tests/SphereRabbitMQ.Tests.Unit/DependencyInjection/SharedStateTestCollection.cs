namespace SphereRabbitMQ.Tests.Unit.DependencyInjection;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SharedStateTestCollection
{
    public const string Name = "shared-state-tests";
}
