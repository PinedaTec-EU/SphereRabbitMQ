# SphereRabbitMQ Runtime Library

`SphereRabbitMQ` is the application/runtime layer that publishes and consumes against RabbitMQ topology already provisioned by `SphereRabbitMQ.IaC` or any other external provisioning tool.

If you also need topology provisioning commands in a developer environment, install the separate `sprmq` CLI as the `SphereRabbitMQ.IaC.Tool` `dotnet tool`. The runtime NuGet package does not install shell commands.

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
- use dedicated channels for subscribers
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
- one dedicated channel per subscriber
- one dedicated serialized publisher channel strategy
- broker prefetch is separate from local subscriber concurrency
- acknowledgements are coordinated safely on the subscriber channel

This avoids unsafe channel sharing between concurrent operations.

## Publishing Example

```csharp
using Microsoft.Extensions.DependencyInjection;
using SphereRabbitMQ.Abstractions.Publishing;
using SphereRabbitMQ.DependencyInjection;

var services = new ServiceCollection();

const string rabbitMqConnectionString = "amqp://guest:guest@localhost:5672/%2f";
const string ordersExchangeName = "orders";
const string orderCreatedRoutingKey = "orders.created";

services.AddSphereRabbitMq(options =>
{
    options.SetConnectionString(rabbitMqConnectionString);
});

services.AddRabbitPublisher<OrderCreated>(config =>
{
    config
        .ToExchange(ordersExchangeName)
        .WithRoutingKey(orderCreatedRoutingKey);
});

await using var provider = services.BuildServiceProvider();
var publisher = provider.GetRequiredService<IMessagePublisher<OrderCreated>>();

await publisher.PublishAsync(new OrderCreated("order-42"));

public sealed record OrderCreated(string OrderId);
```

`IRabbitMQPublisher` remains available as the low-level API for dynamic or infrastructure-heavy scenarios. The recommended application-facing API is `IMessagePublisher<TMessage>`.

## Runtime Configuration Sources

`AddSphereRabbitMq()` now resolves runtime connection settings from environment variables even when the delegate is omitted.

Recognized variables:

- `SPHERE_RABBITMQ_CONNECTION_STRING`
- `SPHERE_RABBITMQ_AMQP_HOST`
- `SPHERE_RABBITMQ_AMQP_PORT`
- `SPHERE_RABBITMQ_AMQP_VHOST`
- `SPHERE_RABBITMQ_USERNAME`
- `SPHERE_RABBITMQ_PASSWORD`
- `SPHERE_RABBITMQ_MANAGEMENT_URL` as a host fallback when `SPHERE_RABBITMQ_AMQP_HOST` is not set

Precedence is:

1. explicit `AddSphereRabbitMq(options => ...)` configuration
2. `SPHERE_RABBITMQ_CONNECTION_STRING`
3. granular `SPHERE_RABBITMQ_AMQP_*` plus credentials
4. built-in defaults (`localhost:5672`, `guest/guest`, `/`)

Example without an explicit delegate:

```csharp
services.AddSphereRabbitMq();
```

If a flow needs to publish the same message type to a different broker route, `IMessagePublisher<TMessage>` also supports an explicit routing-key overload while keeping the configured exchange fixed.

The default routing key is optional. You can register a typed publisher with just the exchange and provide the routing key per call:

```csharp
services.AddRabbitPublisher<OrderCreated>(config =>
{
    config.ToExchange(ordersExchangeName);
});

await publisher.PublishAsync("orders.created.high", new OrderCreated("order-43"));
```

If no default routing key was configured, calling `PublishAsync(message)` throws an `InvalidOperationException`. Passing a null, empty, or whitespace routing-key override throws an `ArgumentException`.

## Subscriber Example

```csharp
using Microsoft.Extensions.DependencyInjection;
using SphereRabbitMQ.DependencyInjection;

var services = new ServiceCollection();

const string rabbitMqConnectionString = "amqp://guest:guest@localhost:5672/%2f";
const string orderCreatedQueueName = "orders.created";
const ushort orderCreatedPrefetchCount = 10;
const int orderCreatedMaxConcurrency = 4;
const int orderCreatedMaxRetryAttempts = 5;

services.AddSphereRabbitMq(options =>
{
    options.SetConnectionString(rabbitMqConnectionString);
    options.ValidateTopologyOnStartup = true;
});

services.AddRabbitSubscriber<OrderCreated, OrderCreatedSubscriber>(
    orderCreatedQueueName,
    config =>
    {
        config
            .WithPrefetchCount(orderCreatedPrefetchCount)
            .WithMaxConcurrency(orderCreatedMaxConcurrency);

        config.ErrorHandling.UseRetryAndDeadLetter(maxRetryAttempts: orderCreatedMaxRetryAttempts);
    });
```

```csharp
using SphereRabbitMQ.Abstractions.Subscribers;
using SphereRabbitMQ.Domain.Messaging;

public sealed record OrderCreated(string OrderId);

public sealed class OrderCreatedSubscriber : IRabbitSubscriberMessageHandler<OrderCreated>
{
    public Task HandleAsync(MessageEnvelope<OrderCreated> message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
```

`IRabbitMQSubscriber` remains available as the low-level API for dynamic or infrastructure-heavy scenarios. The recommended application-facing API is `AddRabbitSubscriber<TMessage, THandler>(...)` with a preconfigured queue and policy.

For advanced scenarios, the library also supports keyed subscribers so the same message type can be handled by multiple independently configured consumers in the same application.

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

When retry is configured for a subscriber, the runtime expects:

- retry exchange exists
- retry queue exists

When dead-letter is configured for a subscriber, the runtime expects:

- dead-letter exchange exists
- dead-letter queue exists

If any of those are missing, `SubscribeAsync` fails explicitly.

## YAML Delay Queue Pattern

When topology is defined through `SphereRabbitMQ.IaC`, a delayed-delivery queue can dead-letter into the final consumer queue explicitly.

Example:

```yaml
virtualHosts:
  - name: sales
    queues:
      - name: orders.delay.5m
        ttl: "00:05:00"
        deadLetter:
          enabled: true
          destinationType: queue
          queueName: orders.consume
      - name: orders.consume
```

This means:

- the application publishes to `orders.delay.5m`
- RabbitMQ keeps the message for five minutes
- after TTL expiration, RabbitMQ dead-letters it to `orders.consume`

Although the intent looks like queue-to-queue routing, RabbitMQ executes this through dead-lettering plus the default exchange. The YAML keeps that intent explicit through `deadLetter.destinationType: queue`.

## Routing Notes

With RabbitMQ `direct` and `topic` exchanges, broker routing is driven by the publish routing key, not by arbitrary message headers.

That means:

- use the configured publisher route by default, or override the routing key explicitly when a flow needs a different broker route
- if you omit the default routing key during registration, every publish call must provide a non-empty routing-key override
- use message headers for metadata and processing context
- use a `headers` exchange in topology only when broker-side header routing is explicitly desired

## Example Failure Flow

A common flow looks like this:

1. Message arrives on `orders.created`.
2. Handler throws `InvalidOperationException`.
3. Retry policy says it is retryable.
4. Runtime republishes to `orders.created.retry` / `orders.created.retry.step1`.
5. Broker TTL queue waits.
6. Broker dead-letters the message back to the main route.
7. Handler fails again.
8. Retry limit is exhausted.
9. Runtime forwards to `orders.created.dlx` / `orders.created.dlq`.
10. Original delivery is acked after the forward succeeds.

## Topology Validation

Startup validation is optional but recommended.

```csharp
services.AddSphereRabbitMq(options =>
{
    options.ValidateTopologyOnStartup = true;
});
```

When enabled, the runtime derives the expected subscriber topology automatically from:

- the registered subscribers
- each subscriber queue name
- each subscriber error-handling policy
- the built-in naming conventions for retry and dead-letter artifacts

This validation is read-only. It does not declare anything.

If an application needs to inspect the derived internal names, it can resolve `ISubscriberInfrastructureRouteResolver` from DI and query them explicitly. Those names are discoverable, but not configurable through the public subscriber API.

## Startup Initialization From YAML

If a team wants topology reconciliation at application startup instead of only through CI/CD, use the IaC runtime integration package and register topology initialization from a YAML file.

```csharp
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.DependencyInjection;

services.AddSphereRabbitMqWithTopologyInitialization(
    configureRabbitMq: options =>
    {
        options.SetConnectionString("amqp://user:pass@rabbitmq:5672/sales");
    },
    configureTopologyInitialization: options =>
    {
        options.YamlFilePath = "rabbitmq/topology.yaml";
        // options.ManagementUrl = "http://rabbitmq:15672/api/";
        // options.AllowMigrations = true;
    });
```

If runtime registration already exists elsewhere, the initializer can be added separately:

```csharp
services.AddSphereRabbitMq(options =>
{
    options.SetConnectionString("amqp://user:pass@rabbitmq:5672/sales");
});

services.AddSphereRabbitMqTopologyInitialization(options =>
{
    options.YamlFilePath = "rabbitmq/topology.yaml";
});
```

Behavior:

- the YAML file is loaded before subscriber startup
- a plan is built against the current broker state
- safe changes are applied automatically
- destructive or unsupported changes fail startup unless `AllowMigrations = true`
- subscriber startup happens only after topology initialization completes

## Internal Message Moving

The runtime contains an internal queue mover used by IaC-assisted migrations.

It is not the main public API for applications. Applications should think in terms of publish/consume/replay semantics, not arbitrary queue-to-queue movement.

## Tested Guarantees

Integration tests cover:

- publish and consume against a real RabbitMQ broker
- broker-based retry
- dead-letter forwarding
- bounded subscriber concurrency
- explicit failure when exchange or queue is missing
- explicit failure when retry topology is missing
- explicit failure when dead-letter topology is missing
- `NonRetriableMessageException`
- `DiscardMessageException`

## Validation Commands

- `dotnet test tests/SphereRabbitMQ.Tests.Unit/SphereRabbitMQ.Tests.Unit.csproj`
- `dotnet test tests/SphereRabbitMQ.Tests.Integration/SphereRabbitMQ.Tests.Integration.csproj`
