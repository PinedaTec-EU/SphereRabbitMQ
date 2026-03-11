# SphereRabbitMQ

`SphereRabbitMQ.IaC` is a .NET 10 infrastructure-as-code toolchain for RabbitMQ topology management. It is intentionally scoped to broker infrastructure only: virtual hosts, exchanges, queues, bindings, dead-letter topology, and broker-based retry topology.

The repository now also includes the runtime library `SphereRabbitMQ`, which publishes and consumes on top of pre-provisioned RabbitMQ topology without declaring infrastructure at runtime. Runtime usage is documented in [docs/runtime-library.md](/Users/jmr.pineda/Projects/GitHub/PinedaTec.eu/SphereRabbitMQ/docs/runtime-library.md).

CLI usage, conventions, and topology generation rules are documented in [docs/cli.md](/Users/jmr.pineda/Projects/GitHub/PinedaTec.eu/SphereRabbitMQ/docs/cli.md).

## Phase 1 status

Phase 1 is implemented in this repository:

- Solution structure for domain, application, infrastructure, CLI, unit tests, and integration tests
- Pure domain models for normalized topology, naming policy, validation issues, and planning primitives
- Source-neutral application contracts plus normalization, validation, and variable resolution services
- YAML contract models and a mapper that keeps YAML DTOs out of the application layer

Later phases will add parsing, planning, RabbitMQ broker adapters, CLI commands, export support, and end-to-end integration behavior.

## Solution layout

- `src/SphereRabbitMQ.IaC.Domain`
- `src/SphereRabbitMQ.IaC.Application`
- `src/SphereRabbitMQ.IaC.Infrastructure.Yaml`
- `src/SphereRabbitMQ.IaC.Infrastructure.RabbitMQ`
- `src/SphereRabbitMQ.IaC.Cli`
- `tests/SphereRabbitMQ.IaC.Tests.Unit`
- `tests/SphereRabbitMQ.IaC.Tests.Integration`
