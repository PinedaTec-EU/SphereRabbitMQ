using Microsoft.Extensions.DependencyInjection;
using Moq;
using SphereRabbitMQ.IaC.Application.Export.Interfaces;
using SphereRabbitMQ.IaC.Application.Models;
using SphereRabbitMQ.IaC.Application.Normalization.Interfaces;
using SphereRabbitMQ.IaC.Application.Parsing.Interfaces;
using SphereRabbitMQ.IaC.Application.Validation.Interfaces;
using SphereRabbitMQ.IaC.Application.Workflows.Interfaces;
using SphereRabbitMQ.IaC.Cli;
using SphereRabbitMQ.IaC.Cli.Commands;
using SphereRabbitMQ.IaC.Cli.Commands.Interfaces;
using SphereRabbitMQ.IaC.Cli.Commands.Models;
using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.Yaml.Parsing;
using SphereRabbitMQ.IaC.Application.Variables;

namespace SphereRabbitMQ.IaC.Tests.Unit.Cli;

public sealed class TopologyCliTests
{
    [Fact]
    public async Task MainAsync_DestroyHelp_PrintsExamples()
    {
        var output = new StringWriter();
        var originalOutput = Console.Out;

        Console.SetOut(output);

        try
        {
            var exitCode = await Program.Main(["destroy", "--help"]);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Contains("Requires --allow-destructive", output.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("sprmq destroy --file samples/minimal-topology.yaml --dry-run", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
    }

    [Fact]
    public async Task DestroyAsync_ReturnsPermissionRequired_WhenAllowDestructiveIsMissing()
    {
        var topologyParserMock = new Mock<ITopologyParser>(MockBehavior.Strict);
        var topologyNormalizerMock = new Mock<ITopologyNormalizer>(MockBehavior.Strict);
        var topologyValidatorMock = new Mock<ITopologyValidator>(MockBehavior.Strict);
        var runtimeFactoryMock = new Mock<IRabbitMqRuntimeServiceFactory>(MockBehavior.Strict);
        var topologyDocumentWriterMock = new Mock<ITopologyDocumentWriter>(MockBehavior.Strict);
        var commandOutputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);
        commandOutputWriterMock.Setup(writer => writer.WriteText(It.IsAny<string>()));

        var handler = new TopologyCommandHandler(
            topologyParserMock.Object,
            topologyNormalizerMock.Object,
            topologyValidatorMock.Object,
            runtimeFactoryMock.Object,
            topologyDocumentWriterMock.Object,
            commandOutputWriterMock.Object);

        var exitCode = await handler.DestroyAsync(
            "does-not-matter.yaml",
            new BrokerOptionsInput(null, null, null, null),
            TopologyOutputFormat.Text,
            false,
            false,
            false,
            false,
            CancellationToken.None);

        Assert.Equal(CommandExitCodes.DestructivePermissionRequired, exitCode);
        runtimeFactoryMock.Verify(factory => factory.Create(It.IsAny<RabbitMqManagementOptions>()), Times.Never);
        commandOutputWriterMock.Verify(writer => writer.WriteText(It.Is<string>(value => value.Contains("--allow-destructive", StringComparison.Ordinal))), Times.Once);
    }

    [Fact]
    public async Task DestroyAsync_DryRun_SucceedsWithoutAllowDestructive()
    {
        var filePath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(filePath, "topology: ignored");

            var topologyParserMock = new Mock<ITopologyParser>(MockBehavior.Strict);
            var topologyNormalizerMock = new Mock<ITopologyNormalizer>(MockBehavior.Strict);
            var topologyValidatorMock = new Mock<ITopologyValidator>(MockBehavior.Strict);
            var runtimeFactoryMock = new Mock<IRabbitMqRuntimeServiceFactory>(MockBehavior.Strict);
            var topologyDocumentWriterMock = new Mock<ITopologyDocumentWriter>(MockBehavior.Strict);
            var commandOutputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);
            var topologyWorkflowServiceMock = new Mock<ITopologyWorkflowService>(MockBehavior.Strict);
            var managementApiClientMock = new Mock<IRabbitMqManagementApiClient>(MockBehavior.Strict);
            var brokerTopologyReaderMock = new Mock<global::SphereRabbitMQ.IaC.Application.Broker.Interfaces.IBrokerTopologyReader>(MockBehavior.Strict);
            var topologyApplierMock = new Mock<global::SphereRabbitMQ.IaC.Application.Apply.Interfaces.ITopologyApplier>(MockBehavior.Strict);
            var topologyExporterMock = new Mock<global::SphereRabbitMQ.IaC.Application.Export.Interfaces.ITopologyExporter>(MockBehavior.Strict);

            topologyParserMock
                .Setup(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TopologyDocument>(new TopologyDocument
                {
                    VirtualHosts = [new VirtualHostDocument { Name = "sales" }],
                }));
            topologyWorkflowServiceMock
                .Setup(service => service.PlanDestroyAsync(It.IsAny<Stream>(), false, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<(TopologyDefinition, TopologyValidationResult, TopologyPlan)>((
                    new TopologyDefinition([new VirtualHostDefinition("sales")]),
                    new TopologyValidationResult(Array.Empty<TopologyIssue>()),
                    new TopologyPlan([new TopologyPlanOperation(TopologyPlanOperationKind.NoOp, TopologyResourceKind.VirtualHost, "/virtualHosts/sales", "Virtual host 'sales' is already absent.")]))));
            runtimeFactoryMock
                .Setup(factory => factory.Create(It.IsAny<RabbitMqManagementOptions>()))
                .Returns(new RabbitMqRuntimeServices(
                    new HttpClient(),
                    new RabbitMqManagementOptions
                    {
                        BaseUri = new Uri("http://localhost:15672/api/"),
                        Username = "guest",
                        Password = "guest",
                    },
                    managementApiClientMock.Object,
                    brokerTopologyReaderMock.Object,
                    topologyApplierMock.Object,
                    topologyExporterMock.Object,
                    topologyWorkflowServiceMock.Object));
            commandOutputWriterMock.Setup(writer => writer.WriteText(It.IsAny<string>()));

            var handler = new TopologyCommandHandler(
                topologyParserMock.Object,
                topologyNormalizerMock.Object,
                topologyValidatorMock.Object,
                runtimeFactoryMock.Object,
                topologyDocumentWriterMock.Object,
                commandOutputWriterMock.Object);

            var exitCode = await handler.DestroyAsync(
                filePath,
                new BrokerOptionsInput(null, null, null, null),
                TopologyOutputFormat.Text,
                true,
                false,
                false,
                false,
                CancellationToken.None);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            topologyWorkflowServiceMock.Verify(service => service.PlanDestroyAsync(It.IsAny<Stream>(), false, It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ApplyAsync_ReturnsUnsupportedPlan_AndPrintsBlockingOperations()
    {
        var filePath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(filePath, "topology: ignored");

            var topologyParserMock = new Mock<ITopologyParser>(MockBehavior.Strict);
            var topologyNormalizerMock = new Mock<ITopologyNormalizer>(MockBehavior.Strict);
            var topologyValidatorMock = new Mock<ITopologyValidator>(MockBehavior.Strict);
            var runtimeFactoryMock = new Mock<IRabbitMqRuntimeServiceFactory>(MockBehavior.Strict);
            var topologyDocumentWriterMock = new Mock<ITopologyDocumentWriter>(MockBehavior.Strict);
            var commandOutputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);
            var topologyWorkflowServiceMock = new Mock<ITopologyWorkflowService>(MockBehavior.Strict);
            var managementApiClientMock = new Mock<IRabbitMqManagementApiClient>(MockBehavior.Strict);
            var brokerTopologyReaderMock = new Mock<global::SphereRabbitMQ.IaC.Application.Broker.Interfaces.IBrokerTopologyReader>(MockBehavior.Strict);
            var topologyApplierMock = new Mock<global::SphereRabbitMQ.IaC.Application.Apply.Interfaces.ITopologyApplier>(MockBehavior.Strict);
            var topologyExporterMock = new Mock<global::SphereRabbitMQ.IaC.Application.Export.Interfaces.ITopologyExporter>(MockBehavior.Strict);

            topologyParserMock
                .Setup(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TopologyDocument>(new TopologyDocument
                {
                    VirtualHosts = [new VirtualHostDocument { Name = "sales" }],
                }));
            topologyWorkflowServiceMock
                .Setup(service => service.PlanAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<(TopologyDefinition, TopologyValidationResult, TopologyPlan)>((
                    new TopologyDefinition([new VirtualHostDefinition("sales")]),
                    new TopologyValidationResult(Array.Empty<TopologyIssue>()),
                    new TopologyPlan(
                    [
                        new TopologyPlanOperation(
                            TopologyPlanOperationKind.DestructiveChange,
                            TopologyResourceKind.Queue,
                            "/virtualHosts/sales/queues/orders",
                            "Queue 'orders' requires delete/recreate because immutable properties changed.",
                            [new TopologyDiff("/virtualHosts/sales/queues/orders", TopologyResourceKind.Queue, "type", "Quorum", "Classic")]),
                    ],
                    destructiveChanges:
                    [
                        new DestructiveChangeWarning("/virtualHosts/sales/queues/orders", "Queue 'orders' requires delete/recreate because immutable properties changed."),
                    ]))));
            runtimeFactoryMock
                .Setup(factory => factory.Create(It.IsAny<RabbitMqManagementOptions>()))
                .Returns(new RabbitMqRuntimeServices(
                    new HttpClient(),
                    new RabbitMqManagementOptions
                    {
                        BaseUri = new Uri("http://localhost:15672/api/"),
                        Username = "guest",
                        Password = "guest",
                    },
                    managementApiClientMock.Object,
                    brokerTopologyReaderMock.Object,
                    topologyApplierMock.Object,
                    topologyExporterMock.Object,
                    topologyWorkflowServiceMock.Object));
            commandOutputWriterMock.Setup(writer => writer.WriteText(It.IsAny<string>()));

            var handler = new TopologyCommandHandler(
                topologyParserMock.Object,
                topologyNormalizerMock.Object,
                topologyValidatorMock.Object,
                runtimeFactoryMock.Object,
                topologyDocumentWriterMock.Object,
                commandOutputWriterMock.Object);

            var exitCode = await handler.ApplyAsync(
                filePath,
                new BrokerOptionsInput(null, null, null, null),
                TopologyOutputFormat.Text,
                false,
                true,
                CancellationToken.None);

            Assert.Equal(CommandExitCodes.UnsupportedPlan, exitCode);
            commandOutputWriterMock.Verify(writer => writer.WriteText(It.Is<string>(value => value.Contains("Blocking plan operations:", StringComparison.Ordinal))), Times.Once);
            commandOutputWriterMock.Verify(writer => writer.WriteText(It.Is<string>(value => value.Contains("/virtualHosts/sales/queues/orders", StringComparison.Ordinal))), Times.Once);
            topologyWorkflowServiceMock.Verify(service => service.ApplyAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ApplyAsync_ReturnsValidationFailed_WhenBrokerVirtualHostsDoNotMatchTopologyVirtualHosts()
    {
        var filePath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(filePath, "topology: ignored");

            var topologyParserMock = new Mock<ITopologyParser>(MockBehavior.Strict);
            var topologyNormalizerMock = new Mock<ITopologyNormalizer>(MockBehavior.Strict);
            var topologyValidatorMock = new Mock<ITopologyValidator>(MockBehavior.Strict);
            var runtimeFactoryMock = new Mock<IRabbitMqRuntimeServiceFactory>(MockBehavior.Strict);
            var topologyDocumentWriterMock = new Mock<ITopologyDocumentWriter>(MockBehavior.Strict);
            var commandOutputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);

            topologyParserMock
                .Setup(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TopologyDocument>(new TopologyDocument
                {
                    Broker = new BrokerDocument
                    {
                        ManagementUrl = "http://localhost:15672/api/",
                        Username = "guest",
                        Password = "guest",
                        VirtualHosts = ["sales-v2"],
                    },
                    VirtualHosts = [new VirtualHostDocument { Name = "sales" }],
                }));
            commandOutputWriterMock.Setup(writer => writer.WriteText(It.IsAny<string>()));

            var handler = new TopologyCommandHandler(
                topologyParserMock.Object,
                topologyNormalizerMock.Object,
                topologyValidatorMock.Object,
                runtimeFactoryMock.Object,
                topologyDocumentWriterMock.Object,
                commandOutputWriterMock.Object);

            var exitCode = await handler.ApplyAsync(
                filePath,
                new BrokerOptionsInput(null, null, null, null),
                TopologyOutputFormat.Text,
                false,
                false,
                CancellationToken.None);

            Assert.Equal(CommandExitCodes.ValidationFailed, exitCode);
            runtimeFactoryMock.Verify(factory => factory.Create(It.IsAny<RabbitMqManagementOptions>()), Times.Never);
            commandOutputWriterMock.Verify(
                writer => writer.WriteText(It.Is<string>(value => value.Contains("broker-virtual-host-mismatch", StringComparison.Ordinal) || value.Contains("do not match declared topology virtualHosts", StringComparison.Ordinal))),
                Times.AtLeastOnce);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ApplyAsync_ReturnsValidationFailed_WhenYamlBrokerVirtualHostDiffersFromDeclaredVirtualHost()
    {
        var filePath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(
                filePath,
                """
                broker:
                  managementUrl: http://localhost:15672/api/
                  username: guest
                  password: guest
                  virtualHosts:
                    - sales-v2
                virtualHosts:
                  - name: sales
                """);

            ITopologyParser topologyParser = new TopologyYamlParser(new EnvironmentVariableResolver());
            var topologyNormalizerMock = new Mock<ITopologyNormalizer>(MockBehavior.Strict);
            var topologyValidatorMock = new Mock<ITopologyValidator>(MockBehavior.Strict);
            var runtimeFactoryMock = new Mock<IRabbitMqRuntimeServiceFactory>(MockBehavior.Strict);
            var topologyDocumentWriterMock = new Mock<ITopologyDocumentWriter>(MockBehavior.Strict);
            var commandOutputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);
            commandOutputWriterMock.Setup(writer => writer.WriteText(It.IsAny<string>()));

            var handler = new TopologyCommandHandler(
                topologyParser,
                topologyNormalizerMock.Object,
                topologyValidatorMock.Object,
                runtimeFactoryMock.Object,
                topologyDocumentWriterMock.Object,
                commandOutputWriterMock.Object);

            var exitCode = await handler.ApplyAsync(
                filePath,
                new BrokerOptionsInput(null, null, null, null),
                TopologyOutputFormat.Text,
                false,
                false,
                CancellationToken.None);

            Assert.Equal(CommandExitCodes.ValidationFailed, exitCode);
            runtimeFactoryMock.Verify(factory => factory.Create(It.IsAny<RabbitMqManagementOptions>()), Times.Never);
            commandOutputWriterMock.Verify(
                writer => writer.WriteText(It.Is<string>(value => value.Contains("do not match declared topology virtualHosts", StringComparison.Ordinal))),
                Times.AtLeastOnce);
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
