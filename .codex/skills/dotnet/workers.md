---
name: dotnet-workers
description: Background worker rules for .NET projects. Applied when BackgroundService/IHostedService workers are used.
---

# Background Workers

- Worker audit invariant (required): every runtime worker (`BackgroundService`/`IHostedService`) must append execution audits to the project's dedicated worker audit collection (see PROJECT.md) with, at minimum, `workerName`, `schedulerName`, `version`, `startedAtUtc`, `completedAtUtc`, `durationMs`, `succeeded`, and `message`. Do not add workers that run without audit persistence.
