using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SphereRabbitMQ.Abstractions.Consumers;

namespace SphereRabbitMQ.DependencyInjection.Consumers;

internal sealed class RabbitMqConsumersHostedService : BackgroundService
{
    private readonly IEnumerable<IRabbitConsumerRegistration> _consumerRegistrations;
    private readonly ILogger<RabbitMqConsumersHostedService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISubscriber _subscriber;

    public RabbitMqConsumersHostedService(
        IEnumerable<IRabbitConsumerRegistration> consumerRegistrations,
        IServiceProvider serviceProvider,
        ISubscriber subscriber,
        ILogger<RabbitMqConsumersHostedService> logger)
    {
        _consumerRegistrations = consumerRegistrations;
        _serviceProvider = serviceProvider;
        _subscriber = subscriber;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriptions = _consumerRegistrations
            .Select(registration =>
            {
                _logger.LogInformation("Starting RabbitMQ consumer for queue {QueueName}.", registration.QueueName);
                return registration.SubscribeAsync(_serviceProvider, _subscriber, stoppingToken);
            })
            .ToArray();

        return Task.WhenAll(subscriptions);
    }
}
