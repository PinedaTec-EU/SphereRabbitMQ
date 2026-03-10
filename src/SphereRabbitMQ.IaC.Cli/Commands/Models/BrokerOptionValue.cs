namespace SphereRabbitMQ.IaC.Cli.Commands.Models;

internal sealed record BrokerOptionValue<T>(
    T Value,
    BrokerOptionSource Source);
