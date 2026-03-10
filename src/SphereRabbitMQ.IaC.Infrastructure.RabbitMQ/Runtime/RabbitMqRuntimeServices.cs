using SphereRabbitMQ.IaC.Application.Apply.Interfaces;
using SphereRabbitMQ.IaC.Application.Broker.Interfaces;
using SphereRabbitMQ.IaC.Application.Export.Interfaces;
using SphereRabbitMQ.IaC.Application.Planning.Interfaces;
using SphereRabbitMQ.IaC.Application.Workflows;
using SphereRabbitMQ.IaC.Application.Workflows.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Apply;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Export;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Read;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime;

/// <summary>
/// Runtime service graph for a RabbitMQ-backed topology workflow execution.
/// </summary>
public sealed class RabbitMqRuntimeServices : IDisposable
{
    public RabbitMqRuntimeServices(
        HttpClient httpClient,
        RabbitMqManagementOptions options,
        IRabbitMqManagementApiClient managementApiClient,
        IBrokerTopologyReader brokerTopologyReader,
        ITopologyApplier topologyApplier,
        ITopologyExporter topologyExporter,
        ITopologyWorkflowService topologyWorkflowService)
    {
        HttpClient = httpClient;
        Options = options;
        ManagementApiClient = managementApiClient;
        BrokerTopologyReader = brokerTopologyReader;
        TopologyApplier = topologyApplier;
        TopologyExporter = topologyExporter;
        TopologyWorkflowService = topologyWorkflowService;
    }

    public HttpClient HttpClient { get; }

    public RabbitMqManagementOptions Options { get; }

    public IRabbitMqManagementApiClient ManagementApiClient { get; }

    public IBrokerTopologyReader BrokerTopologyReader { get; }

    public ITopologyApplier TopologyApplier { get; }

    public ITopologyExporter TopologyExporter { get; }

    public ITopologyWorkflowService TopologyWorkflowService { get; }

    public void Dispose()
    {
        HttpClient.Dispose();
    }
}
