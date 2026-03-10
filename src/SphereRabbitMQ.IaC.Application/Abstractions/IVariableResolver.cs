namespace SphereRabbitMQ.IaC.Application.Abstractions;

/// <summary>
/// Resolves placeholders used inside topology source documents.
/// </summary>
public interface IVariableResolver
{
    /// <summary>
    /// Resolves variables in a single string value.
    /// </summary>
    string Resolve(
        string input,
        IReadOnlyDictionary<string, string?> variables,
        bool throwOnMissingVariable = true);
}
