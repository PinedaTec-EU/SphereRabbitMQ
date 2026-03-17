# AGENTS

Este repositorio consume skills compartidas desde `../ai-skills-shared`.

## Fuente oficial

- Las reglas compartidas válidas viven en `../ai-skills-shared/AGENTS.md`.
- Las skills compartidas viven en `../ai-skills-shared/.shared-skills/skills/*`.
- No dupliques ni edites reglas de dominio en este repositorio salvo que la desviación sea local y explícita.

## Skills activas para este repositorio

- `../ai-skills-shared/.shared-skills/skills/terraform/SKILL.md`
- `../ai-skills-shared/.shared-skills/skills/terraform/k3s-environments.md`
- `../ai-skills-shared/.shared-skills/skills/terraform/k8s-modules.md`

## Reglas locales

- Antes de modificar cualquier clase que aparezca como `✅ Corregido` en `docs/tech.debts/codesmells.md` o `docs/tech.debts/architecture-violations.md`, lee el item correspondiente para conocer el invariante de diseño establecido y respétalo.
- Antes de marcar cualquier item como `✅` en los ficheros de deuda técnica, verifica el estado real del código — no marques por inferencia de commits pasados.

## Orden de prioridad

1. Instrucciones del sistema o de la sesión de la herramienta.
2. Instrucciones específicas del proveedor (`CLAUDE.md`, `COPILOT.md`, `CODEX.md`, `.codex/AGENTS.md`).
3. Este archivo `AGENTS.md`.
4. `../ai-skills-shared/AGENTS.md`.
5. Skills compartidas aplicables en `../ai-skills-shared/.shared-skills/skills/*`.
6. Prompt del usuario para la tarea actual.
