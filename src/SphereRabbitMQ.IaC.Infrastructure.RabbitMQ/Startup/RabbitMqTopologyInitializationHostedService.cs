using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.IaC.Application.Apply;
using SphereRabbitMQ.IaC.Application.Normalization.Interfaces;
using SphereRabbitMQ.IaC.Application.Parsing.Interfaces;
using SphereRabbitMQ.IaC.Application.Validation.Interfaces;
using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Runtime.Interfaces;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Startup.Interfaces;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Startup;

internal sealed class RabbitMqTopologyInitializationHostedService : IHostedService
{
    private const int DefaultManagementPort = 15672;
    private const string DefaultManagementPath = "api/";

    private readonly IRabbitMqRuntimeServiceFactory _runtimeServiceFactory;
    private readonly ILogger<RabbitMqTopologyInitializationHostedService> _logger;
    private readonly SphereRabbitMqOptions _runtimeOptions;
    private readonly RabbitMqTopologyInitializationOptions _initializationOptions;
    private readonly ITopologyParser _topologyParser;
    private readonly ITopologyNormalizer _topologyNormalizer;
    private readonly ITopologyValidator _topologyValidator;
    private readonly IRuntimeTopologyYamlContractValidator _runtimeTopologyYamlContractValidator;

    public RabbitMqTopologyInitializationHostedService(
        IRabbitMqRuntimeServiceFactory runtimeServiceFactory,
        IOptions<SphereRabbitMqOptions> runtimeOptions,
        IOptions<RabbitMqTopologyInitializationOptions> initializationOptions,
        ITopologyParser topologyParser,
        ITopologyNormalizer topologyNormalizer,
        ITopologyValidator topologyValidator,
        IRuntimeTopologyYamlContractValidator runtimeTopologyYamlContractValidator,
        ILogger<RabbitMqTopologyInitializationHostedService> logger)
    {
        _runtimeServiceFactory = runtimeServiceFactory;
        _logger = logger;
        _runtimeOptions = runtimeOptions.Value;
        _initializationOptions = initializationOptions.Value;
        _topologyParser = topologyParser;
        _topologyNormalizer = topologyNormalizer;
        _topologyValidator = topologyValidator;
        _runtimeTopologyYamlContractValidator = runtimeTopologyYamlContractValidator;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_initializationOptions.Enabled && !_initializationOptions.ValidateRuntimeContractAgainstYaml)
        {
            return;
        }

        var yamlFilePath = ResolveYamlFilePath();
        var definition = await LoadValidatedDefinitionAsync(yamlFilePath, cancellationToken);

        if (_initializationOptions.ValidateRuntimeContractAgainstYaml)
        {
            _logger.LogInformation("Validating RabbitMQ runtime subscriber contract against YAML file {YamlFilePath}.", yamlFilePath);
            _runtimeTopologyYamlContractValidator.Validate(definition);
        }

        if (!_initializationOptions.Enabled)
        {
            return;
        }

        _logger.LogInformation("Applying RabbitMQ topology from YAML file {YamlFilePath}.", yamlFilePath);

        await using var topologyStream = File.OpenRead(yamlFilePath);
        using var runtimeServices = _runtimeServiceFactory.Create(CreateManagementOptions());
        var (_, validation, plan) = await runtimeServices.TopologyWorkflowService.PlanAsync(topologyStream, cancellationToken);

        EnsureValidationSucceeded(validation, yamlFilePath);
        EnsurePlanCanRun(plan, yamlFilePath);

        topologyStream.Position = 0;
        await runtimeServices.TopologyWorkflowService.ApplyAsync(
            topologyStream,
            _initializationOptions.AllowMigrations ? new TopologyApplyOptions { AllowMigrations = true } : TopologyApplyOptions.Safe,
            cancellationToken);

        _logger.LogInformation(
            "RabbitMQ topology initialization completed from {YamlFilePath}. Operations: {OperationCount}. Destructive changes: {DestructiveChangeCount}. Unsupported changes: {UnsupportedChangeCount}.",
            yamlFilePath,
            plan.Operations.Count,
            plan.DestructiveChanges.Count,
            plan.UnsupportedChanges.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<TopologyDefinition> LoadValidatedDefinitionAsync(string yamlFilePath, CancellationToken cancellationToken)
    {
        await using var topologyStream = File.OpenRead(yamlFilePath);
        var topologyDocument = await _topologyParser.ParseAsync(topologyStream, cancellationToken);
        var definition = await _topologyNormalizer.NormalizeAsync(topologyDocument, cancellationToken);
        var validation = await _topologyValidator.ValidateAsync(definition, cancellationToken);
        EnsureValidationSucceeded(validation, yamlFilePath);
        return definition;
    }

    private string ResolveYamlFilePath()
    {
        if (string.IsNullOrWhiteSpace(_initializationOptions.YamlFilePath))
        {
            throw new InvalidOperationException("RabbitMQ topology initialization requires YamlFilePath to be configured.");
        }

        var configuredPath = _initializationOptions.YamlFilePath.Trim();
        var resolvedPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(configuredPath, AppContext.BaseDirectory);

        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"RabbitMQ topology YAML file '{resolvedPath}' was not found.", resolvedPath);
        }

        return resolvedPath;
    }

    private RabbitMqManagementOptions CreateManagementOptions()
    {
        var baseUri = string.IsNullOrWhiteSpace(_initializationOptions.ManagementUrl)
            ? new UriBuilder(Uri.UriSchemeHttp, _runtimeOptions.HostName, DefaultManagementPort, DefaultManagementPath).Uri
            : new Uri(_initializationOptions.ManagementUrl, UriKind.Absolute);
        var managedVirtualHosts = _initializationOptions.ManagedVirtualHosts.Count > 0
            ? _initializationOptions.ManagedVirtualHosts
            : [_runtimeOptions.VirtualHost];

        return new RabbitMqManagementOptions
        {
            BaseUri = baseUri,
            Username = string.IsNullOrWhiteSpace(_initializationOptions.Username) ? _runtimeOptions.UserName : _initializationOptions.Username,
            Password = string.IsNullOrWhiteSpace(_initializationOptions.Password) ? _runtimeOptions.Password : _initializationOptions.Password,
            ManagedVirtualHosts = managedVirtualHosts,
            IncludeSystemArtifacts = _initializationOptions.IncludeSystemArtifacts,
            AmqpHostName = _runtimeOptions.HostName,
            AmqpPort = _runtimeOptions.Port,
            AmqpVirtualHost = _runtimeOptions.VirtualHost,
        };
    }

    private void EnsureValidationSucceeded(TopologyValidationResult validation, string yamlFilePath)
    {
        if (validation.IsValid)
        {
            return;
        }

        var summary = string.Join(
            Environment.NewLine,
            validation.Issues.Select(issue => $"- [{issue.Code}] {issue.Path}: {issue.Message}"));
        throw new InvalidOperationException(
            $"RabbitMQ topology validation failed for '{yamlFilePath}'.{Environment.NewLine}{summary}");
    }

    private void EnsurePlanCanRun(TopologyPlan plan, string yamlFilePath)
    {
        if (_initializationOptions.AllowMigrations)
        {
            return;
        }

        if (plan.CanApply && plan.DestructiveChanges.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"RabbitMQ topology initialization for '{yamlFilePath}' requires migrations or destructive changes approval. Set AllowMigrations=true to enable runtime-assisted topology changes.");
    }
}
