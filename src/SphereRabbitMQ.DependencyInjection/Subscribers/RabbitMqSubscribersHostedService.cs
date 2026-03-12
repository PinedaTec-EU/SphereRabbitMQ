using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SphereRabbitMQ.Abstractions.Subscribers;

namespace SphereRabbitMQ.DependencyInjection.Subscribers;

internal sealed class RabbitMqSubscribersHostedService : BackgroundService
{
    private readonly IEnumerable<IRabbitSubscriberRegistration> _subscriberRegistrations;
    private readonly ILogger<RabbitMqSubscribersHostedService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IRabbitMQSubscriber _subscriber;

    public RabbitMqSubscribersHostedService(
        IEnumerable<IRabbitSubscriberRegistration> subscriberRegistrations,
        IServiceProvider serviceProvider,
        IRabbitMQSubscriber subscriber,
        ILogger<RabbitMqSubscribersHostedService> logger)
    {
        _subscriberRegistrations = subscriberRegistrations;
        _serviceProvider = serviceProvider;
        _subscriber = subscriber;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriptions = _subscriberRegistrations
            .Select(registration =>
            {
                _logger.LogInformation("Starting RabbitMQ subscriber for queue {QueueName}.", registration.QueueName);
                return registration.SubscribeAsync(_serviceProvider, _subscriber, stoppingToken);
            })
            .ToArray();

        return Task.WhenAll(subscriptions);
    }
}
