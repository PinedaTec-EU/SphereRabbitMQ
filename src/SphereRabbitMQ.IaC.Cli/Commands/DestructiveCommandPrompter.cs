using SphereRabbitMQ.IaC.Cli.Commands.Interfaces;

namespace SphereRabbitMQ.IaC.Cli.Commands;

internal sealed class DestructiveCommandPrompter : IDestructiveCommandPrompter
{
    public Task<bool> ConfirmAsync(string operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException(
                $"Cannot prompt for confirmation for '{operation}' because stdin is redirected. Re-run with '--auto-approve' to execute non-interactively.");
        }

        Console.Write($"{operation} is destructive. Type 'yes' to continue: ");
        var response = Console.ReadLine();
        var confirmed = string.Equals(response?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(confirmed);
    }
}
