# SphereRabbitMQ Runtime Library

`SphereRabbitMQ` is the runtime companion to `SphereRabbitMQ.IaC`.

It publishes and consumes messages on top of RabbitMQ topology that already exists.

It does not declare or mutate:

- virtual hosts
- exchanges
- queues
- bindings

If expected topology is missing, it fails explicitly.

## Projects

- `src/SphereRabbitMQ.Abstractions`
- `src/SphereRabbitMQ.Domain`
- `src/SphereRabbitMQ.Application`
- `src/SphereRabbitMQ.Infrastructure.RabbitMQ`
- `src/SphereRabbitMQ.DependencyInjection`
- `tests/SphereRabbitMQ.Tests.Unit`
- `tests/SphereRabbitMQ.Tests.Integration`

## Features

- async-first publisher with headers, correlation id, message id, timestamps, and persistent delivery
- async subscriber with manual acknowledgements and configurable prefetch
- broker-based retry forwarding using `x-retry-count`
- dead-letter forwarding without in-memory retry loops
- `System.Text.Json` serializer by default, behind `IMessageSerializer`
- DI registration for publisher, subscriber, topology validator, and hosted consumers
- startup topology validation for expected exchanges and queues

## Example

```csharp
using Microsoft.Extensions.DependencyInjection;
using SphereRabbitMQ.Abstractions.Consumers;
using SphereRabbitMQ.Abstractions.Publishing;
using SphereRabbitMQ.DependencyInjection;
using SphereRabbitMQ.Domain.Messaging;

var services = new ServiceCollection();

services.AddSphereRabbitMq(options =>
{
    options.HostName = "localhost";
    options.ValidateTopologyOnStartup = true;
    options.ExpectedTopology = new(["orders"], ["orders.created"]);
});

services.AddRabbitConsumer<OrderCreated, OrderCreatedConsumer>(config =>
{
    config.Queue = "orders.created";
    config.Prefetch = 10;
    config.FallbackExchange = "orders";
    config.FallbackRoutingKey = "orders.created";
    config.ErrorHandling.EnableRetry = true;
    config.ErrorHandling.MaxRetryAttempts = 5;
    config.ErrorHandling.RetryExchange = "orders.retry";
    config.ErrorHandling.RetryRoutingKey = "orders.created.retry";
    config.ErrorHandling.EnableDeadLetter = true;
    config.ErrorHandling.DeadLetterExchange = "orders.dlx";
    config.ErrorHandling.DeadLetterRoutingKey = "orders.created.dlq";
});

await using var provider = services.BuildServiceProvider();
var publisher = provider.GetRequiredService<IPublisher>();

await publisher.PublishAsync(
    exchange: "orders",
    routingKey: "orders.created",
    message: new OrderCreated("order-42"));

public sealed record OrderCreated(string OrderId);

public sealed class OrderCreatedConsumer : IRabbitMessageHandler<OrderCreated>
{
    public Task HandleAsync(MessageEnvelope<OrderCreated> message, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
```

## Validation

- `dotnet test tests/SphereRabbitMQ.Tests.Unit/SphereRabbitMQ.Tests.Unit.csproj`
- `dotnet test tests/SphereRabbitMQ.Tests.Integration/SphereRabbitMQ.Tests.Integration.csproj`
