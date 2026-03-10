using SphereRabbitMQ.IaC.Application.Abstractions;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Application.Services;

/// <summary>
/// Default topology validator that delegates semantic checks to the domain model.
/// </summary>
public sealed class TopologyValidationService : ITopologyValidator
{
    public ValueTask<TopologyValidationResult> ValidateAsync(
        TopologyDefinition definition,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(definition);

        return ValueTask.FromResult(definition.Validate());
    }
}
