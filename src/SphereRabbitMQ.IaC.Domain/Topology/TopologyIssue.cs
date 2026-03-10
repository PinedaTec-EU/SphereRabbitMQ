using SphereRabbitMQ.IaC.Domain.Internal;

namespace SphereRabbitMQ.IaC.Domain.Topology;

/// <summary>
/// Represents a validation or reconciliation issue tied to a topology path.
/// </summary>
public record TopologyIssue
{
    public TopologyIssue(string code, string message, string path, TopologyIssueSeverity severity)
    {
        Code = Guard.AgainstNullOrWhiteSpace(code, nameof(code));
        Message = Guard.AgainstNullOrWhiteSpace(message, nameof(message));
        Path = Guard.AgainstNullOrWhiteSpace(path, nameof(path));
        Severity = severity;
    }

    public string Code { get; }

    public string Message { get; }

    public string Path { get; }

    public TopologyIssueSeverity Severity { get; }
}
