# SphereRabbitMQ

`SphereRabbitMQ` is a .NET 10 RabbitMQ toolkit split into two clearly separated parts:

- `SphereRabbitMQ.IaC`: infrastructure-as-code for RabbitMQ topology
- `SphereRabbitMQ`: runtime publisher/subscriber library for applications

The separation is strict by design:

- the IaC tool owns topology
- the runtime library never creates or mutates topology

This repository is intentionally opinionated around externally managed RabbitMQ infrastructure, broker-based retries, and explicit operational safety.

## Components

### `SphereRabbitMQ.IaC`

Infrastructure toolchain and CLI for:

- virtual hosts
- exchanges
- queues
- bindings
- dead-letter topology
- broker-based retry topology based on TTL + dead-letter reinjection
- reconciliation, export, and controlled migration

CLI documentation: [docs/cli.md](/Users/jmr.pineda/Projects/GitHub/PinedaTec.eu/SphereRabbitMQ/docs/cli.md)

Distribution:

- runtime library: NuGet package `SphereRabbitMQ`
- CLI: NuGet `dotnet tool` package `SphereRabbitMQ.IaC.Tool` providing the `sprmq` command

### `SphereRabbitMQ`

Runtime library for:

- publishing messages to existing exchanges
- publishing with either a configured default routing key or a per-call routing-key override
- consuming from existing queues
- broker-based retry forwarding
- dead-letter forwarding
- topology validation at startup

Runtime documentation: [docs/runtime-library.md](/Users/jmr.pineda/Projects/GitHub/PinedaTec.eu/SphereRabbitMQ/docs/runtime-library.md)

## YAML Dead-Letter To Another Queue

For delayed delivery patterns, the YAML topology can declare a queue whose expired messages are dead-lettered directly into another queue.

This is expressed explicitly as dead-lettering, not as a generic queue-to-queue binding.

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

Behavior:

- `orders.delay.5m` keeps the message for `00:05:00`
- once the TTL expires, RabbitMQ dead-letters the message through the default exchange
- the message is routed into `orders.consume`

Notes:

- `destinationType: queue` requires `queueName`
- this does not generate a dedicated DLX/DLQ pair
- RabbitMQ still implements it internally through dead-lettering to the default exchange, not through a native queue-to-queue binding primitive

## Startup Topology Initialization From YAML

Applications that do not want to apply topology only through CI/CD can also load and apply the topology YAML during host startup.

This capability is provided by the IaC runtime integration package and runs before subscriber startup.

Example:

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

You can also keep the existing runtime registration and add the startup initializer separately:

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

Operational rules:

- the YAML file is parsed, normalized, validated, and planned before apply
- if the plan contains destructive or unsupported changes, startup fails unless `AllowMigrations = true`
- topology initialization runs before runtime topology validation and before subscribers begin consuming
- this is intended for teams that want self-contained application startup instead of a separate topology deployment pipeline

## Core Rule

The runtime library must never declare or modify RabbitMQ topology.

That means it will not create:

- virtual hosts
- exchanges
- queues
- bindings

If any expected topology is missing, the runtime fails explicitly with a diagnostic error.

## Repository Layout

- `src/SphereRabbitMQ.Abstractions`
- `src/SphereRabbitMQ.Domain`
- `src/SphereRabbitMQ.Application`
- `src/SphereRabbitMQ.Infrastructure.RabbitMQ`
- `src/SphereRabbitMQ.DependencyInjection`
- `src/SphereRabbitMQ.IaC.Domain`
- `src/SphereRabbitMQ.IaC.Application`
- `src/SphereRabbitMQ.IaC.Infrastructure.RabbitMQ`
- `src/SphereRabbitMQ.IaC.Infrastructure.Yaml`
- `src/SphereRabbitMQ.IaC.Cli`
- `tests/SphereRabbitMQ.Tests.Unit`
- `tests/SphereRabbitMQ.Tests.Integration`
- `tests/SphereRabbitMQ.IaC.Tests.Unit`
- `tests/SphereRabbitMQ.IaC.Tests.Integration`

## Typical Flow

1. Install or restore `SphereRabbitMQ.IaC.Tool`.
2. Scaffold a starting YAML with `sprmq init` or reuse `samples/templates/`.
3. Define or adapt the topology in YAML.
4. Apply it with `sprmq`.
5. Optionally bootstrap from a live broker with `sprmq export --include-broker`.
6. Enable shell completions with `sprmq completion <bash|zsh|pwsh>`.
7. Start application code with `SphereRabbitMQ`.
8. Publish and consume only against pre-existing broker resources.

## Runtime Configuration Sources

`AddSphereRabbitMq()` can resolve runtime connection settings from environment variables even when the configuration delegate is omitted.

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

## Runtime Error Model

Subscriber handlers may fail in three distinct ways:

- retryable failure: the message is forwarded to retry topology
- non-retryable failure: the message skips retries and goes to DLQ if configured
- discard failure: the message is acked and dropped intentionally

Built-in exception semantics:

- `NonRetriableMessageException`: never retried
- `DiscardMessageException`: never retried and never dead-lettered

Additional non-retryable exception types can be configured per subscriber through settings.

## Migration Safety

The IaC CLI supports `apply --migrate` for broker resources that RabbitMQ cannot redeclare in place.

Migration rules are explicit and tested:

- incompatible exchanges are recreated and bindings are restored from YAML
- generated queues are recreated directly
- mainstream queues are migrated through a temporary queue
- concurrent migrations are serialized with the lock queue `sprmq.migration.lock`

## Validation

Runtime:

- `dotnet test tests/SphereRabbitMQ.Tests.Unit/SphereRabbitMQ.Tests.Unit.csproj`
- `dotnet test tests/SphereRabbitMQ.Tests.Integration/SphereRabbitMQ.Tests.Integration.csproj`

IaC:

- `dotnet test tests/SphereRabbitMQ.IaC.Tests.Unit/SphereRabbitMQ.IaC.Tests.Unit.csproj`
- `dotnet test tests/SphereRabbitMQ.IaC.Tests.Integration/SphereRabbitMQ.IaC.Tests.Integration.csproj`
