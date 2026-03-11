using System.Diagnostics.CodeAnalysis;

using SphereRabbitMQ.IaC.Application.Models;

namespace SphereRabbitMQ.IaC.Cli.Commands.Models;

[ExcludeFromCodeCoverage]
internal sealed record ExportCommandResult(
    BrokerResolutionResult Broker,
    string Content,
    TopologyDocument Document);
