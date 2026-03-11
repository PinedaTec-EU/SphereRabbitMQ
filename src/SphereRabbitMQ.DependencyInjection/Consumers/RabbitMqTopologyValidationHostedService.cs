using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.Abstractions.Topology;

namespace SphereRabbitMQ.DependencyInjection.Consumers;

internal sealed class RabbitMqTopologyValidationHostedService : IHostedService
{
    private readonly ILogger<RabbitMqTopologyValidationHostedService> _logger;
    private readonly SphereRabbitMqOptions _options;
    private readonly IRabbitMqTopologyValidator _topologyValidator;

    public RabbitMqTopologyValidationHostedService(
        IRabbitMqTopologyValidator topologyValidator,
        IOptions<SphereRabbitMqOptions> options,
        ILogger<RabbitMqTopologyValidationHostedService> logger)
    {
        _topologyValidator = topologyValidator;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.ValidateTopologyOnStartup)
        {
            return;
        }

        _logger.LogInformation("Validating RabbitMQ topology before starting consumers.");
        await _topologyValidator.ValidateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
