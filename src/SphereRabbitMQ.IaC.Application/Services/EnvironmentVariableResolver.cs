using System.Text.RegularExpressions;
using SphereRabbitMQ.IaC.Application.Abstractions;

namespace SphereRabbitMQ.IaC.Application.Services;

/// <summary>
/// Resolves <c>${NAME}</c> placeholders using explicit variables first and environment variables second.
/// </summary>
public sealed partial class EnvironmentVariableResolver : IVariableResolver
{
    [GeneratedRegex(@"\$\{(?<name>[A-Za-z0-9_\-\.]+)\}", RegexOptions.Compiled)]
    private static partial Regex VariablePattern();

    public string Resolve(
        string input,
        IReadOnlyDictionary<string, string?> variables,
        bool throwOnMissingVariable = true)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(variables);

        return VariablePattern().Replace(input, match =>
        {
            var name = match.Groups["name"].Value;
            if (variables.TryGetValue(name, out var value) && value is not null)
            {
                return value;
            }

            value = Environment.GetEnvironmentVariable(name);
            if (value is not null)
            {
                return value;
            }

            if (throwOnMissingVariable)
            {
                throw new InvalidOperationException($"Variable '{name}' was not provided.");
            }

            return match.Value;
        });
    }
}
