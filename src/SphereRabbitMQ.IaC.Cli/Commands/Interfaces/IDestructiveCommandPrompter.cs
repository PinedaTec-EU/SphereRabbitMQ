namespace SphereRabbitMQ.IaC.Cli.Commands.Interfaces;

internal interface IDestructiveCommandPrompter
{
    Task<bool> ConfirmAsync(string operation, CancellationToken cancellationToken);
}
