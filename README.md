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
- consuming from existing queues
- broker-based retry forwarding
- dead-letter forwarding
- topology validation at startup

Runtime documentation: [docs/runtime-library.md](/Users/jmr.pineda/Projects/GitHub/PinedaTec.eu/SphereRabbitMQ/docs/runtime-library.md)

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

1. Define topology in YAML.
2. Install or restore `SphereRabbitMQ.IaC.Tool`.
3. Apply it with `sprmq`.
4. Start application code with `SphereRabbitMQ`.
5. Publish and consume only against pre-existing broker resources.

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
