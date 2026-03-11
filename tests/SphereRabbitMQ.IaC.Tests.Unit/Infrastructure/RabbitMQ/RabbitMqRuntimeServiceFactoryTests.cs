using System.Net.Http.Headers;
using System.Text;

using Moq;

using SphereRabbitMQ.IaC.Application.Models;
using SphereRabbitMQ.IaC.Application.Normalization.Interfaces;
using SphereRabbitMQ.IaC.Application.Parsing.Interfaces;
using SphereRabbitMQ.IaC.Application.Planning.Interfaces;
using SphereRabbitMQ.IaC.Application.Validation.Interfaces;
using SphereRabbitMQ.IaC.Application.Workflows;
using SphereRabbitMQ.IaC.Domain.Topology;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Apply;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Export;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Read;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime.Interfaces;

namespace SphereRabbitMQ.IaC.Tests.Unit.Infrastructure.RabbitMQ;

public sealed class RabbitMqRuntimeServiceFactoryTests
{
    [Fact]
    public void Create_Throws_WhenOptionsAreNull()
    {
        IRabbitMqRuntimeServiceFactory factory = CreateFactory();

        Assert.Throws<ArgumentNullException>(() => factory.Create(null!));
    }

    [Fact]
    public void Create_ReturnsConfiguredRuntimeServiceGraph()
    {
        IRabbitMqRuntimeServiceFactory factory = CreateFactory();
        var options = CreateOptions();

        using var services = factory.Create(options);

        Assert.Same(options, services.Options);
        Assert.NotNull(services.HttpClient);
        Assert.Equal(options.BaseUri, services.HttpClient.BaseAddress);
        Assert.Equal(
            new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"))),
            services.HttpClient.DefaultRequestHeaders.Authorization);
        Assert.IsType<RabbitMqManagementApiClient>(services.ManagementApiClient);
        Assert.IsType<RabbitMqManagementTopologyReader>(services.BrokerTopologyReader);
        Assert.IsType<RabbitMqManagementTopologyApplier>(services.TopologyApplier);
        Assert.IsType<RabbitMqManagementTopologyExporter>(services.TopologyExporter);
        Assert.IsType<TopologyWorkflowService>(services.TopologyWorkflowService);
    }

    [Fact]
    public async Task Create_WiresWorkflowService_UsingInjectedApplicationCollaborators()
    {
        var document = new TopologyDocument
        {
            VirtualHosts =
            [
                new VirtualHostDocument { Name = "sales" },
            ],
        };
        var definition = new TopologyDefinition(
        [
            new VirtualHostDefinition("sales"),
        ]);

        var parserMock = new Mock<ITopologyParser>(MockBehavior.Strict);
        parserMock.Setup(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync(document);

        var normalizerMock = new Mock<ITopologyNormalizer>(MockBehavior.Strict);
        normalizerMock.Setup(normalizer => normalizer.NormalizeAsync(document, It.IsAny<CancellationToken>())).ReturnsAsync(definition);

        var validatorMock = new Mock<ITopologyValidator>(MockBehavior.Strict);
        validatorMock.Setup(validator => validator.ValidateAsync(definition, It.IsAny<CancellationToken>())).ReturnsAsync(TopologyValidationResult.Success);

        IRabbitMqRuntimeServiceFactory factory = CreateFactory(
            parser: parserMock.Object,
            normalizer: normalizerMock.Object,
            validator: validatorMock.Object);

        using var services = factory.Create(CreateOptions());
        await using var stream = new MemoryStream([1]);

        var result = await services.TopologyWorkflowService.ValidateAsync(stream, CancellationToken.None);

        Assert.Equal(definition, result.Definition);
        Assert.True(result.Validation.IsValid);
        parserMock.Verify(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        normalizerMock.Verify(normalizer => normalizer.NormalizeAsync(document, It.IsAny<CancellationToken>()), Times.Once);
        validatorMock.Verify(validator => validator.ValidateAsync(definition, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static IRabbitMqRuntimeServiceFactory CreateFactory(
        ITopologyParser? parser = null,
        ITopologyNormalizer? normalizer = null,
        ITopologyValidator? validator = null,
        ITopologyPlanner? planner = null,
        ITopologyDestroyPlanner? destroyPlanner = null)
        => new RabbitMqRuntimeServiceFactory(
            parser ?? Mock.Of<ITopologyParser>(mock => mock.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyDocument>(new TopologyDocument())),
            normalizer ?? Mock.Of<ITopologyNormalizer>(mock => mock.NormalizeAsync(It.IsAny<TopologyDocument>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyDefinition>(new TopologyDefinition(Array.Empty<VirtualHostDefinition>()))),
            validator ?? Mock.Of<ITopologyValidator>(mock => mock.ValidateAsync(It.IsAny<TopologyDefinition>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyValidationResult>(TopologyValidationResult.Success)),
            planner ?? Mock.Of<ITopologyPlanner>(),
            destroyPlanner ?? Mock.Of<ITopologyDestroyPlanner>());

    private static RabbitMqManagementOptions CreateOptions()
        => new()
        {
            BaseUri = new Uri("http://localhost:15672/api/"),
            Username = "guest",
            Password = "guest",
            AmqpHostName = "localhost",
            ManagedVirtualHosts = ["sales"],
        };
}