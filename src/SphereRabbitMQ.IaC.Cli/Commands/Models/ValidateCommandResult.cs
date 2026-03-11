using System.Diagnostics.CodeAnalysis;

using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Cli.Commands.Models;

[ExcludeFromCodeCoverage]
internal sealed record ValidateCommandResult(TopologyValidationResult Validation);
