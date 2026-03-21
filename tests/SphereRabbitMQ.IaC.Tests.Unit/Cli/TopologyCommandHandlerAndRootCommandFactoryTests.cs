using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;

using NUlid;

using Moq;

using SphereRabbitMQ.IaC.Application.Apply;
using SphereRabbitMQ.IaC.Application.Export.Interfaces;
using SphereRabbitMQ.IaC.Application.Models;
using SphereRabbitMQ.IaC.Application.Normalization;
using SphereRabbitMQ.IaC.Application.Normalization.Interfaces;
using SphereRabbitMQ.IaC.Application.Parsing.Interfaces;
using SphereRabbitMQ.IaC.Application.Validation.Interfaces;
using SphereRabbitMQ.IaC.Application.Workflows.Interfaces;
using SphereRabbitMQ.IaC.Cli.Commands;
using SphereRabbitMQ.IaC.Cli.Commands.Interfaces;
using SphereRabbitMQ.IaC.Cli.Commands.Models;
using SphereRabbitMQ.IaC.Cli.Templates.Interfaces;
using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Management.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime.Interfaces;

namespace SphereRabbitMQ.IaC.Tests.Unit.Cli;

[Collection(EnvironmentVariableTestCollection.Name)]
public sealed class TopologyCommandHandlerAdditionalTests
{
    [Fact]
    public async Task InitAsync_WritesSelectedTemplate_ToOutputFile()
    {
        var outputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);
        var templateCatalogMock = new Mock<ITopologyTemplateCatalog>(MockBehavior.Strict);
        var outputPath = Path.Combine(Path.GetTempPath(), $"{Ulid.NewUlid()}.yaml");

        try
        {
            templateCatalogMock.Setup(catalog => catalog.GetTemplateContent("retry")).Returns("virtualHosts:\n  - name: sales\n");
            templateCatalogMock.Setup(catalog => catalog.GetTemplateNames()).Returns(["debug", "minimal", "quorum", "retry", "retry-dead-letter", "topic-routing"]);
            outputWriterMock.Setup(writer => writer.WriteText(It.IsAny<string>()));

            var handler = CreateHandler(
                Mock.Of<ITopologyParser>(),
                Mock.Of<ITopologyNormalizer>(),
                Mock.Of<ITopologyValidator>(),
                Mock.Of<IRabbitMqRuntimeServiceFactory>(),
                Mock.Of<ITopologyDocumentWriter>(),
                outputWriterMock.Object,
                templateCatalog: templateCatalogMock.Object);

            var exitCode = await handler.InitAsync("retry", outputPath, false, CancellationToken.None);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal("virtualHosts:\n  - name: sales\n", await File.ReadAllTextAsync(outputPath));
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public async Task CompletionAsync_WritesBashScript()
    {
        var outputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);
        var templateCatalogMock = new Mock<ITopologyTemplateCatalog>(MockBehavior.Strict);

        templateCatalogMock.Setup(catalog => catalog.GetTemplateNames()).Returns(["debug", "minimal", "retry"]);
        outputWriterMock.Setup(writer => writer.WriteText(It.IsAny<string>()));

        var handler = CreateHandler(
            Mock.Of<ITopologyParser>(),
            Mock.Of<ITopologyNormalizer>(),
            Mock.Of<ITopologyValidator>(),
            Mock.Of<IRabbitMqRuntimeServiceFactory>(),
            Mock.Of<ITopologyDocumentWriter>(),
            outputWriterMock.Object,
            templateCatalog: templateCatalogMock.Object);

        var exitCode = await handler.CompletionAsync("bash", CancellationToken.None);

        Assert.Equal(CommandExitCodes.Success, exitCode);
        outputWriterMock.Verify(writer => writer.WriteText(It.Is<string>(text =>
            text.Contains("complete -F _sprmq_completion sprmq", StringComparison.Ordinal) &&
            text.Contains("--template", StringComparison.Ordinal) &&
            text.Contains("minimal", StringComparison.Ordinal))), Times.Once);
    }

    [Fact]
    public async Task ValidateAsync_WithJsonOutput_WritesJsonOnly()
    {
        var parserMock = new Mock<ITopologyParser>(MockBehavior.Strict);
        var normalizerMock = new Mock<ITopologyNormalizer>(MockBehavior.Strict);
        var validatorMock = new Mock<ITopologyValidator>(MockBehavior.Strict);
        var runtimeFactoryMock = new Mock<IRabbitMqRuntimeServiceFactory>(MockBehavior.Strict);
        var topologyDocumentWriterMock = new Mock<ITopologyDocumentWriter>(MockBehavior.Strict);
        var outputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);

        var filePath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(filePath, "topology: ignored");

            var document = new TopologyDocument
            {
                VirtualHosts = [new VirtualHostDocument { Name = "sales" }],
            };
            var definition = new TopologyDefinition([new VirtualHostDefinition("sales")]);

            parserMock.Setup(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync(document);
            normalizerMock.Setup(normalizer => normalizer.NormalizeAsync(document, It.IsAny<CancellationToken>())).ReturnsAsync(definition);
            validatorMock.Setup(validator => validator.ValidateAsync(definition, It.IsAny<CancellationToken>())).ReturnsAsync(TopologyValidationResult.Success);
            outputWriterMock.Setup(writer => writer.WriteJson(It.IsAny<ValidateCommandResult>()));

            var handler = CreateHandler(
                parserMock.Object,
                normalizerMock.Object,
                validatorMock.Object,
                runtimeFactoryMock.Object,
                topologyDocumentWriterMock.Object,
                outputWriterMock.Object);

            var exitCode = await handler.ValidateAsync(filePath, TopologyOutputFormat.Json, true, CancellationToken.None);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            outputWriterMock.Verify(writer => writer.WriteJson(It.IsAny<ValidateCommandResult>()), Times.Once);
            outputWriterMock.Verify(writer => writer.WriteText(It.IsAny<string>()), Times.Never);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ValidateAsync_ReturnsValidationFailed_WhenNormalizationThrows()
    {
        var parserMock = new Mock<ITopologyParser>(MockBehavior.Strict);
        var normalizerMock = new Mock<ITopologyNormalizer>(MockBehavior.Strict);
        var validatorMock = new Mock<ITopologyValidator>(MockBehavior.Strict);
        var runtimeFactoryMock = new Mock<IRabbitMqRuntimeServiceFactory>(MockBehavior.Strict);
        var topologyDocumentWriterMock = new Mock<ITopologyDocumentWriter>(MockBehavior.Strict);
        var outputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);

        var filePath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(filePath, "topology: ignored");

            var issues = new[]
            {
                new TopologyIssue("invalid-exchange-type", "Invalid exchange type.", "/virtualHosts/sales/exchanges/orders", TopologyIssueSeverity.Error),
            };
            var document = new TopologyDocument
            {
                VirtualHosts = [new VirtualHostDocument { Name = "sales" }],
            };

            parserMock.Setup(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync(document);
            normalizerMock
                .Setup(normalizer => normalizer.NormalizeAsync(document, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TopologyNormalizationException(issues));
            outputWriterMock.Setup(writer => writer.WriteText(It.IsAny<string>()));

            var handler = CreateHandler(
                parserMock.Object,
                normalizerMock.Object,
                validatorMock.Object,
                runtimeFactoryMock.Object,
                topologyDocumentWriterMock.Object,
                outputWriterMock.Object);

            var exitCode = await handler.ValidateAsync(filePath, TopologyOutputFormat.Text, false, CancellationToken.None);

            Assert.Equal(CommandExitCodes.ValidationFailed, exitCode);
            outputWriterMock.Verify(writer => writer.WriteText(It.Is<string>(text => text.Contains("Invalid exchange type.", StringComparison.Ordinal))), Times.Once);
            validatorMock.Verify(validator => validator.ValidateAsync(It.IsAny<TopologyDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task PlanAsync_UsesEnvironmentBrokerSettings_WhenCliAndYamlAreMissing()
    {
        var parserMock = new Mock<ITopologyParser>(MockBehavior.Strict);
        var normalizerMock = new Mock<ITopologyNormalizer>(MockBehavior.Strict);
        var validatorMock = new Mock<ITopologyValidator>(MockBehavior.Strict);
        var runtimeFactoryMock = new Mock<IRabbitMqRuntimeServiceFactory>(MockBehavior.Strict);
        var topologyDocumentWriterMock = new Mock<ITopologyDocumentWriter>(MockBehavior.Strict);
        var outputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);
        var workflowMock = new Mock<ITopologyWorkflowService>(MockBehavior.Strict);

        var filePath = Path.GetTempFileName();
        var previousManagementUrl = Environment.GetEnvironmentVariable("SPHERE_RABBITMQ_MANAGEMENT_URL");
        var previousUsername = Environment.GetEnvironmentVariable("SPHERE_RABBITMQ_USERNAME");
        var previousPassword = Environment.GetEnvironmentVariable("SPHERE_RABBITMQ_PASSWORD");
        var previousVhosts = Environment.GetEnvironmentVariable("SPHERE_RABBITMQ_VHOSTS");

        try
        {
            Environment.SetEnvironmentVariable("SPHERE_RABBITMQ_MANAGEMENT_URL", "http://rabbit:15672/api/");
            Environment.SetEnvironmentVariable("SPHERE_RABBITMQ_USERNAME", "env-user");
            Environment.SetEnvironmentVariable("SPHERE_RABBITMQ_PASSWORD", "env-pass");
            Environment.SetEnvironmentVariable("SPHERE_RABBITMQ_VHOSTS", "ops,sales");
            await File.WriteAllTextAsync(filePath, "topology: ignored");

            parserMock.Setup(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync(new TopologyDocument
            {
                VirtualHosts = [new VirtualHostDocument { Name = "sales" }, new VirtualHostDocument { Name = "ops" }],
            });
            workflowMock
                .Setup(service => service.PlanAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((new TopologyDefinition([new VirtualHostDefinition("sales")]), TopologyValidationResult.Success, new TopologyPlan(Array.Empty<TopologyPlanOperation>())));
            runtimeFactoryMock
                .Setup(factory => factory.Create(It.Is<RabbitMqManagementOptions>(options =>
                    options.BaseUri == new Uri("http://rabbit:15672/api/") &&
                    options.Username == "env-user" &&
                    options.Password == "env-pass" &&
                    options.ManagedVirtualHosts.SequenceEqual(new[] { "ops", "sales" }))))
                .Returns(CreateRuntimeServices(workflowMock.Object));
            outputWriterMock.Setup(writer => writer.WriteText(It.IsAny<string>()));

            var handler = CreateHandler(
                parserMock.Object,
                normalizerMock.Object,
                validatorMock.Object,
                runtimeFactoryMock.Object,
                topologyDocumentWriterMock.Object,
                outputWriterMock.Object);

            var exitCode = await handler.PlanAsync(
                filePath,
                new BrokerOptionsInput(null, null, null, null),
                TopologyOutputFormat.Text,
                false,
                CancellationToken.None);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            runtimeFactoryMock.VerifyAll();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPHERE_RABBITMQ_MANAGEMENT_URL", previousManagementUrl);
            Environment.SetEnvironmentVariable("SPHERE_RABBITMQ_USERNAME", previousUsername);
            Environment.SetEnvironmentVariable("SPHERE_RABBITMQ_PASSWORD", previousPassword);
            Environment.SetEnvironmentVariable("SPHERE_RABBITMQ_VHOSTS", previousVhosts);
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ExportAsync_WithStdoutOutput_WritesRenderedTextAndYamlContent()
    {
        var parserMock = new Mock<ITopologyParser>(MockBehavior.Strict);
        var normalizerMock = new Mock<ITopologyNormalizer>(MockBehavior.Strict);
        var validatorMock = new Mock<ITopologyValidator>(MockBehavior.Strict);
        var runtimeFactoryMock = new Mock<IRabbitMqRuntimeServiceFactory>(MockBehavior.Strict);
        var topologyDocumentWriterMock = new Mock<ITopologyDocumentWriter>(MockBehavior.Strict);
        var outputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);
        var workflowMock = new Mock<ITopologyWorkflowService>(MockBehavior.Strict);

        var exportedDocument = new TopologyDocument
        {
            VirtualHosts = [new VirtualHostDocument { Name = "sales" }],
        };

        workflowMock.Setup(service => service.ExportAsync(It.IsAny<CancellationToken>())).ReturnsAsync(exportedDocument);
        runtimeFactoryMock
            .Setup(factory => factory.Create(It.IsAny<RabbitMqManagementOptions>()))
            .Returns(CreateRuntimeServices(workflowMock.Object));
        topologyDocumentWriterMock
            .Setup(writer => writer.WriteAsync(exportedDocument, It.IsAny<CancellationToken>()))
            .ReturnsAsync("virtualHosts:\n  - name: sales\n");
        outputWriterMock.Setup(writer => writer.WriteText(It.IsAny<string>()));

        var handler = CreateHandler(
            parserMock.Object,
            normalizerMock.Object,
            validatorMock.Object,
            runtimeFactoryMock.Object,
            topologyDocumentWriterMock.Object,
            outputWriterMock.Object);

        var exitCode = await handler.ExportAsync(
            null,
            new BrokerOptionsInput("http://localhost:15672/api/", "guest", "guest", ["sales"]),
            "-",
            false,
            TopologyOutputFormat.Text,
            false,
            CancellationToken.None);

        Assert.Equal(CommandExitCodes.Success, exitCode);
        outputWriterMock.Verify(writer => writer.WriteText(It.Is<string>(text => text.Contains("Export completed.", StringComparison.Ordinal))), Times.Once);
        outputWriterMock.Verify(writer => writer.WriteText("virtualHosts:\n  - name: sales\n"), Times.Once);
        outputWriterMock.Verify(writer => writer.WriteJson(It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task ExportAsync_WithIncludeBroker_WritesResolvedBrokerIntoExportedDocument()
    {
        var runtimeFactoryMock = new Mock<IRabbitMqRuntimeServiceFactory>(MockBehavior.Strict);
        var topologyDocumentWriterMock = new Mock<ITopologyDocumentWriter>(MockBehavior.Strict);
        var outputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);
        var workflowMock = new Mock<ITopologyWorkflowService>(MockBehavior.Strict);

        var exportedDocument = new TopologyDocument
        {
            VirtualHosts = [new VirtualHostDocument { Name = "sales" }],
        };

        workflowMock.Setup(service => service.ExportAsync(It.IsAny<CancellationToken>())).ReturnsAsync(exportedDocument);
        runtimeFactoryMock
            .Setup(factory => factory.Create(It.IsAny<RabbitMqManagementOptions>()))
            .Returns(CreateRuntimeServices(workflowMock.Object));
        topologyDocumentWriterMock
            .Setup(writer => writer.WriteAsync(
                It.Is<TopologyDocument>(document =>
                    document.Broker != null &&
                    document.Broker.ManagementUrl == "http://localhost:15672/api/" &&
                    document.Broker.Username == "guest" &&
                    document.Broker.Password == "guest" &&
                    document.Broker.VirtualHosts.SequenceEqual(new[] { "sales" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("broker:\n  managementUrl: http://localhost:15672/api/\n");
        outputWriterMock.Setup(writer => writer.WriteText(It.IsAny<string>()));

        var handler = CreateHandler(
            Mock.Of<ITopologyParser>(),
            Mock.Of<ITopologyNormalizer>(),
            Mock.Of<ITopologyValidator>(),
            runtimeFactoryMock.Object,
            topologyDocumentWriterMock.Object,
            outputWriterMock.Object);

        var exitCode = await handler.ExportAsync(
            null,
            new BrokerOptionsInput("http://localhost:15672/api/", "guest", "guest", ["sales"]),
            "-",
            true,
            TopologyOutputFormat.Text,
            false,
            CancellationToken.None);

        Assert.Equal(CommandExitCodes.Success, exitCode);
        topologyDocumentWriterMock.VerifyAll();
    }

    private static TopologyCommandHandler CreateHandler(
        ITopologyParser parser,
        ITopologyNormalizer normalizer,
        ITopologyValidator validator,
        IRabbitMqRuntimeServiceFactory runtimeFactory,
        ITopologyDocumentWriter documentWriter,
        ICommandOutputWriter outputWriter,
        IDestructiveCommandPrompter? destructiveCommandPrompter = null,
        ITopologyTemplateCatalog? templateCatalog = null)
        => new(
            parser,
            normalizer,
            validator,
            runtimeFactory,
            documentWriter,
            outputWriter,
            destructiveCommandPrompter ?? Mock.Of<IDestructiveCommandPrompter>(),
            templateCatalog ?? CreateTemplateCatalog());

    private static ITopologyTemplateCatalog CreateTemplateCatalog()
    {
        var templateCatalogMock = new Mock<ITopologyTemplateCatalog>(MockBehavior.Strict);
        templateCatalogMock.Setup(catalog => catalog.GetTemplateNames()).Returns(["debug", "minimal", "quorum", "retry", "retry-dead-letter", "topic-routing"]);
        templateCatalogMock.Setup(catalog => catalog.GetTemplateContent("minimal")).Returns("virtualHosts:\n  - name: sales\n");
        return templateCatalogMock.Object;
    }

    private static RabbitMqRuntimeServices CreateRuntimeServices(ITopologyWorkflowService workflowService)
        => new(
            new HttpClient(),
            new RabbitMqManagementOptions
            {
                BaseUri = new Uri("http://localhost:15672/api/"),
                Username = "guest",
                Password = "guest",
            },
            Mock.Of<IRabbitMqManagementApiClient>(),
            Mock.Of<global::SphereRabbitMQ.IaC.Application.Broker.Interfaces.IBrokerTopologyReader>(),
            Mock.Of<global::SphereRabbitMQ.IaC.Application.Apply.Interfaces.ITopologyApplier>(),
            Mock.Of<global::SphereRabbitMQ.IaC.Application.Export.Interfaces.ITopologyExporter>(),
            workflowService);
}

[Collection(EnvironmentVariableTestCollection.Name)]
public sealed class TopologyRootCommandFactoryTests
{
    [Fact]
    public void Create_RegistersExpectedCommandsAndOptions()
    {
        using var services = new ServiceCollection()
            .AddSingleton<ITopologyTemplateCatalog>(CreateTemplateCatalog())
            .AddSingleton(CreateHandler())
            .BuildServiceProvider();

        var rootCommand = TopologyRootCommandFactory.Create(services);

        Assert.Equal(["init", "validate", "plan", "apply", "purge", "destroy", "export", "completion"], rootCommand.Subcommands.Select(command => command.Name).ToArray());
        Assert.True(GetCommand(rootCommand, "validate").Options.OfType<Option<string>>().Single(option => option.Name == "file").IsRequired);
        Assert.Contains(GetCommand(rootCommand, "apply").Options, option => option.Name == "migrate");
        Assert.Contains(GetCommand(rootCommand, "destroy").Options, option => option.Name == "allow-destructive");
        Assert.Contains(GetCommand(rootCommand, "destroy").Options, option => option.Name == "auto-approve");
        Assert.DoesNotContain(GetCommand(rootCommand, "destroy").Options, option => option.Name == "destroy-vhost");
        Assert.Contains(GetCommand(rootCommand, "purge").Options, option => option.Name == "allow-destructive");
        Assert.Contains(GetCommand(rootCommand, "purge").Options, option => option.Name == "auto-approve");
        Assert.Contains(GetCommand(rootCommand, "export").Options, option => option.Name == "output-file");
        Assert.Contains(GetCommand(rootCommand, "export").Options, option => option.Name == "include-broker");
    }

    [Fact]
    public async Task Create_InvokedExportCommand_BindsBrokerOptionsFromCommandLine()
    {
        var runtimeFactoryMock = new Mock<IRabbitMqRuntimeServiceFactory>(MockBehavior.Strict);
        var topologyDocumentWriterMock = new Mock<ITopologyDocumentWriter>(MockBehavior.Strict);
        var outputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);
        var workflowMock = new Mock<ITopologyWorkflowService>(MockBehavior.Strict);

        var exportedDocument = new TopologyDocument
        {
            VirtualHosts = [new VirtualHostDocument { Name = "sales" }],
        };

        workflowMock.Setup(service => service.ExportAsync(It.IsAny<CancellationToken>())).ReturnsAsync(exportedDocument);
        runtimeFactoryMock
            .Setup(factory => factory.Create(It.Is<RabbitMqManagementOptions>(options =>
                options.BaseUri == new Uri("http://rabbit:15672/api/") &&
                options.Username == "cli-user" &&
                options.Password == "cli-pass" &&
                options.ManagedVirtualHosts.SequenceEqual(new[] { "sales", "ops" }))))
            .Returns(CreateRuntimeServices(workflowMock.Object));
        topologyDocumentWriterMock.Setup(writer => writer.WriteAsync(exportedDocument, It.IsAny<CancellationToken>())).ReturnsAsync("yaml-content");
        outputWriterMock.Setup(writer => writer.WriteJson(It.IsAny<ExportCommandResult>()));

        using var services = new ServiceCollection()
            .AddSingleton<ITopologyTemplateCatalog>(CreateTemplateCatalog())
            .AddSingleton(CreateHandler(
                Mock.Of<ITopologyParser>(),
                Mock.Of<ITopologyNormalizer>(),
                Mock.Of<ITopologyValidator>(),
                runtimeFactoryMock.Object,
                topologyDocumentWriterMock.Object,
                outputWriterMock.Object))
            .BuildServiceProvider();

        var rootCommand = TopologyRootCommandFactory.Create(services);
        var exitCode = await rootCommand.InvokeAsync([
            "export",
            "--management-url", "http://rabbit:15672/api/",
            "--username", "cli-user",
            "--password", "cli-pass",
            "--vhost", "sales",
            "--vhost", "ops",
            "--output", "json",
            "--output-file", "-",
        ]);

        Assert.Equal(CommandExitCodes.Success, exitCode);
        runtimeFactoryMock.VerifyAll();
        outputWriterMock.Verify(writer => writer.WriteJson(It.IsAny<ExportCommandResult>()), Times.Once);
        outputWriterMock.Verify(writer => writer.WriteText(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Create_InvokedExportCommand_WithIncludeBroker_PassesFlagToHandlerFlow()
    {
        var runtimeFactoryMock = new Mock<IRabbitMqRuntimeServiceFactory>(MockBehavior.Strict);
        var topologyDocumentWriterMock = new Mock<ITopologyDocumentWriter>(MockBehavior.Strict);
        var outputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);
        var workflowMock = new Mock<ITopologyWorkflowService>(MockBehavior.Strict);

        var exportedDocument = new TopologyDocument
        {
            VirtualHosts = [new VirtualHostDocument { Name = "sales" }],
        };

        workflowMock.Setup(service => service.ExportAsync(It.IsAny<CancellationToken>())).ReturnsAsync(exportedDocument);
        runtimeFactoryMock
            .Setup(factory => factory.Create(It.Is<RabbitMqManagementOptions>(options =>
                options.BaseUri == new Uri("http://rabbit:15672/api/") &&
                options.Username == "cli-user" &&
                options.Password == "cli-pass" &&
                options.ManagedVirtualHosts.SequenceEqual(new[] { "sales" }))))
            .Returns(CreateRuntimeServices(workflowMock.Object));
        topologyDocumentWriterMock
            .Setup(writer => writer.WriteAsync(
                It.Is<TopologyDocument>(document =>
                    document.Broker != null &&
                    document.Broker.ManagementUrl == "http://rabbit:15672/api/" &&
                    document.Broker.Username == "cli-user"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("broker:\n  managementUrl: http://rabbit:15672/api/\n");
        outputWriterMock.Setup(writer => writer.WriteText(It.IsAny<string>()));

        using var services = new ServiceCollection()
            .AddSingleton<ITopologyTemplateCatalog>(CreateTemplateCatalog())
            .AddSingleton(CreateHandler(
                runtimeFactory: runtimeFactoryMock.Object,
                documentWriter: topologyDocumentWriterMock.Object,
                outputWriter: outputWriterMock.Object))
            .BuildServiceProvider();

        var rootCommand = TopologyRootCommandFactory.Create(services);
        var exitCode = await rootCommand.InvokeAsync([
            "export",
            "--management-url", "http://rabbit:15672/api/",
            "--username", "cli-user",
            "--password", "cli-pass",
            "--vhost", "sales",
            "--include-broker",
            "--output-file", "-",
        ]);

        Assert.Equal(CommandExitCodes.Success, exitCode);
        topologyDocumentWriterMock.VerifyAll();
    }

    [Fact]
    public async Task Create_InvokedCompletionCommand_WritesZshScript()
    {
        var outputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);
        var templateCatalogMock = new Mock<ITopologyTemplateCatalog>(MockBehavior.Strict);

        templateCatalogMock.Setup(catalog => catalog.GetTemplateNames()).Returns(["minimal", "retry"]);
        outputWriterMock.Setup(writer => writer.WriteText(It.IsAny<string>()));

        using var services = new ServiceCollection()
            .AddSingleton<ITopologyTemplateCatalog>(templateCatalogMock.Object)
            .AddSingleton(CreateHandler(outputWriter: outputWriterMock.Object, templateCatalog: templateCatalogMock.Object))
            .BuildServiceProvider();

        var rootCommand = TopologyRootCommandFactory.Create(services);
        var exitCode = await rootCommand.InvokeAsync(["completion", "zsh"]);

        Assert.Equal(CommandExitCodes.Success, exitCode);
        outputWriterMock.Verify(writer => writer.WriteText(It.Is<string>(text =>
            text.Contains("bashcompinit", StringComparison.Ordinal) &&
            text.Contains("complete -F _sprmq_completion sprmq", StringComparison.Ordinal))), Times.Once);
    }

    [Fact]
    public async Task Create_InvokedInitCommand_WritesTemplateToRequestedFile()
    {
        var outputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);
        var templateCatalogMock = new Mock<ITopologyTemplateCatalog>(MockBehavior.Strict);
        var outputPath = Path.Combine(Path.GetTempPath(), $"{Ulid.NewUlid()}.yaml");

        try
        {
            templateCatalogMock.Setup(catalog => catalog.GetTemplateNames()).Returns(["minimal"]);
            templateCatalogMock.Setup(catalog => catalog.GetTemplateContent("minimal")).Returns("virtualHosts:\n  - name: sales\n");
            outputWriterMock.Setup(writer => writer.WriteText(It.IsAny<string>()));

            using var services = new ServiceCollection()
                .AddSingleton<ITopologyTemplateCatalog>(templateCatalogMock.Object)
                .AddSingleton(CreateHandler(outputWriter: outputWriterMock.Object, templateCatalog: templateCatalogMock.Object))
                .BuildServiceProvider();

            var rootCommand = TopologyRootCommandFactory.Create(services);
            var exitCode = await rootCommand.InvokeAsync([
                "init",
                "--template", "minimal",
                "--output-file", outputPath,
            ]);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal("virtualHosts:\n  - name: sales\n", await File.ReadAllTextAsync(outputPath));
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public async Task Create_InvokedValidateCommand_ExecutesValidateHandlerLambda()
    {
        var parserMock = new Mock<ITopologyParser>(MockBehavior.Strict);
        var normalizerMock = new Mock<ITopologyNormalizer>(MockBehavior.Strict);
        var validatorMock = new Mock<ITopologyValidator>(MockBehavior.Strict);
        var outputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);

        var filePath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(filePath, "topology: ignored");

            var document = new TopologyDocument { VirtualHosts = [new VirtualHostDocument { Name = "sales" }] };
            var definition = new TopologyDefinition([new VirtualHostDefinition("sales")]);

            parserMock.Setup(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync(document);
            normalizerMock.Setup(normalizer => normalizer.NormalizeAsync(document, It.IsAny<CancellationToken>())).ReturnsAsync(definition);
            validatorMock.Setup(validator => validator.ValidateAsync(definition, It.IsAny<CancellationToken>())).ReturnsAsync(TopologyValidationResult.Success);
            outputWriterMock.Setup(writer => writer.WriteJson(It.IsAny<ValidateCommandResult>()));

            using var services = new ServiceCollection()
                .AddSingleton<ITopologyTemplateCatalog>(CreateTemplateCatalog())
                .AddSingleton(CreateHandler(
                    parserMock.Object,
                    normalizerMock.Object,
                    validatorMock.Object,
                    Mock.Of<IRabbitMqRuntimeServiceFactory>(),
                    Mock.Of<ITopologyDocumentWriter>(),
                    outputWriterMock.Object))
                .BuildServiceProvider();

            var rootCommand = TopologyRootCommandFactory.Create(services);
            var exitCode = await rootCommand.InvokeAsync([
                "validate",
                "--file", filePath,
                "--output", "json",
                "--verbose",
            ]);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            outputWriterMock.Verify(writer => writer.WriteJson(It.IsAny<ValidateCommandResult>()), Times.Once);
            outputWriterMock.Verify(writer => writer.WriteText(It.IsAny<string>()), Times.Never);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task Create_InvokedPlanCommand_ExecutesPlanHandlerLambda_WithCliBrokerOptions()
    {
        var parserMock = new Mock<ITopologyParser>(MockBehavior.Strict);
        var runtimeFactoryMock = new Mock<IRabbitMqRuntimeServiceFactory>(MockBehavior.Strict);
        var outputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);
        var workflowMock = new Mock<ITopologyWorkflowService>(MockBehavior.Strict);

        var filePath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(filePath, "topology: ignored");

            parserMock.Setup(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync(new TopologyDocument
            {
                VirtualHosts = [new VirtualHostDocument { Name = "sales" }, new VirtualHostDocument { Name = "ops" }],
            });
            workflowMock
                .Setup(service => service.PlanAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((
                    new TopologyDefinition([new VirtualHostDefinition("sales")]),
                    TopologyValidationResult.Success,
                    new TopologyPlan(Array.Empty<TopologyPlanOperation>())));
            runtimeFactoryMock
                .Setup(factory => factory.Create(It.Is<RabbitMqManagementOptions>(options =>
                    options.BaseUri == new Uri("http://rabbit:15672/api/") &&
                    options.Username == "cli-user" &&
                    options.Password == "cli-pass" &&
                    options.ManagedVirtualHosts.Count == 2 &&
                    options.ManagedVirtualHosts.Contains("sales") &&
                    options.ManagedVirtualHosts.Contains("ops"))))
                .Returns(CreateRuntimeServices(workflowMock.Object));
            outputWriterMock.Setup(writer => writer.WriteText(It.IsAny<string>()));

            using var services = new ServiceCollection()
                .AddSingleton<ITopologyTemplateCatalog>(CreateTemplateCatalog())
                .AddSingleton(CreateHandler(
                    parserMock.Object,
                    Mock.Of<ITopologyNormalizer>(),
                    Mock.Of<ITopologyValidator>(),
                    runtimeFactoryMock.Object,
                    Mock.Of<ITopologyDocumentWriter>(),
                    outputWriterMock.Object))
                .BuildServiceProvider();

            var rootCommand = TopologyRootCommandFactory.Create(services);
            var exitCode = await rootCommand.InvokeAsync([
                "plan",
                "--file", filePath,
                "--management-url", "http://rabbit:15672/api/",
                "--username", "cli-user",
                "--password", "cli-pass",
                "--vhost", "sales",
                "--vhost", "ops",
            ]);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            runtimeFactoryMock.VerifyAll();
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task Create_InvokedApplyCommand_ExecutesApplyHandlerLambda_WithMigrateEnabled()
    {
        var parserMock = new Mock<ITopologyParser>(MockBehavior.Strict);
        var runtimeFactoryMock = new Mock<IRabbitMqRuntimeServiceFactory>(MockBehavior.Strict);
        var outputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);
        var workflowMock = new Mock<ITopologyWorkflowService>(MockBehavior.Strict);

        var filePath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(filePath, "topology: ignored");

            parserMock.Setup(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync(new TopologyDocument
            {
                VirtualHosts = [new VirtualHostDocument { Name = "sales" }],
            });
            workflowMock
                .Setup(service => service.PlanAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((
                    new TopologyDefinition([new VirtualHostDefinition("sales")]),
                    TopologyValidationResult.Success,
                    new TopologyPlan(
                    [
                        new TopologyPlanOperation(
                            TopologyPlanOperationKind.DestructiveChange,
                            TopologyResourceKind.Queue,
                            "/virtualHosts/sales/queues/orders",
                            "Queue 'orders' requires delete/recreate because immutable properties changed."),
                    ],
                    destructiveChanges:
                    [
                        new DestructiveChangeWarning("/virtualHosts/sales/queues/orders", "Queue 'orders' requires delete/recreate because immutable properties changed."),
                    ])));
            workflowMock
                .Setup(service => service.ApplyAsync(
                    It.IsAny<Stream>(),
                    It.Is<TopologyApplyOptions?>(options => options != null && options.AllowMigrations),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((
                    new TopologyDefinition([new VirtualHostDefinition("sales")]),
                    TopologyValidationResult.Success,
                    new TopologyPlan(Array.Empty<TopologyPlanOperation>())));
            runtimeFactoryMock
                .Setup(factory => factory.Create(It.IsAny<RabbitMqManagementOptions>()))
                .Returns(CreateRuntimeServices(workflowMock.Object));
            outputWriterMock.Setup(writer => writer.WriteText(It.IsAny<string>()));

            using var services = new ServiceCollection()
                .AddSingleton<ITopologyTemplateCatalog>(CreateTemplateCatalog())
                .AddSingleton(CreateHandler(
                    parserMock.Object,
                    Mock.Of<ITopologyNormalizer>(),
                    Mock.Of<ITopologyValidator>(),
                    runtimeFactoryMock.Object,
                    Mock.Of<ITopologyDocumentWriter>(),
                    outputWriterMock.Object))
                .BuildServiceProvider();

            var rootCommand = TopologyRootCommandFactory.Create(services);
            var exitCode = await rootCommand.InvokeAsync([
                "apply",
                "--file", filePath,
                "--migrate",
            ]);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            workflowMock.Verify(service => service.ApplyAsync(
                It.IsAny<Stream>(),
                It.Is<TopologyApplyOptions?>(options => options != null && options.AllowMigrations),
                It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task Create_InvokedDestroyCommand_ExecutesDestroyHandlerLambda_ForFullVirtualHostDeletion()
    {
        var parserMock = new Mock<ITopologyParser>(MockBehavior.Strict);
        var runtimeFactoryMock = new Mock<IRabbitMqRuntimeServiceFactory>(MockBehavior.Strict);
        var outputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);
        var workflowMock = new Mock<ITopologyWorkflowService>(MockBehavior.Strict);
        var destructivePrompterMock = new Mock<IDestructiveCommandPrompter>(MockBehavior.Strict);

        var filePath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(filePath, "topology: ignored");

            parserMock.Setup(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync(new TopologyDocument
            {
                VirtualHosts = [new VirtualHostDocument { Name = "sales" }],
            });
            workflowMock
                .Setup(service => service.DestroyAsync(It.IsAny<Stream>(), true, It.IsAny<CancellationToken>()))
                .ReturnsAsync((
                    new TopologyDefinition([new VirtualHostDefinition("sales")]),
                    TopologyValidationResult.Success,
                    new TopologyPlan(Array.Empty<TopologyPlanOperation>())));
            runtimeFactoryMock
                .Setup(factory => factory.Create(It.IsAny<RabbitMqManagementOptions>()))
                .Returns(CreateRuntimeServices(workflowMock.Object));
            outputWriterMock.Setup(writer => writer.WriteText(It.IsAny<string>()));
            destructivePrompterMock
                .Setup(prompter => prompter.ConfirmAsync("destroy", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            using var services = new ServiceCollection()
                .AddSingleton<ITopologyTemplateCatalog>(CreateTemplateCatalog())
                .AddSingleton(CreateHandler(
                    parserMock.Object,
                    Mock.Of<ITopologyNormalizer>(),
                    Mock.Of<ITopologyValidator>(),
                    runtimeFactoryMock.Object,
                    Mock.Of<ITopologyDocumentWriter>(),
                    outputWriterMock.Object,
                    destructiveCommandPrompter: destructivePrompterMock.Object))
                .BuildServiceProvider();

            var rootCommand = TopologyRootCommandFactory.Create(services);
            var exitCode = await rootCommand.InvokeAsync([
                "destroy",
                "--file", filePath,
                "--allow-destructive",
            ]);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            workflowMock.Verify(service => service.DestroyAsync(It.IsAny<Stream>(), true, It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task Create_InvokedPurgeCommand_PurgesNormalizedQueues()
    {
        var parserMock = new Mock<ITopologyParser>(MockBehavior.Strict);
        var normalizerMock = new Mock<ITopologyNormalizer>(MockBehavior.Strict);
        var validatorMock = new Mock<ITopologyValidator>(MockBehavior.Strict);
        var runtimeFactoryMock = new Mock<IRabbitMqRuntimeServiceFactory>(MockBehavior.Strict);
        var outputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);
        var apiClientMock = new Mock<IRabbitMqManagementApiClient>(MockBehavior.Strict);
        var destructivePrompterMock = new Mock<IDestructiveCommandPrompter>(MockBehavior.Strict);

        var filePath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(filePath, "topology: ignored");

            var topologyDocument = new TopologyDocument
            {
                VirtualHosts = [new VirtualHostDocument { Name = "sales" }],
            };
            var topologyDefinition = new TopologyDefinition(
            [
                new VirtualHostDefinition(
                    "sales",
                    Array.Empty<ExchangeDefinition>(),
                    [
                        new QueueDefinition("orders"),
                        new QueueDefinition("orders.debug"),
                    ],
                    Array.Empty<BindingDefinition>()),
            ]);

            parserMock.Setup(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync(topologyDocument);
            normalizerMock.Setup(normalizer => normalizer.NormalizeAsync(topologyDocument, It.IsAny<CancellationToken>())).ReturnsAsync(topologyDefinition);
            validatorMock.Setup(validator => validator.ValidateAsync(topologyDefinition, It.IsAny<CancellationToken>())).ReturnsAsync(TopologyValidationResult.Success);
            apiClientMock.Setup(client => client.PurgeQueueAsync("sales", "orders", It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
            apiClientMock.Setup(client => client.PurgeQueueAsync("sales", "orders.debug", It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
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
                    apiClientMock.Object,
                    Mock.Of<global::SphereRabbitMQ.IaC.Application.Broker.Interfaces.IBrokerTopologyReader>(),
                    Mock.Of<global::SphereRabbitMQ.IaC.Application.Apply.Interfaces.ITopologyApplier>(),
                    Mock.Of<global::SphereRabbitMQ.IaC.Application.Export.Interfaces.ITopologyExporter>(),
                    Mock.Of<ITopologyWorkflowService>()));
            outputWriterMock.Setup(writer => writer.WriteText(It.IsAny<string>()));
            destructivePrompterMock
                .Setup(prompter => prompter.ConfirmAsync("purge", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            using var services = new ServiceCollection()
                .AddSingleton<ITopologyTemplateCatalog>(CreateTemplateCatalog())
                .AddSingleton(CreateHandler(
                    parserMock.Object,
                    normalizerMock.Object,
                    validatorMock.Object,
                    runtimeFactoryMock.Object,
                    Mock.Of<ITopologyDocumentWriter>(),
                    outputWriterMock.Object,
                    destructiveCommandPrompter: destructivePrompterMock.Object))
                .BuildServiceProvider();

            var rootCommand = TopologyRootCommandFactory.Create(services);
            var exitCode = await rootCommand.InvokeAsync([
                "purge",
                "--file", filePath,
                "--allow-destructive",
            ]);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            apiClientMock.Verify(client => client.PurgeQueueAsync("sales", "orders", It.IsAny<CancellationToken>()), Times.Once);
            apiClientMock.Verify(client => client.PurgeQueueAsync("sales", "orders.debug", It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task Create_InvokedDestroyCommand_WithAutoApprove_SkipsConfirmationPrompt()
    {
        var parserMock = new Mock<ITopologyParser>(MockBehavior.Strict);
        var runtimeFactoryMock = new Mock<IRabbitMqRuntimeServiceFactory>(MockBehavior.Strict);
        var outputWriterMock = new Mock<ICommandOutputWriter>(MockBehavior.Strict);
        var workflowMock = new Mock<ITopologyWorkflowService>(MockBehavior.Strict);
        var destructivePrompterMock = new Mock<IDestructiveCommandPrompter>(MockBehavior.Strict);

        var filePath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(filePath, "topology: ignored");

            parserMock.Setup(parser => parser.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync(new TopologyDocument
            {
                VirtualHosts = [new VirtualHostDocument { Name = "sales" }],
            });
            workflowMock
                .Setup(service => service.DestroyAsync(It.IsAny<Stream>(), true, It.IsAny<CancellationToken>()))
                .ReturnsAsync((
                    new TopologyDefinition([new VirtualHostDefinition("sales")]),
                    TopologyValidationResult.Success,
                    new TopologyPlan(Array.Empty<TopologyPlanOperation>())));
            runtimeFactoryMock
                .Setup(factory => factory.Create(It.IsAny<RabbitMqManagementOptions>()))
                .Returns(CreateRuntimeServices(workflowMock.Object));
            outputWriterMock.Setup(writer => writer.WriteText(It.IsAny<string>()));

            using var services = new ServiceCollection()
                .AddSingleton<ITopologyTemplateCatalog>(CreateTemplateCatalog())
                .AddSingleton(CreateHandler(
                    parserMock.Object,
                    Mock.Of<ITopologyNormalizer>(),
                    Mock.Of<ITopologyValidator>(),
                    runtimeFactoryMock.Object,
                    Mock.Of<ITopologyDocumentWriter>(),
                    outputWriterMock.Object,
                    destructiveCommandPrompter: destructivePrompterMock.Object))
                .BuildServiceProvider();

            var rootCommand = TopologyRootCommandFactory.Create(services);
            var exitCode = await rootCommand.InvokeAsync([
                "destroy",
                "--file", filePath,
                "--allow-destructive",
                "--auto-approve",
            ]);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            destructivePrompterMock.Verify(prompter => prompter.ConfirmAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static Command GetCommand(RootCommand rootCommand, string name)
        => rootCommand.Subcommands.Single(command => command.Name == name);

    private static TopologyCommandHandler CreateHandler(
        ITopologyParser? parser = null,
        ITopologyNormalizer? normalizer = null,
        ITopologyValidator? validator = null,
        IRabbitMqRuntimeServiceFactory? runtimeFactory = null,
        ITopologyDocumentWriter? documentWriter = null,
        ICommandOutputWriter? outputWriter = null,
        IDestructiveCommandPrompter? destructiveCommandPrompter = null,
        ITopologyTemplateCatalog? templateCatalog = null)
        => new(
            parser ?? Mock.Of<ITopologyParser>(),
            normalizer ?? Mock.Of<ITopologyNormalizer>(),
            validator ?? Mock.Of<ITopologyValidator>(),
            runtimeFactory ?? Mock.Of<IRabbitMqRuntimeServiceFactory>(),
            documentWriter ?? Mock.Of<ITopologyDocumentWriter>(),
            outputWriter ?? Mock.Of<ICommandOutputWriter>(),
            destructiveCommandPrompter ?? Mock.Of<IDestructiveCommandPrompter>(),
            templateCatalog ?? CreateTemplateCatalog());

    private static ITopologyTemplateCatalog CreateTemplateCatalog()
    {
        var templateCatalogMock = new Mock<ITopologyTemplateCatalog>(MockBehavior.Strict);
        templateCatalogMock.Setup(catalog => catalog.GetTemplateNames()).Returns(["debug", "minimal", "quorum", "retry", "retry-dead-letter", "topic-routing"]);
        templateCatalogMock.Setup(catalog => catalog.GetTemplateContent("minimal")).Returns("virtualHosts:\n  - name: sales\n");
        return templateCatalogMock.Object;
    }

    private static RabbitMqRuntimeServices CreateRuntimeServices(ITopologyWorkflowService workflowService)
        => new(
            new HttpClient(),
            new RabbitMqManagementOptions
            {
                BaseUri = new Uri("http://localhost:15672/api/"),
                Username = "guest",
                Password = "guest",
            },
            Mock.Of<IRabbitMqManagementApiClient>(),
            Mock.Of<global::SphereRabbitMQ.IaC.Application.Broker.Interfaces.IBrokerTopologyReader>(),
            Mock.Of<global::SphereRabbitMQ.IaC.Application.Apply.Interfaces.ITopologyApplier>(),
            Mock.Of<global::SphereRabbitMQ.IaC.Application.Export.Interfaces.ITopologyExporter>(),
            workflowService);
}
