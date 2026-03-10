using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Application.Normalization;

/// <summary>
/// Raised when a source document cannot be normalized into a valid topology model.
/// </summary>
public sealed class TopologyNormalizationException : Exception
{
    public TopologyNormalizationException(IReadOnlyList<TopologyIssue> issues)
        : base("Topology normalization failed.")
    {
        Issues = issues;
    }

    public IReadOnlyList<TopologyIssue> Issues { get; }
}
