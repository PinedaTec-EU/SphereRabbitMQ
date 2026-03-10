using SphereRabbitMQ.IaC.Application.Apply.Interfaces;
using SphereRabbitMQ.IaC.Application.Broker.Interfaces;
using SphereRabbitMQ.IaC.Application.Export.Interfaces;
using SphereRabbitMQ.IaC.Application.Planning;
using SphereRabbitMQ.IaC.Application.Planning.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Apply;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Export;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Read;

namespace SphereRabbitMQ.IaC.Tests.Integration;

public sealed class RabbitMqManagementIntegrationFixture : IAsyncLifetime, IDisposable
{
    private const string VirtualHostPrefix = "sphere-iac-it";

    public string VirtualHostName { get; }

    public RabbitMqManagementOptions Options { get; }

    public IRabbitMqManagementApiClient ApiClient { get; }

    public IBrokerTopologyReader BrokerTopologyReader { get; }

    public ITopologyApplier TopologyApplier { get; }

    public ITopologyExporter TopologyExporter { get; }

    public ITopologyPlanner TopologyPlanner { get; }

    public ITopologyDestroyPlanner TopologyDestroyPlanner { get; }

    private readonly HttpClient _httpClient;

    private readonly bool _isConfigured;

    public RabbitMqManagementIntegrationFixture()
    {
        _httpClient = new HttpClient();
        VirtualHostName = $"{VirtualHostPrefix}-{Guid.NewGuid():N}";

        if (!RabbitMqIntegrationTestSettings.TryCreate(out var settings) || settings is null)
        {
            _isConfigured = false;
            Options = new RabbitMqManagementOptions
            {
                BaseUri = new Uri("http://localhost:15672/api/"),
                Username = string.Empty,
                Password = string.Empty,
                ManagedVirtualHosts = [VirtualHostName],
            };
            ApiClient = null!;
            BrokerTopologyReader = null!;
            TopologyApplier = null!;
            TopologyExporter = null!;
            TopologyPlanner = null!;
            TopologyDestroyPlanner = null!;
            return;
        }

        _isConfigured = true;
        Options = new RabbitMqManagementOptions
        {
            BaseUri = settings.BaseUri,
            Username = settings.Username,
            Password = settings.Password,
            ManagedVirtualHosts = [VirtualHostName],
        };

        ApiClient = new RabbitMqManagementApiClient(_httpClient, Options);
        BrokerTopologyReader = new RabbitMqManagementTopologyReader(ApiClient, Options);
        TopologyApplier = new RabbitMqManagementTopologyApplier(ApiClient);
        TopologyExporter = new RabbitMqManagementTopologyExporter(BrokerTopologyReader);
        TopologyPlanner = new TopologyPlannerService();
        TopologyDestroyPlanner = new TopologyDestroyPlannerService();
    }

    public async Task InitializeAsync()
    {
        if (!_isConfigured)
        {
            return;
        }

        await ResetVirtualHostAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_isConfigured)
        {
            return;
        }

        await ApiClient.DeleteVirtualHostAsync(VirtualHostName);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public async ValueTask ResetVirtualHostAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConfigured)
        {
            return;
        }

        await ApiClient.DeleteVirtualHostAsync(VirtualHostName, cancellationToken);
        await ApiClient.CreateVirtualHostAsync(VirtualHostName, cancellationToken);
    }
}
