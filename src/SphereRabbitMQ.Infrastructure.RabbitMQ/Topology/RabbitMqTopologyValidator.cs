using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client.Exceptions;
using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.Abstractions.Topology;
using SphereRabbitMQ.Domain.Topology;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Connection;

namespace SphereRabbitMQ.Infrastructure.RabbitMQ.Topology;

internal sealed class RabbitMqTopologyValidator : IRabbitMqTopologyValidator
{
    private readonly ILogger<RabbitMqTopologyValidator> _logger;
    private readonly SphereRabbitMqOptions _options;
    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly ISubscriberTopologyExpectationProvider _subscriberTopologyExpectationProvider;

    public RabbitMqTopologyValidator(
        RabbitMqConnectionProvider connectionProvider,
        IOptions<SphereRabbitMqOptions> options,
        ISubscriberTopologyExpectationProvider subscriberTopologyExpectationProvider,
        ILogger<RabbitMqTopologyValidator> logger)
    {
        _connectionProvider = connectionProvider;
        _options = options.Value;
        _subscriberTopologyExpectationProvider = subscriberTopologyExpectationProvider;
        _logger = logger;
    }

    public async Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var expectedTopology = BuildExpectedTopology();

        foreach (var exchange in expectedTopology.Exchanges.OrderBy(name => name, StringComparer.Ordinal))
        {
            try
            {
                await channel.ExchangeDeclarePassiveAsync(exchange, cancellationToken);
            }
            catch (OperationInterruptedException exception)
            {
                throw new InvalidOperationException($"Expected exchange '{exchange}' does not exist in RabbitMQ. SphereRabbitMQ will not declare topology automatically.", exception);
            }
        }

        foreach (var queue in expectedTopology.Queues.OrderBy(name => name, StringComparer.Ordinal))
        {
            try
            {
                await channel.QueueDeclarePassiveAsync(queue, cancellationToken);
            }
            catch (OperationInterruptedException exception)
            {
                throw new InvalidOperationException($"Expected queue '{queue}' does not exist in RabbitMQ. SphereRabbitMQ will not declare topology automatically.", exception);
            }
        }

        _logger.LogInformation("RabbitMQ topology validation completed successfully.");
    }

    private TopologyExpectation BuildExpectedTopology()
    {
        var exchanges = new HashSet<string>(_options.ExpectedTopology.Exchanges, StringComparer.Ordinal);
        var queues = new HashSet<string>(_options.ExpectedTopology.Queues, StringComparer.Ordinal);
        var derivedExpectation = _subscriberTopologyExpectationProvider.BuildExpectation();
        exchanges.UnionWith(derivedExpectation.Exchanges);
        queues.UnionWith(derivedExpectation.Queues);

        return new TopologyExpectation(exchanges.ToArray(), queues.ToArray());
    }
}
