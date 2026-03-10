using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime.Interfaces;

/// <summary>
/// Creates RabbitMQ-backed runtime services for a single CLI execution.
/// </summary>
public interface IRabbitMqRuntimeServiceFactory
{
    /// <summary>
    /// Creates a runtime service graph using the supplied management options.
    /// </summary>
    RabbitMqRuntimeServices Create(RabbitMqManagementOptions options);
}
