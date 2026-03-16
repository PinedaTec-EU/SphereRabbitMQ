# Agent Instructions — SphereRabbitMQ

## Reglas compartidas (fuente única de verdad)

Las reglas de dominio, calidad, arquitectura y workflow viven en el repo `ai-skills-shared`
del mismo workspace. Lee y aplica sus instrucciones antes de actuar en este repo:

- `ai-skills-shared/AGENTS.md` — índice y máximas transversales
- `ai-skills-shared/.shared-skills/skills/dotnet/SKILL.md` — reglas .NET, tests, workflow, build
- `ai-skills-shared/.shared-skills/skills/dotnet/workers.md` — background workers
- `ai-skills-shared/.shared-skills/skills/hexagonal/SKILL.md` — arquitectura hexagonal

No dupliques esas reglas aquí. Cualquier cambio de reglas se hace en `ai-skills-shared`.

## Reglas específicas de este repo

- Solución: `SphereRabbitMQ.slnx`
- Framework: .NET 10
- Versión centralizada en `version_definition.json` (incrementar `currentVersion` +0.0.0.1 en cada build)
