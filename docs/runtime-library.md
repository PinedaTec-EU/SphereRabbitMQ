# SphereRabbitMQ Runtime Library

`SphereRabbitMQ` is the application/runtime layer that publishes and consumes against RabbitMQ topology already provisioned by `SphereRabbitMQ.IaC` or any other external provisioning tool.

## Hard Restrictions

The runtime library never creates or mutates:

- virtual hosts
- exchanges
- queues
- bindings

It also does not try to “self-heal” missing retry or dead-letter topology.

If the required broker topology is missing, startup or message processing fails explicitly.

## What The Runtime Does

- manage one shared RabbitMQ connection
- use dedicated channels for consumers
- use a dedicated publisher channel strategy safe for confirms
- publish messages with headers, correlation id, message id, and timestamp
- consume messages with bounded concurrency and manual acknowledgements
- forward retryable failures through broker retry topology
- forward exhausted failures to dead-letter topology
- validate expected topology when configured

## What The Runtime Does Not Do

- no topology declaration
- no in-memory retry loops
- no hidden background requeue logic
- no automatic creation of retry exchanges or queues
- no automatic creation of dead-letter exchanges or queues

## Architecture

Projects:

- `src/SphereRabbitMQ.Abstractions`
- `src/SphereRabbitMQ.Domain`
- `src/SphereRabbitMQ.Application`
- `src/SphereRabbitMQ.Infrastructure.RabbitMQ`
- `src/SphereRabbitMQ.DependencyInjection`

Layers:

- `Abstractions`: public contracts
- `Domain`: pure runtime models and error semantics
- `Application`: retry and failure decision logic
- `Infrastructure.RabbitMQ`: RabbitMQ client adapters
- `DependencyInjection`: registration helpers

## Concurrency Model

Threading and broker usage are intentionally conservative:

- one shared connection
- one dedicated channel per consumer
- one dedicated serialized publisher channel strategy
- broker prefetch is separate from local consumer concurrency
- acknowledgements are coordinated safely on the consumer channel

This avoids unsafe channel sharing between concurrent operations.

## Publishing Example

```csharp
using Microsoft.Extensions.DependencyInjection;
using SphereRabbitMQ.Abstractions.Publishing;
using SphereRabbitMQ.DependencyInjection;

var services = new ServiceCollection();

services.AddSphereRabbitMq(options =>
{
    options.HostName = "localhost";
    options.Port = 5672;
    options.UserName = "guest";
    options.Password = "guest";
});

await using var provider = services.BuildServiceProvider();
var publisher = provider.GetRequiredService<IPublisher>();

await publisher.PublishAsync(
    exchange: "orders",
    routingKey: "orders.created",
    message: new OrderCreated("order-42"));

public sealed record OrderCreated(string OrderId);
```

## Consumer Example

```csharp
using Microsoft.Extensions.DependencyInjection;
using SphereRabbitMQ.DependencyInjection;

var services = new ServiceCollection();

services.AddSphereRabbitMq(options =>
{
    options.HostName = "localhost";
    options.Port = 5672;
    options.UserName = "guest";
    options.Password = "guest";
    options.ValidateTopologyOnStartup = true;
    options.ExpectedTopology = new(
        Exchanges: ["orders", "orders.retry", "orders.dlx"],
        Queues: ["orders.created", "orders.created.retry", "orders.created.dlq"]);
});

services.AddRabbitConsumer<OrderCreated, OrderCreatedConsumer>(config =>
{
    config.Queue = "orders.created";
    config.Prefetch = 10;
    config.MaxConcurrency = 4;
    config.ErrorHandling.EnableRetry = true;
    config.ErrorHandling.MaxRetryAttempts = 5;
    config.ErrorHandling.RetryExchange = "orders.retry";
    config.ErrorHandling.RetryRoutingKey = "orders.created.retry";
    config.ErrorHandling.RetryQueue = "orders.created.retry";
    config.ErrorHandling.EnableDeadLetter = true;
    config.ErrorHandling.DeadLetterExchange = "orders.dlx";
    config.ErrorHandling.DeadLetterRoutingKey = "orders.created.dlq";
    config.ErrorHandling.DeadLetterQueue = "orders.created.dlq";
});
```

```csharp
using SphereRabbitMQ.Abstractions.Consumers;
using SphereRabbitMQ.Domain.Messaging;

public sealed record OrderCreated(string OrderId);

public sealed class OrderCreatedConsumer : IRabbitMessageHandler<OrderCreated>
{
    public Task HandleAsync(MessageEnvelope<OrderCreated> message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
```

## Error Handling Semantics

The runtime separates:

- handler failure
- retry decision
- final disposition

### Default Dispositions

- retryable failure -> `Retry`
- retries exhausted with DLQ configured -> `DeadLetter`
- explicit discard -> `Discard`

### Built-In Exceptions

`NonRetriableMessageException`

- skips retry even if retry is enabled
- goes to DLQ if configured
- otherwise falls back to discard

`DiscardMessageException`

- skips retry
- skips dead-letter
- message is acknowledged and discarded intentionally

### Additional Non-Retryable Exceptions

Custom exception types can also be configured:

```csharp
config.ErrorHandling.NonRetriableExceptions.Add(typeof(ValidationException));
config.ErrorHandling.NonRetriableExceptions.Add(typeof(DomainRuleViolationException));
```

## Retry And Dead-Letter Requirements

Broker-based retry only works if the topology already exists.

When retry is configured for a consumer, the runtime expects:

- retry exchange exists
- retry queue exists

When dead-letter is configured for a consumer, the runtime expects:

- dead-letter exchange exists
- dead-letter queue exists

If any of those are missing, `SubscribeAsync` fails explicitly.

## Example Failure Flow

A common flow looks like this:

1. Message arrives on `orders.created`.
2. Handler throws `InvalidOperationException`.
3. Retry policy says it is retryable.
4. Runtime republishes to `orders.retry` / `orders.created.retry`.
5. Broker TTL queue waits.
6. Broker dead-letters the message back to the main route.
7. Handler fails again.
8. Retry limit is exhausted.
9. Runtime forwards to `orders.dlx` / `orders.created.dlq`.
10. Original delivery is acked after the forward succeeds.

## Topology Validation

Startup validation is optional but recommended.

```csharp
services.AddSphereRabbitMq(options =>
{
    options.ValidateTopologyOnStartup = true;
    options.ExpectedTopology = new(
        Exchanges: ["orders", "orders.retry", "orders.dlx"],
        Queues: ["orders.created", "orders.created.retry", "orders.created.dlq"]);
});
```

This validation is read-only. It does not declare anything.

## Internal Message Moving

The runtime contains an internal queue mover used by IaC-assisted migrations.

It is not the main public API for applications. Applications should think in terms of publish/consume/replay semantics, not arbitrary queue-to-queue movement.

## Tested Guarantees

Integration tests cover:

- publish and consume against a real RabbitMQ broker
- broker-based retry
- dead-letter forwarding
- bounded consumer concurrency
- explicit failure when exchange or queue is missing
- explicit failure when retry topology is missing
- explicit failure when dead-letter topology is missing
- `NonRetriableMessageException`
- `DiscardMessageException`

## Validation Commands

- `dotnet test tests/SphereRabbitMQ.Tests.Unit/SphereRabbitMQ.Tests.Unit.csproj`
- `dotnet test tests/SphereRabbitMQ.Tests.Integration/SphereRabbitMQ.Tests.Integration.csproj`
