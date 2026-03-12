namespace SphereRabbitMQ.DependencyInjection.Publishing;

public sealed class RabbitPublisherRegistrationBuilder<TMessage>
{
    public string Exchange { get; private set; } = string.Empty;

    public string RoutingKey { get; private set; } = string.Empty;

    public RabbitPublisherRegistrationBuilder<TMessage> ToExchange(string exchange)
    {
        Exchange = exchange;
        return this;
    }

    public RabbitPublisherRegistrationBuilder<TMessage> WithRoutingKey(string routingKey)
    {
        RoutingKey = routingKey;
        return this;
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Exchange))
        {
            throw new InvalidOperationException($"Rabbit publisher '{typeof(TMessage).Name}' requires an exchange.");
        }

        if (string.IsNullOrWhiteSpace(RoutingKey))
        {
            throw new InvalidOperationException($"Rabbit publisher '{typeof(TMessage).Name}' requires a routing key.");
        }
    }
}
