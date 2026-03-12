using SphereRabbitMQ.Abstractions.Subscribers;
using SphereRabbitMQ.Abstractions.Topology;
using SphereRabbitMQ.Domain.Subscribers;
using SphereRabbitMQ.Domain.Topology;

namespace SphereRabbitMQ.DependencyInjection.Subscribers;

internal sealed class DefaultSubscriberTopologyExpectationProvider : ISubscriberTopologyExpectationProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEnumerable<IRabbitSubscriberRegistration> _subscriberRegistrations;
    private readonly ISubscriberInfrastructureRouteResolver _subscriberInfrastructureRouteResolver;

    public DefaultSubscriberTopologyExpectationProvider(
        IEnumerable<IRabbitSubscriberRegistration> subscriberRegistrations,
        ISubscriberInfrastructureRouteResolver subscriberInfrastructureRouteResolver,
        IServiceProvider serviceProvider)
    {
        _subscriberRegistrations = subscriberRegistrations;
        _subscriberInfrastructureRouteResolver = subscriberInfrastructureRouteResolver;
        _serviceProvider = serviceProvider;
    }

    public TopologyExpectation BuildExpectation()
    {
        var exchanges = new HashSet<string>(StringComparer.Ordinal);
        var queues = new HashSet<string>(StringComparer.Ordinal);

        foreach (var registration in _subscriberRegistrations)
        {
            var definition = registration.BuildDefinition(_serviceProvider);
            queues.Add(definition.QueueName);
            AppendDerivedArtifacts(definition, exchanges, queues);
        }

        return new TopologyExpectation(exchanges.ToArray(), queues.ToArray());
    }

    private void AppendDerivedArtifacts(
        SubscriberDefinition definition,
        ISet<string> exchanges,
        ISet<string> queues)
    {
        if (definition.ErrorHandling.Strategy is SubscriberErrorStrategyKind.RetryThenDeadLetter)
        {
            var retryRoute = definition.ErrorHandling.RetryRoute ?? _subscriberInfrastructureRouteResolver.ResolveRetryRoute(definition.QueueName);
            exchanges.Add(retryRoute.Exchange);
            queues.Add(retryRoute.QueueName ?? retryRoute.RoutingKey);
        }

        if (definition.ErrorHandling.Strategy is SubscriberErrorStrategyKind.DeadLetterOnly or SubscriberErrorStrategyKind.RetryThenDeadLetter)
        {
            var deadLetterRoute = definition.ErrorHandling.DeadLetterRoute ?? _subscriberInfrastructureRouteResolver.ResolveDeadLetterRoute(definition.QueueName);
            exchanges.Add(deadLetterRoute.Exchange);
            queues.Add(deadLetterRoute.QueueName ?? deadLetterRoute.RoutingKey);
        }
    }
}
