using Moq;
using SphereRabbitMQ.IaC.Application.Apply;
using SphereRabbitMQ.IaC.Application.Apply.Interfaces;
using SphereRabbitMQ.IaC.Application.Broker.Interfaces;
using SphereRabbitMQ.IaC.Application.Export.Interfaces;
using SphereRabbitMQ.IaC.Application.Models;
using SphereRabbitMQ.IaC.Application.Normalization.Interfaces;
using SphereRabbitMQ.IaC.Application.Parsing.Interfaces;
using SphereRabbitMQ.IaC.Application.Planning.Interfaces;
using SphereRabbitMQ.IaC.Application.Validation.Interfaces;
using SphereRabbitMQ.IaC.Application.Workflows;
using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Tests.Unit.Application.Workflows;

public sealed class TopologyWorkflowServiceTests
{
    [Fact]
    public async Task ValidateAsync_ParsesNormalizesAndValidates()
    {
        var document = CreateDocument();
        var definition = CreateDefinition("sales");
        var validation = TopologyValidationResult.Success;

        var parserMock = new Mock<ITopologyParser>();
        parserMock.Setup(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync(document);

        var normalizerMock = new Mock<ITopologyNormalizer>();
        normalizerMock.Setup(normalizer => normalizer.NormalizeAsync(document, It.IsAny<CancellationToken>())).ReturnsAsync(definition);

        var validatorMock = new Mock<ITopologyValidator>();
        validatorMock.Setup(validator => validator.ValidateAsync(definition, It.IsAny<CancellationToken>())).ReturnsAsync(validation);

        var service = CreateService(
            parser: parserMock.Object,
            normalizer: normalizerMock.Object,
            validator: validatorMock.Object);

        await using var stream = new MemoryStream([1]);
        var result = await service.ValidateAsync(stream, CancellationToken.None);

        Assert.Equal(definition, result.Definition);
        Assert.Equal(validation, result.Validation);
        parserMock.Verify(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        normalizerMock.Verify(normalizer => normalizer.NormalizeAsync(document, It.IsAny<CancellationToken>()), Times.Once);
        validatorMock.Verify(validator => validator.ValidateAsync(definition, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PlanAsync_ReturnsEmptyPlan_WhenValidationFails()
    {
        var definition = CreateDefinition("sales");
        var validation = new TopologyValidationResult(
        [
            new TopologyIssue("invalid", "Validation error", "/virtualHosts/sales", TopologyIssueSeverity.Error),
        ]);

        var service = CreateService(
            parser: Mock.Of<ITopologyParser>(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyDocument>(CreateDocument())),
            normalizer: Mock.Of<ITopologyNormalizer>(normalizer => normalizer.NormalizeAsync(It.IsAny<TopologyDocument>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyDefinition>(definition)),
            validator: Mock.Of<ITopologyValidator>(validator => validator.ValidateAsync(definition, It.IsAny<CancellationToken>()) == new ValueTask<TopologyValidationResult>(validation)),
            brokerTopologyReader: Mock.Of<IBrokerTopologyReader>(MockBehavior.Strict),
            planner: Mock.Of<ITopologyPlanner>(MockBehavior.Strict));

        await using var stream = new MemoryStream([1]);
        var result = await service.PlanAsync(stream, CancellationToken.None);

        Assert.Equal(definition, result.Definition);
        Assert.Equal(validation, result.Validation);
        Assert.Empty(result.Plan.Operations);
    }

    [Fact]
    public async Task PlanAsync_ReadsActualAndPlans_WhenValidationSucceeds()
    {
        var desired = CreateDefinition("sales");
        var actual = CreateDefinition("sales-current");
        var plan = new TopologyPlan(
        [
            new TopologyPlanOperation(TopologyPlanOperationKind.Create, TopologyResourceKind.Queue, "/virtualHosts/sales/queues/orders", "Create queue"),
        ]);

        var brokerReaderMock = new Mock<IBrokerTopologyReader>();
        brokerReaderMock.Setup(reader => reader.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(actual);

        var plannerMock = new Mock<ITopologyPlanner>();
        plannerMock.Setup(planner => planner.PlanAsync(desired, actual, It.IsAny<CancellationToken>())).ReturnsAsync(plan);

        var service = CreateService(
            parser: Mock.Of<ITopologyParser>(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyDocument>(CreateDocument())),
            normalizer: Mock.Of<ITopologyNormalizer>(normalizer => normalizer.NormalizeAsync(It.IsAny<TopologyDocument>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyDefinition>(desired)),
            validator: Mock.Of<ITopologyValidator>(validator => validator.ValidateAsync(desired, It.IsAny<CancellationToken>()) == new ValueTask<TopologyValidationResult>(TopologyValidationResult.Success)),
            brokerTopologyReader: brokerReaderMock.Object,
            planner: plannerMock.Object);

        await using var stream = new MemoryStream([1]);
        var result = await service.PlanAsync(stream, CancellationToken.None);

        Assert.Equal(plan, result.Plan);
        brokerReaderMock.Verify(reader => reader.ReadAsync(It.IsAny<CancellationToken>()), Times.Once);
        plannerMock.Verify(planner => planner.PlanAsync(desired, actual, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_InvokesApplier_WhenValidationSucceeds()
    {
        var desired = CreateDefinition("sales");
        var actual = CreateDefinition("sales-current");
        var plan = new TopologyPlan([new TopologyPlanOperation(TopologyPlanOperationKind.NoOp, TopologyResourceKind.VirtualHost, "/virtualHosts/sales", "NoOp")]);
        var options = new TopologyApplyOptions { AllowMigrations = true };

        var applierMock = new Mock<ITopologyApplier>();
        applierMock
            .Setup(applier => applier.ApplyAsync(desired, plan, options, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService(
            parser: Mock.Of<ITopologyParser>(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyDocument>(CreateDocument())),
            normalizer: Mock.Of<ITopologyNormalizer>(normalizer => normalizer.NormalizeAsync(It.IsAny<TopologyDocument>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyDefinition>(desired)),
            validator: Mock.Of<ITopologyValidator>(validator => validator.ValidateAsync(desired, It.IsAny<CancellationToken>()) == new ValueTask<TopologyValidationResult>(TopologyValidationResult.Success)),
            brokerTopologyReader: Mock.Of<IBrokerTopologyReader>(reader => reader.ReadAsync(It.IsAny<CancellationToken>()) == new ValueTask<TopologyDefinition>(actual)),
            planner: Mock.Of<ITopologyPlanner>(planner => planner.PlanAsync(desired, actual, It.IsAny<CancellationToken>()) == new ValueTask<TopologyPlan>(plan)),
            applier: applierMock.Object);

        await using var stream = new MemoryStream([1]);
        var result = await service.ApplyAsync(stream, options, CancellationToken.None);

        Assert.Equal(plan, result.Plan);
        applierMock.Verify(applier => applier.ApplyAsync(desired, plan, options, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_DoesNotInvokeApplier_WhenValidationFails()
    {
        var desired = CreateDefinition("sales");
        var validation = new TopologyValidationResult(
        [
            new TopologyIssue("invalid", "Validation error", "/virtualHosts/sales", TopologyIssueSeverity.Error),
        ]);

        var applierMock = new Mock<ITopologyApplier>(MockBehavior.Strict);
        var service = CreateService(
            parser: Mock.Of<ITopologyParser>(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyDocument>(CreateDocument())),
            normalizer: Mock.Of<ITopologyNormalizer>(normalizer => normalizer.NormalizeAsync(It.IsAny<TopologyDocument>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyDefinition>(desired)),
            validator: Mock.Of<ITopologyValidator>(validator => validator.ValidateAsync(desired, It.IsAny<CancellationToken>()) == new ValueTask<TopologyValidationResult>(validation)),
            brokerTopologyReader: Mock.Of<IBrokerTopologyReader>(MockBehavior.Strict),
            planner: Mock.Of<ITopologyPlanner>(MockBehavior.Strict),
            applier: applierMock.Object);

        await using var stream = new MemoryStream([1]);
        var result = await service.ApplyAsync(stream, null, CancellationToken.None);

        Assert.Equal(validation, result.Validation);
        Assert.Empty(result.Plan.Operations);
    }

    [Fact]
    public async Task PlanDestroyAsync_UsesDestroyPlannerAndFlag_WhenValidationSucceeds()
    {
        var desired = CreateDefinition("sales");
        var actual = CreateDefinition("sales-current");
        var destroyPlan = new TopologyPlan([new TopologyPlanOperation(TopologyPlanOperationKind.Destroy, TopologyResourceKind.Queue, "/virtualHosts/sales/queues/orders", "Destroy queue")]);

        var destroyPlannerMock = new Mock<ITopologyDestroyPlanner>();
        destroyPlannerMock
            .Setup(planner => planner.PlanAsync(desired, actual, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(destroyPlan);

        var service = CreateService(
            parser: Mock.Of<ITopologyParser>(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyDocument>(CreateDocument())),
            normalizer: Mock.Of<ITopologyNormalizer>(normalizer => normalizer.NormalizeAsync(It.IsAny<TopologyDocument>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyDefinition>(desired)),
            validator: Mock.Of<ITopologyValidator>(validator => validator.ValidateAsync(desired, It.IsAny<CancellationToken>()) == new ValueTask<TopologyValidationResult>(TopologyValidationResult.Success)),
            brokerTopologyReader: Mock.Of<IBrokerTopologyReader>(reader => reader.ReadAsync(It.IsAny<CancellationToken>()) == new ValueTask<TopologyDefinition>(actual)),
            destroyPlanner: destroyPlannerMock.Object);

        await using var stream = new MemoryStream([1]);
        var result = await service.PlanDestroyAsync(stream, destroyVirtualHosts: true, CancellationToken.None);

        Assert.Equal(destroyPlan, result.Plan);
        destroyPlannerMock.Verify(planner => planner.PlanAsync(desired, actual, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DestroyAsync_InvokesApplierWithSafeOptions_WhenValidationSucceeds()
    {
        var desired = CreateDefinition("sales");
        var actual = CreateDefinition("sales-current");
        var destroyPlan = new TopologyPlan([new TopologyPlanOperation(TopologyPlanOperationKind.Destroy, TopologyResourceKind.Queue, "/virtualHosts/sales/queues/orders", "Destroy queue")]);

        var applierMock = new Mock<ITopologyApplier>();
        applierMock
            .Setup(applier => applier.ApplyAsync(desired, destroyPlan, TopologyApplyOptions.Safe, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService(
            parser: Mock.Of<ITopologyParser>(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyDocument>(CreateDocument())),
            normalizer: Mock.Of<ITopologyNormalizer>(normalizer => normalizer.NormalizeAsync(It.IsAny<TopologyDocument>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyDefinition>(desired)),
            validator: Mock.Of<ITopologyValidator>(validator => validator.ValidateAsync(desired, It.IsAny<CancellationToken>()) == new ValueTask<TopologyValidationResult>(TopologyValidationResult.Success)),
            brokerTopologyReader: Mock.Of<IBrokerTopologyReader>(reader => reader.ReadAsync(It.IsAny<CancellationToken>()) == new ValueTask<TopologyDefinition>(actual)),
            destroyPlanner: Mock.Of<ITopologyDestroyPlanner>(planner => planner.PlanAsync(desired, actual, false, It.IsAny<CancellationToken>()) == new ValueTask<TopologyPlan>(destroyPlan)),
            applier: applierMock.Object);

        await using var stream = new MemoryStream([1]);
        var result = await service.DestroyAsync(stream, destroyVirtualHosts: false, CancellationToken.None);

        Assert.Equal(destroyPlan, result.Plan);
        applierMock.Verify(applier => applier.ApplyAsync(desired, destroyPlan, TopologyApplyOptions.Safe, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExportAsync_DelegatesToExporter()
    {
        var expected = CreateDocument();
        var exporterMock = new Mock<ITopologyExporter>();
        exporterMock.Setup(exporter => exporter.ExportAsync(It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var service = CreateService(exporter: exporterMock.Object);

        var exported = await service.ExportAsync(CancellationToken.None);

        Assert.Equal(expected, exported);
        exporterMock.Verify(exporter => exporter.ExportAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static TopologyWorkflowService CreateService(
        ITopologyParser? parser = null,
        ITopologyNormalizer? normalizer = null,
        ITopologyValidator? validator = null,
        IBrokerTopologyReader? brokerTopologyReader = null,
        ITopologyPlanner? planner = null,
        ITopologyDestroyPlanner? destroyPlanner = null,
        ITopologyApplier? applier = null,
        ITopologyExporter? exporter = null)
        => new(
            parser ?? Mock.Of<ITopologyParser>(mock => mock.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyDocument>(CreateDocument())),
            normalizer ?? Mock.Of<ITopologyNormalizer>(mock => mock.NormalizeAsync(It.IsAny<TopologyDocument>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyDefinition>(CreateDefinition("default"))),
            validator ?? Mock.Of<ITopologyValidator>(mock => mock.ValidateAsync(It.IsAny<TopologyDefinition>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyValidationResult>(TopologyValidationResult.Success)),
            brokerTopologyReader ?? Mock.Of<IBrokerTopologyReader>(mock => mock.ReadAsync(It.IsAny<CancellationToken>()) == new ValueTask<TopologyDefinition>(CreateDefinition("actual"))),
            planner ?? Mock.Of<ITopologyPlanner>(mock => mock.PlanAsync(It.IsAny<TopologyDefinition>(), It.IsAny<TopologyDefinition>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyPlan>(new TopologyPlan(Array.Empty<TopologyPlanOperation>()))),
            destroyPlanner ?? Mock.Of<ITopologyDestroyPlanner>(mock => mock.PlanAsync(It.IsAny<TopologyDefinition>(), It.IsAny<TopologyDefinition>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()) == new ValueTask<TopologyPlan>(new TopologyPlan(Array.Empty<TopologyPlanOperation>()))),
            applier ?? Mock.Of<ITopologyApplier>(mock => mock.ApplyAsync(It.IsAny<TopologyDefinition>(), It.IsAny<TopologyPlan>(), It.IsAny<TopologyApplyOptions?>(), It.IsAny<CancellationToken>()) == ValueTask.CompletedTask),
            exporter ?? Mock.Of<ITopologyExporter>(mock => mock.ExportAsync(It.IsAny<CancellationToken>()) == new ValueTask<TopologyDocument>(CreateDocument())));

    private static TopologyDocument CreateDocument()
        => new()
        {
            VirtualHosts =
            [
                new VirtualHostDocument { Name = "sales" },
            ],
        };

    private static TopologyDefinition CreateDefinition(string virtualHost)
        => new(
        [
            new VirtualHostDefinition(virtualHost),
        ]);
}
