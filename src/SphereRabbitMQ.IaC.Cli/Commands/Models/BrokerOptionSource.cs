namespace SphereRabbitMQ.IaC.Cli.Commands.Models;

internal enum BrokerOptionSource
{
    CommandLine,
    Environment,
    Yaml,
    Default,
    Derived,
}
