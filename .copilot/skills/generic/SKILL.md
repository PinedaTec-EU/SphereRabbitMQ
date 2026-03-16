```skill
---
name: travelagent
description: Puente de compatibilidad. Redirige a las skills compartidas en ai-skills-shared.
---

# TravelAgent (bridge)

Esta skill existe para compatibilidad con rutas antiguas de Copilot.

## Fuente única de verdad (shared repo)

Las skills de dominio y calidad viven en el repo `ai-skills-shared`, que forma parte de este workspace:

1. `ai-skills-shared/.shared-skills/skills/dotnet/SKILL.md` — reglas .NET, tests, workflow, build
2. `ai-skills-shared/.shared-skills/skills/dotnet/workers.md` — extensiones para background workers
3. `ai-skills-shared/.shared-skills/skills/hexagonal/SKILL.md` — arquitectura hexagonal (Port & Adapters)

No edites esos ficheros desde este repo. Cualquier cambio de reglas debe hacerse en `ai-skills-shared` directamente.

## Máximas activas (definidas en shared)

- Identifier rule: ULID-only, nunca GUID (`Guid`, `Guid.NewGuid()`). Usar `Ulid.NewUlid()` en runtime y tests.

No dupliques aquí reglas de dominio o calidad.
```
