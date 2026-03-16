using Microsoft.Extensions.Options;

using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.Abstractions.Subscribers;
using SphereRabbitMQ.DependencyInjection.Subscribers;
using SphereRabbitMQ.Domain.Subscribers;
using SphereRabbitMQ.IaC.Domain.Topology;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Startup.Interfaces;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Startup;

internal sealed class RuntimeTopologyYamlContractValidator : IRuntimeTopologyYamlContractValidator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SphereRabbitMqOptions _runtimeOptions;
    private readonly IEnumerable<IRabbitSubscriberRegistration> _subscriberRegistrations;
    private readonly ISubscriberInfrastructureRouteResolver _subscriberInfrastructureRouteResolver;

    public RuntimeTopologyYamlContractValidator(
        IEnumerable<IRabbitSubscriberRegistration> subscriberRegistrations,
        ISubscriberInfrastructureRouteResolver subscriberInfrastructureRouteResolver,
        IServiceProvider serviceProvider,
        IOptions<SphereRabbitMqOptions> runtimeOptions)
    {
        _subscriberRegistrations = subscriberRegistrations;
        _subscriberInfrastructureRouteResolver = subscriberInfrastructureRouteResolver;
        _serviceProvider = serviceProvider;
        _runtimeOptions = runtimeOptions.Value;
    }

    public void Validate(TopologyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var virtualHost = definition.VirtualHosts.SingleOrDefault(candidate =>
            string.Equals(candidate.Name, _runtimeOptions.VirtualHost, StringComparison.Ordinal));

        if (virtualHost is null)
        {
            throw new InvalidOperationException(
                $"RabbitMQ topology YAML does not define the runtime virtual host '{_runtimeOptions.VirtualHost}'.");
        }

        var issues = new List<string>();

        foreach (var registration in _subscriberRegistrations)
        {
            var subscriberDefinition = registration.BuildDefinition(_serviceProvider);
            var queueDefinition = virtualHost.Queues.SingleOrDefault(queue =>
                string.Equals(queue.Name, subscriberDefinition.QueueName, StringComparison.Ordinal));

            if (queueDefinition is null)
            {
                issues.Add(
                    $"Subscriber queue '{subscriberDefinition.QueueName}' is not declared in YAML virtual host '{virtualHost.Name}'.");
                continue;
            }

            ValidateSubscriberAgainstQueue(definition.NamingPolicy, subscriberDefinition, queueDefinition, issues);
        }

        if (issues.Count == 0)
        {
            return;
        }

        var summary = string.Join(Environment.NewLine, issues.Select(issue => $"- {issue}"));
        throw new InvalidOperationException(
            $"RabbitMQ runtime topology contract validation against YAML failed.{Environment.NewLine}{summary}");
    }

    private void ValidateSubscriberAgainstQueue(
        NamingConventionPolicy namingPolicy,
        SubscriberDefinition subscriberDefinition,
        QueueDefinition queueDefinition,
        ICollection<string> issues)
    {
        switch (subscriberDefinition.ErrorHandling.Strategy)
        {
            case SubscriberErrorStrategyKind.Discard:
                ValidateDiscardStrategy(queueDefinition, issues);
                return;
            case SubscriberErrorStrategyKind.DeadLetterOnly:
                ValidateDeadLetterOnlyStrategy(namingPolicy, subscriberDefinition, queueDefinition, issues);
                return;
            case SubscriberErrorStrategyKind.RetryThenDeadLetter:
                ValidateRetryThenDeadLetterStrategy(namingPolicy, subscriberDefinition, queueDefinition, issues);
                return;
            default:
                throw new InvalidOperationException(
                    $"Unsupported subscriber error strategy '{subscriberDefinition.ErrorHandling.Strategy}'.");
        }
    }

    private void ValidateDiscardStrategy(QueueDefinition queueDefinition, ICollection<string> issues)
    {
        if (queueDefinition.Retry is { Enabled: true })
        {
            issues.Add(
                $"Subscriber queue '{queueDefinition.Name}' uses discard, but YAML enables retry for that queue.");
        }

        if (queueDefinition.DeadLetter is { Enabled: true })
        {
            issues.Add(
                $"Subscriber queue '{queueDefinition.Name}' uses discard, but YAML enables dead-letter for that queue.");
        }
    }

    private void ValidateDeadLetterOnlyStrategy(
        NamingConventionPolicy namingPolicy,
        SubscriberDefinition subscriberDefinition,
        QueueDefinition queueDefinition,
        ICollection<string> issues)
    {
        if (queueDefinition.Retry is { Enabled: true })
        {
            issues.Add(
                $"Subscriber queue '{queueDefinition.Name}' uses dead-letter only, but YAML also enables retry for that queue.");
        }

        if (queueDefinition.DeadLetter is not { Enabled: true } deadLetterDefinition)
        {
            issues.Add(
                $"Subscriber queue '{queueDefinition.Name}' expects dead-letter topology, but YAML does not enable dead-letter for that queue.");
            return;
        }

        ValidateDeadLetterRoute(namingPolicy, subscriberDefinition, queueDefinition, deadLetterDefinition, issues);
    }

    private void ValidateRetryThenDeadLetterStrategy(
        NamingConventionPolicy namingPolicy,
        SubscriberDefinition subscriberDefinition,
        QueueDefinition queueDefinition,
        ICollection<string> issues)
    {
        if (queueDefinition.Retry is not { Enabled: true } retryDefinition)
        {
            issues.Add(
                $"Subscriber queue '{queueDefinition.Name}' expects retry topology, but YAML does not enable retry for that queue.");
        }
        else
        {
            ValidateRetryRoute(namingPolicy, subscriberDefinition, queueDefinition, retryDefinition, issues);
        }

        if (queueDefinition.DeadLetter is not { Enabled: true } deadLetterDefinition)
        {
            issues.Add(
                $"Subscriber queue '{queueDefinition.Name}' expects dead-letter topology, but YAML does not enable dead-letter for that queue.");
            return;
        }

        ValidateDeadLetterRoute(namingPolicy, subscriberDefinition, queueDefinition, deadLetterDefinition, issues);
    }

    private void ValidateRetryRoute(
        NamingConventionPolicy namingPolicy,
        SubscriberDefinition subscriberDefinition,
        QueueDefinition queueDefinition,
        RetryDefinition retryDefinition,
        ICollection<string> issues)
    {
        var runtimeRetryRoute = _subscriberInfrastructureRouteResolver.ResolveRetryRoute(subscriberDefinition.QueueName);
        var firstRetryStep = retryDefinition.Steps.FirstOrDefault();

        if (firstRetryStep is null)
        {
            issues.Add(
                $"Subscriber queue '{queueDefinition.Name}' expects retry topology, but YAML retry does not declare any steps.");
            return;
        }

        var retryStepName = firstRetryStep.Name ?? namingPolicy.GetRetryStepToken(0);
        var yamlRetryExchange = retryDefinition.ExchangeName ?? namingPolicy.GetRetryExchangeName(queueDefinition.Name);
        var yamlRetryQueue = firstRetryStep.QueueName ?? namingPolicy.GetRetryQueueName(queueDefinition.Name, retryStepName);
        var yamlRetryRoutingKey = firstRetryStep.RoutingKey ?? yamlRetryQueue;

        if (!string.Equals(runtimeRetryRoute.Exchange, yamlRetryExchange, StringComparison.Ordinal))
        {
            issues.Add(
                $"Subscriber queue '{queueDefinition.Name}' expects retry exchange '{runtimeRetryRoute.Exchange}', but YAML resolves '{yamlRetryExchange}'.");
        }

        var runtimeRetryQueue = runtimeRetryRoute.QueueName ?? runtimeRetryRoute.RoutingKey;
        if (!string.Equals(runtimeRetryQueue, yamlRetryQueue, StringComparison.Ordinal))
        {
            issues.Add(
                $"Subscriber queue '{queueDefinition.Name}' expects retry queue '{runtimeRetryQueue}', but YAML resolves '{yamlRetryQueue}'.");
        }

        if (!string.Equals(runtimeRetryRoute.RoutingKey, yamlRetryRoutingKey, StringComparison.Ordinal))
        {
            issues.Add(
                $"Subscriber queue '{queueDefinition.Name}' expects retry routing key '{runtimeRetryRoute.RoutingKey}', but YAML resolves '{yamlRetryRoutingKey}'.");
        }
    }

    private void ValidateDeadLetterRoute(
        NamingConventionPolicy namingPolicy,
        SubscriberDefinition subscriberDefinition,
        QueueDefinition queueDefinition,
        DeadLetterDefinition deadLetterDefinition,
        ICollection<string> issues)
    {
        if (deadLetterDefinition.DestinationType != DeadLetterDestinationType.Generated)
        {
            issues.Add(
                $"Subscriber queue '{queueDefinition.Name}' expects generated dead-letter exchange/queue topology, but YAML uses destination type '{deadLetterDefinition.DestinationType}'.");
            return;
        }

        var runtimeDeadLetterRoute = _subscriberInfrastructureRouteResolver.ResolveDeadLetterRoute(subscriberDefinition.QueueName);
        var yamlDeadLetterExchange = deadLetterDefinition.ExchangeName ?? namingPolicy.GetDeadLetterExchangeName(queueDefinition.Name);
        var yamlDeadLetterQueue = deadLetterDefinition.QueueName ?? namingPolicy.GetDeadLetterQueueName(queueDefinition.Name);
        var yamlDeadLetterRoutingKey = deadLetterDefinition.RoutingKey ?? queueDefinition.Name;

        if (!string.Equals(runtimeDeadLetterRoute.Exchange, yamlDeadLetterExchange, StringComparison.Ordinal))
        {
            issues.Add(
                $"Subscriber queue '{queueDefinition.Name}' expects dead-letter exchange '{runtimeDeadLetterRoute.Exchange}', but YAML resolves '{yamlDeadLetterExchange}'.");
        }

        var runtimeDeadLetterQueue = runtimeDeadLetterRoute.QueueName ?? runtimeDeadLetterRoute.RoutingKey;
        if (!string.Equals(runtimeDeadLetterQueue, yamlDeadLetterQueue, StringComparison.Ordinal))
        {
            issues.Add(
                $"Subscriber queue '{queueDefinition.Name}' expects dead-letter queue '{runtimeDeadLetterQueue}', but YAML resolves '{yamlDeadLetterQueue}'.");
        }

        if (!string.Equals(runtimeDeadLetterRoute.RoutingKey, yamlDeadLetterRoutingKey, StringComparison.Ordinal))
        {
            issues.Add(
                $"Subscriber queue '{queueDefinition.Name}' expects dead-letter routing key '{runtimeDeadLetterRoute.RoutingKey}', but YAML resolves '{yamlDeadLetterRoutingKey}'.");
        }
    }
}
