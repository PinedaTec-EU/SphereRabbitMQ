using SphereRabbitMQ.IaC.Application.Apply.Interfaces;
using SphereRabbitMQ.IaC.Application.Broker.Interfaces;
using SphereRabbitMQ.IaC.Application.Export.Interfaces;
using SphereRabbitMQ.IaC.Application.Normalization.Interfaces;
using SphereRabbitMQ.IaC.Application.Parsing.Interfaces;
using SphereRabbitMQ.IaC.Application.Planning.Interfaces;
using SphereRabbitMQ.IaC.Application.Validation.Interfaces;
using SphereRabbitMQ.IaC.Application.Workflows;
using SphereRabbitMQ.IaC.Application.Workflows.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Apply;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Export;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Read;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime.Interfaces;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime;

/// <summary>
/// Default factory for RabbitMQ-backed runtime services.
/// </summary>
public sealed class RabbitMqRuntimeServiceFactory : IRabbitMqRuntimeServiceFactory
{
    private readonly ITopologyParser _topologyParser;
    private readonly ITopologyNormalizer _topologyNormalizer;
    private readonly ITopologyValidator _topologyValidator;
    private readonly ITopologyPlanner _topologyPlanner;
    private readonly ITopologyDestroyPlanner _topologyDestroyPlanner;

    public RabbitMqRuntimeServiceFactory(
        ITopologyParser topologyParser,
        ITopologyNormalizer topologyNormalizer,
        ITopologyValidator topologyValidator,
        ITopologyPlanner topologyPlanner,
        ITopologyDestroyPlanner topologyDestroyPlanner)
    {
        _topologyParser = topologyParser;
        _topologyNormalizer = topologyNormalizer;
        _topologyValidator = topologyValidator;
        _topologyPlanner = topologyPlanner;
        _topologyDestroyPlanner = topologyDestroyPlanner;
    }

    public RabbitMqRuntimeServices Create(RabbitMqManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var httpClient = new HttpClient();
        IRabbitMqManagementApiClient managementApiClient = new RabbitMqManagementApiClient(httpClient, options);
        IBrokerTopologyReader brokerTopologyReader = new RabbitMqManagementTopologyReader(managementApiClient, options);
        ITopologyApplier topologyApplier = new RabbitMqManagementTopologyApplier(
            managementApiClient,
            new RabbitMqRuntimeQueueMigrationMessageMover(options));
        ITopologyExporter topologyExporter = new RabbitMqManagementTopologyExporter(brokerTopologyReader);
        ITopologyWorkflowService topologyWorkflowService = new TopologyWorkflowService(
            _topologyParser,
            _topologyNormalizer,
            _topologyValidator,
            brokerTopologyReader,
            _topologyPlanner,
            _topologyDestroyPlanner,
            topologyApplier,
            topologyExporter);

        return new RabbitMqRuntimeServices(
            httpClient,
            options,
            managementApiClient,
            brokerTopologyReader,
            topologyApplier,
            topologyExporter,
            topologyWorkflowService);
    }
}
