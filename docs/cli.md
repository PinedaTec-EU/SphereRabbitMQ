# SphereRabbitMQ CLI

`sprmq` is the command-line interface for `SphereRabbitMQ.IaC`.

Its scope is intentionally limited to RabbitMQ infrastructure:

- virtual hosts
- exchanges
- queues
- bindings
- dead-letter topology
- retry topology based on TTL and dead-letter reinjection
- export and reconciliation of broker topology

It does not provide runtime publisher/subscriber abstractions.

## Build And Run

Publish the standalone CLI through the VS Code task `publish sprmq cli`.

The published binary is generated in `./cli`:

```bash
./cli/sprmq --help
```

During development, you can still use:

```bash
dotnet run --project src/SphereRabbitMQ.IaC.Cli -- --help
```

## Commands

### `validate`

Validates YAML syntax, normalization, and semantic consistency.

```bash
./cli/sprmq validate --file samples/minimal-topology.yaml
```

### `plan`

Reads desired topology, reads current broker state, and prints the reconciliation plan.

`plan` never changes broker state.

```bash
./cli/sprmq plan --file samples/minimal-topology.yaml
```

### `apply`

Builds the execution plan and applies only safe operations.

If the plan contains destructive or unsupported changes, `apply` stops and reports the blocking operations unless `--migrate` is provided.

```bash
./cli/sprmq apply --file samples/minimal-topology.yaml
./cli/sprmq apply --file samples/minimal-topology.yaml --dry-run
./cli/sprmq apply --file samples/minimal-topology.yaml --verbose
./cli/sprmq apply --file samples/minimal-topology.yaml --migrate
```

### `apply --migrate`

`--migrate` enables broker-side reconciliation for resources that RabbitMQ cannot redeclare in place when immutable arguments differ.

Operational rules:

- a per-virtual-host lock queue named `sprmq.migration.lock` is used to serialize migrations across concurrent CLI instances
- incompatible exchanges are deleted and recreated, then bindings are restored from the YAML definition
- generated debug queues are deleted and recreated without preserving messages
- generated retry/dead-letter/parking queues are deleted and recreated without preserving messages
- mainstream queues use a temporary queue:
  - create a temporary queue and bind it with the same desired bindings
  - remove bindings from the old queue
  - move buffered messages from the old queue into the temporary queue
  - delete the old queue and create the new one
  - move messages from the temporary queue into the new queue before restoring bindings
  - restore bindings from the YAML definition
  - delete the temporary queue

If `--migrate` is not specified, the CLI keeps the current safe behavior and fails when an incompatible queue or exchange already exists on the broker.

### `destroy`

Builds and optionally executes a destroy plan for the topology described in the YAML file.

Real deletion requires `--allow-destructive`.

```bash
./cli/sprmq destroy --file samples/minimal-topology.yaml --dry-run
./cli/sprmq destroy --file samples/minimal-topology.yaml --allow-destructive
./cli/sprmq destroy --file samples/minimal-topology.yaml --allow-destructive --destroy-vhost
```

### `export`

Reads current broker topology and exports it as YAML.

```bash
./cli/sprmq export --file samples/minimal-topology.yaml
./cli/sprmq export --output-file exported-topology.yaml
```

## Broker Configuration Resolution

Broker connection values are resolved in this order:

1. command-line arguments
2. environment variables
3. YAML `broker` section
4. defaults or derived values

Supported environment variables:

- `SPHERE_RABBITMQ_MANAGEMENT_URL`
- `SPHERE_RABBITMQ_USERNAME`
- `SPHERE_RABBITMQ_PASSWORD`
- `SPHERE_RABBITMQ_VHOSTS`

The CLI prints the origin of each resolved value in text mode:

```text
Broker settings:
- managementUrl: http://localhost:31672/api/ (yaml)
- username: admin (environment)
- password: (command-line)
- virtualHosts: sales (yaml)
```

## Output Modes

### Text output

Default operator-friendly output.

Includes:

- tool banner
- connection target
- validation result
- plan or execution plan
- blocking changes when execution is not safe

### JSON output

Pipeline-friendly output:

```bash
./cli/sprmq plan --file samples/minimal-topology.yaml --output json
```

JSON output includes `blockingChanges` when the plan is not safely executable.

## Topology Conventions

The YAML format allows explicit values, but several defaults are intentionally conventional.

### Exchange defaults

- `type: topic` when omitted
- `durable: true` by default

### Queue defaults

- `type: classic` when omitted
- `durable: true` by default
- `ttl` is optional and maps to `x-message-ttl`

Example:

```yaml
queues:
  - name: orders.created
    ttl: "00:10:00"
```

### Naming convention policy

The `naming` block controls generated retry and dead-letter artifact names:

```yaml
naming:
  separator: "."
  retryExchangeSuffix: "retry"
  retryQueueSuffix: "retry"
  deadLetterExchangeSuffix: "dlx"
  deadLetterQueueSuffix: "dlq"
  parkingLotQueueSuffix: "parking"
```

When naming values are omitted, internal defaults are used.

### Debug queue generation

The optional `debugQueues` block generates one debug queue per exchange in each managed virtual host.

```yaml
debugQueues:
  enabled: true
  queueSuffix: debug
```

Debug queue conventions are fixed:

- queue name: `<exchange>.<queueSuffix>`
- queue type: `classic`
- queue durable: `true`
- binding routing key: `#`

This keeps debug topology deterministic across environments.

## Resilience Topology

Retry and dead-letter topology are broker-based and deterministic.

The tool can derive retry and dead-letter artifacts from high-level queue settings.

Example:

```yaml
queues:
  - name: orders.created
    type: quorum
    retry:
      enabled: true
      steps:
        - name: fast
          delay: "00:00:30"
    deadLetter:
      enabled: true
```

The resulting topology is conceptually:

```mermaid
flowchart LR
    EX[orders exchange]
    Q[orders.created]
    RX[orders.created.retry exchange]
    RQ[orders.created.retry.fast]
    DLX[orders.created.dlx]
    DLQ[orders.created.dlq]

    EX -- orders.created --> Q
    Q -- dead-letter --> RX
    RX -- orders.created.retry.fast --> RQ
    RQ -- TTL elapsed --> Q
    Q -- rejected or expired --> DLX
    DLX -- orders.created --> DLQ
```

Operationally:

- the business queue dead-letters into the retry exchange
- each retry queue uses TTL
- when TTL expires, the message is dead-lettered back to the main flow
- dead-letter artifacts remain separate and visible in the plan

## Samples

Repository samples:

- `samples/minimal-topology.yaml`
- `samples/queue-ttl-and-debug-topology.yaml`

The second sample demonstrates:

- implicit exchange defaults
- queue TTL
- generated debug queues

## Safe Execution Model

The CLI is designed for CI/CD execution:

- deterministic output
- non-interactive behavior
- explicit dry-run support
- explicit destructive opt-in for `destroy`
- machine-readable JSON output
- no hidden destructive reconciliation during `apply`

If the tool detects a destructive or unsupported change, it prints the blocking operations and exits with a non-success code.
