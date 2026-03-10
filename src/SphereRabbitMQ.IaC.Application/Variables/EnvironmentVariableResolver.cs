using System.Text.RegularExpressions;
using SphereRabbitMQ.IaC.Application.Variables.Interfaces;

namespace SphereRabbitMQ.IaC.Application.Variables;

/// <summary>
/// Resolves <c>${NAME}</c> placeholders using explicit variables first and environment variables second.
/// </summary>
public sealed partial class EnvironmentVariableResolver : IVariableResolver
{
    [GeneratedRegex(@"\$\{(?<name>[A-Za-z0-9_\-\.]+)\}", RegexOptions.Compiled)]
    private static partial Regex VariablePattern();

    /// <inheritdoc />
    public string Resolve(
        string input,
        IReadOnlyDictionary<string, string?> variables,
        bool throwOnMissingVariable = true)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(variables);

        return VariablePattern().Replace(input, match => ResolveMatch(match, variables, throwOnMissingVariable));
    }

    private static string ResolveMatch(
        Match match,
        IReadOnlyDictionary<string, string?> variables,
        bool throwOnMissingVariable)
    {
        var variableName = match.Groups["name"].Value;
        if (variables.TryGetValue(variableName, out var explicitValue) && explicitValue is not null)
        {
            return explicitValue;
        }

        var environmentValue = Environment.GetEnvironmentVariable(variableName);
        if (environmentValue is not null)
        {
            return environmentValue;
        }

        if (throwOnMissingVariable)
        {
            throw new InvalidOperationException($"Variable '{variableName}' was not provided.");
        }

        return match.Value;
    }
}
