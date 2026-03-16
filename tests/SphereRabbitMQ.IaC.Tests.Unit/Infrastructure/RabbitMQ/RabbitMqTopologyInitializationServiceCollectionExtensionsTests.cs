using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.IaC.Application.Apply;
using SphereRabbitMQ.IaC.Application.Apply.Interfaces;
using SphereRabbitMQ.IaC.Application.Broker.Interfaces;
using SphereRabbitMQ.IaC.Application.Export.Interfaces;
using SphereRabbitMQ.IaC.Application.Workflows.Interfaces;
using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.DependencyInjection;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Startup;

namespace SphereRabbitMQ.IaC.Tests.Unit.Infrastructure.RabbitMQ;

public sealed class RabbitMqTopologyInitializationServiceCollectionExtensionsTests
{
    private const string InitializerHostedServiceFullName = "SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Startup.RabbitMqTopologyInitializationHostedService";
    private const string TopologyValidationHostedServiceFullName = "SphereRabbitMQ.DependencyInjection.Subscribers.RabbitMqTopologyValidationHostedService";
    private const string SubscribersHostedServiceFullName = "SphereRabbitMQ.DependencyInjection.Subscribers.RabbitMqSubscribersHostedService";
    private static readonly string[] ExpectedHostedServiceOrder =
    [
        InitializerHostedServiceFullName,
        TopologyValidationHostedServiceFullName,
        SubscribersHostedServiceFullName,
    ];

    [Fact]
    public void AddSphereRabbitMqWithTopologyInitialization_RegistersInitializerBeforeRuntimeHostedServices()
    {
        var services = new ServiceCollection();

        services.AddSphereRabbitMqWithTopologyInitialization(
            configureTopologyInitialization: options => options.YamlFilePath = "topology.yaml");

        var hostedServiceTypes = services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .Select(descriptor => descriptor.ImplementationType?.FullName)
            .Where(name => name is not null)
            .ToArray();

        Assert.Equal(ExpectedHostedServiceOrder, hostedServiceTypes!);
    }
}

public sealed class RabbitMqTopologyInitializationHostedServiceTests
{
    [Fact]
    public async Task StartAsync_AppliesTopologyFromYamlFile()
    {
        var topologyFilePath = CreateTopologyFile();
        try
        {
            var workflowServiceMock = new Mock<ITopologyWorkflowService>(MockBehavior.Strict);
            workflowServiceMock
                .Setup(service => service.PlanAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CreateDefinition(), TopologyValidationResult.Success, new TopologyPlan(Array.Empty<TopologyPlanOperation>())));
            workflowServiceMock
                .Setup(service => service.ApplyAsync(
                    It.IsAny<Stream>(),
                    It.Is<TopologyApplyOptions?>(options => options == TopologyApplyOptions.Safe),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((CreateDefinition(), TopologyValidationResult.Success, new TopologyPlan(Array.Empty<TopologyPlanOperation>())));

            var runtimeServiceFactoryMock = new Mock<IRabbitMqRuntimeServiceFactory>(MockBehavior.Strict);
            runtimeServiceFactoryMock
                .Setup(factory => factory.Create(It.Is<RabbitMqManagementOptions>(options =>
                    options.BaseUri == new Uri("http://rabbit.internal:15672/api/") &&
                    options.Username == "runtime-user" &&
                    options.Password == "runtime-password" &&
                    options.ManagedVirtualHosts.SequenceEqual(new[] { "sales" }) &&
                    options.AmqpHostName == "rabbit.internal" &&
                    options.AmqpPort == 5678 &&
                    options.AmqpVirtualHost == "sales")))
                .Returns(CreateRuntimeServices(workflowServiceMock.Object));

            var hostedService = CreateHostedService(runtimeServiceFactoryMock.Object, topologyFilePath);

            await hostedService.StartAsync(CancellationToken.None);

            runtimeServiceFactoryMock.VerifyAll();
            workflowServiceMock.VerifyAll();
        }
        finally
        {
            File.Delete(topologyFilePath);
        }
    }

    [Fact]
    public async Task StartAsync_Throws_WhenPlanRequiresMigrationsAndTheyAreDisabled()
    {
        var topologyFilePath = CreateTopologyFile();
        try
        {
            var workflowServiceMock = new Mock<ITopologyWorkflowService>(MockBehavior.Strict);
            workflowServiceMock
                .Setup(service => service.PlanAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((
                    CreateDefinition(),
                    TopologyValidationResult.Success,
                    new TopologyPlan(
                        Array.Empty<TopologyPlanOperation>(),
                        destructiveChanges: [new DestructiveChangeWarning("/virtualHosts/sales/queues/orders", "queue recreation required")])));

            var runtimeServiceFactoryMock = new Mock<IRabbitMqRuntimeServiceFactory>(MockBehavior.Strict);
            runtimeServiceFactoryMock
                .Setup(factory => factory.Create(It.IsAny<RabbitMqManagementOptions>()))
                .Returns(CreateRuntimeServices(workflowServiceMock.Object));

            var hostedService = CreateHostedService(runtimeServiceFactoryMock.Object, topologyFilePath);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => hostedService.StartAsync(CancellationToken.None));

            Assert.Contains("AllowMigrations=true", exception.Message, StringComparison.Ordinal);
            workflowServiceMock.Verify(service => service.ApplyAsync(It.IsAny<Stream>(), It.IsAny<TopologyApplyOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            File.Delete(topologyFilePath);
        }
    }

    private static RabbitMqTopologyInitializationHostedService CreateHostedService(
        IRabbitMqRuntimeServiceFactory runtimeServiceFactory,
        string topologyFilePath,
        bool allowMigrations = false)
        => new(
            runtimeServiceFactory,
            Options.Create(new SphereRabbitMqOptions
            {
                HostName = "rabbit.internal",
                Port = 5678,
                VirtualHost = "sales",
                UserName = "runtime-user",
                Password = "runtime-password",
            }),
            Options.Create(new RabbitMqTopologyInitializationOptions
            {
                YamlFilePath = topologyFilePath,
                AllowMigrations = allowMigrations,
            }),
            NullLogger<RabbitMqTopologyInitializationHostedService>.Instance);

    private static RabbitMqRuntimeServices CreateRuntimeServices(ITopologyWorkflowService workflowService)
        => new(
            new HttpClient(),
            new RabbitMqManagementOptions
            {
                BaseUri = new Uri("http://localhost:15672/api/"),
                Username = "guest",
                Password = "guest",
                ManagedVirtualHosts = ["sales"],
            },
            Mock.Of<IRabbitMqManagementApiClient>(),
            Mock.Of<IBrokerTopologyReader>(),
            Mock.Of<ITopologyApplier>(),
            Mock.Of<ITopologyExporter>(),
            workflowService);

    private static TopologyDefinition CreateDefinition()
        => new([new VirtualHostDefinition("sales")]);

    private static string CreateTopologyFile()
    {
        var topologyFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(topologyFilePath, "virtualHosts:\n  - name: sales\n");
        return topologyFilePath;
    }
}
