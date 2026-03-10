---
name: dotnet
description: Work on the .NET solution. Use this when editing APIs, portals, scheduler/jobs, core libraries, shared contracts/persistence, JSON data, or tests.
---

# .NET

- .Net 10

## Optional skill extensions

The following skill files are applied when they exist alongside this skill:

- `mongodb.md` — collection naming, BSON document contracts
- `smtp.md` — outbound email queue patterns
- `blazor.md` — Blazor/Razor component rules
- `ai.md` — AI/Ollama integration rules
- `workers.md` — background worker audit invariants

## Main structure

If a `PROJECT.md` file exists alongside this skill, use it as the authoritative reference for project-specific structure, context names, and collection prefixes.

Otherwise, assume the standard layout:
- `src/` — production source projects
- `tests/` — test projects

### Web portals

- Register user ip, browser info, and resolve ip location using an external IP geolocation service

### Http APIs

- All must be secured with an API key (tenant scoped, private).
- Apply HTTP rate limiters and basic anti-DoS protections.

## Considerations

- Before implementing, inspect whether the capability already exists partially or fully in the same bounded context and prefer extending/reusing that path instead of creating a parallel one.
- During analysis, if duplicated functionality, duplicated responsibilities, or near-identical flows are detected, explicitly alert about it before or while implementing.
- If a method has a `CancellationToken`, it must be the last parameter in the method signature.
- SOLID principles must be followed.
- Reuse-first rule: avoid adding new types, helpers, mappers, validators, or UI blocks when an existing focused abstraction can be reused or extended with low coupling.
- Variables must be descriptive and clear, without being excessively long.
- Member ordering rule: declare class elements in this order:
  - constants (`const`, then `static readonly` metadata/config when kept local)
  - properties
  - fields/members
  - constructors
  - methods
- Visibility ordering rule: inside each element group, order by visibility `public`, `protected`, `internal`, `private`.
- The order of methods in classes must be: public, protected, internal, then private.
- Private-member pressure rule: when a class or component grows beyond 16 private members, extract cohesive internal state into a dedicated `<Owner>Descriptor` record in a separate file. Descriptors are data-only records: no methods, no behavior. Move behavior to focused services/helpers/components and make the owner delegate to them instead of continuing to grow.
- Magic numbers, strings, etc. must be avoided. Use `private const` when elements are exclusive to a class, or, if shared, a `XxxxConsts` class with `internal const` members and reduced visibility.
- Fixed-value collections rule: `private static readonly` collections that act as immutable metadata/configuration (for example search keys, labels, supported tokens) should be treated as constants and extracted to `<Owner>Consts.cs` with the narrowest possible visibility.
- Scope rule for constants:
  - if used in only one class/component: keep it as `private const` in that class/component.
  - if reused in 2+ files/projects: move it to shared contracts (`src/shared/.../Constants`) with clear domain naming.
  - UI labels/tooltips can remain local constants unless they are reused across modules.
- Before closing a change, run a quick pass for new magic values introduced in edited files (`strings`, `numbers`, `timeouts`, `status names`) and extract them.
- When possible, components should use a fluent style for construction/configuration.
- Methods must have clearly bounded responsibilities; actions should be focused and the code readable, split into small chunks (ideally no more than 50 lines of code).
- Async rule (required): never call async methods synchronously. Do not use `.GetAwaiter().GetResult()`, `.Result`, or `.Wait()` to block on a `Task`. Always propagate `async/await` up the call chain. Controllers, services, and stores that call async methods must be declared `async Task<...>`.
- Async naming rule (required): every method that returns `Task` or `Task<T>` must have the `Async` suffix (for example `GetTierAsync`, `UpdateDraftAsync`). Methods without the suffix must be synchronous. No exceptions.
- SRP enforcement rule: prefer small focused classes over multipurpose classes. Avoid "god" services/resolvers/controllers with key-based switch branches spanning unrelated capabilities.
- Coupling control rule: depend on abstractions at module boundaries and avoid introducing new direct knowledge of infrastructure, transport, or unrelated domain details into consumers that do not own them.
- Search extension rule: for dynamic search key resolution, prefer multiple focused `IKeyResolver` implementations (registered together) instead of one monolithic resolver. Add behavior by adding new resolver classes and DI registrations, not by growing a central resolver.
- Maintainability reduction rule: when the same composition/configuration logic appears in 2+ modules (for example repeated DI registrations), extract a shared extension/factory in the owning domain package and reuse it from consumers. Prefer one canonical registration point over duplicated wiring.
- Open telemetry for every component, apis, workers, services
- Time source invariant (required): in runtime code (APIs, services, workers, stores), do not use `DateTime.UtcNow` or `DateTimeOffset.UtcNow` directly. Inject and use `ISuiteTimeProvider` (`UtcNow`, `UtcDate`) so behavior is deterministic/testable. The canonical clock abstraction/implementation must live in the shared core project and must not be hosted in domain cores. Keep direct wall-clock calls only in tests or strictly isolated compatibility layers.
- External runtime configuration (SMTP, endpoints, storage, provider options) must come from the external runtime configuration store (defined in PROJECT.md). `appsettings` is only allowed for bootstrap/entry-point configuration, not as fallback for other external systems.
- Dependency injection rule: never instantiate runtime collaborators inside controllers/services/stores (for example, providers, gateways, stores, clients, resolvers, clocks). Require abstractions in constructor parameters and resolve them from IoC; tests must pass fakes/mocks explicitly.
- Constructor dependency rule: constructor parameters must use interfaces/contracts (`I*`) instead of final concrete implementations whenever the dependency is an internal collaborator/service/provider/client/store that can be abstracted. Only depend on a concrete type when the concrete type itself is the intended boundary and no meaningful contract exists.
- Program composition rule: registrations added to `Program.cs` must be extracted to `ServiceCollectionExtensions.cs` and grouped by functionality (for example: presentation, infrastructure, domain/application). Keep `Program.cs` as minimal composition/root wiring.
- Core folder/naming convention for services/stores:
  - Use vertical folders by capability (`Store`, `Versioning`, `Time`, `Audit`, `Seeding`, `Evaluator`, etc.).
  - Interfaces must live in `<Capability>/Interfaces` and use `I*` naming (e.g., `Store/Interfaces/I<Domain>Store.cs`).
  - Implementations must live in `<Capability>/` (not inside `Interfaces/`) and use explicit role suffixes:
    - stores: `*Store.cs` (or `*StoreService.cs` when it is an orchestration/service wrapper around store behavior)
    - services: `*Service.cs`
    - providers: `*Provider.cs`
  - Keep one clear interface-to-implementation mapping and avoid mixing unrelated capabilities in the same folder.
- Global usings convention: when the same `using` appears repeatedly in a project (default threshold: 5+ files), move it to that project's `globals.cs` as a `global using`. Keep file-local `using` when scope is intentionally narrow or improves readability.
- Architectural decision policy: prefer centralizing business/context decisions in a single orchestrator/service. Do not spread decision flags across callers when callers only forward values and do not own the decision context.
- Cross-domain store/event invariant: for every domain with lifecycle/state transitions, define one store boundary (`I<Domain>Store`) as the single source of truth for persistence and integration-event emission. Controllers, seeders, workers, and portals must never emit domain events directly.
- Portal-level global rules (`admin`, `tenant`, `guest`) should be managed in Customization UI with override-only persistence and reset-to-default controls, instead of hardcoding per-consumer flags.
- Multi-portal API boundary invariant: when a domain serves multiple portals, keep role-separated APIs by project/namespace. Admin APIs own management/mutations; tenant/guest APIs must be read-only or narrowly scoped operations and must not expose admin mutation endpoints.
- Reuse-first UI/component rule: when a UI block/component is expected to be used in 2 or more places, default to extracting it as an independent reusable component (preferably in the shared portal project) instead of duplicating markup/logic.
- Contract shape rule: do not introduce nested helper DTOs inside a request/response only to visually reduce top-level member count (for example `Configuration`, `Payload`, `Data`). If several requests/responses share the same fields, extract an explicit shared base contract in `src/shared/.../Contracts` and keep the actual HTTP contract flat.
- While the app is incomplete, don't make legacy code, don't worry about refactoring, just add new code in the best way possible, and when the app is complete, we can start refactoring and improving the codebase as needed.
- Breaking-change rule (required during development): do not add backward-compatibility layers, legacy parsers, dual-format persistence, tolerant readers, or transition code for old data/contracts unless the user explicitly asks for compatibility or a migration window. Default to one canonical format and update seeds/tests/contracts to match it.

## Tests

- Test code is exempt from SRP, class-size limits, and God-Class rules. A large test class (even 1000+ lines) is acceptable as long as its methods are readable, focused, and maintainable individually. Do not split or refactor test classes solely to reduce line count.
- Test fakes and doubles may implement multiple interfaces and hold state for multiple aggregates when that simplifies test setup. Do not force one-interface-per-fake unless it provides a concrete readability or isolation benefit.
- Fake naming rule: test fakes and in-memory doubles must include `Fake` in their name so the origin is immediately visible from the type or variable name alone (for example `InMemoryFakeCampaignStore`, `FakeTenantStore`, `FakeAuditWriter`). Do not name fakes as if they were real implementations (for example avoid `InMemoryCampaignStore` — prefer `InMemoryFakeCampaignStore`).
- Reusable, associated fixtures must be used, with methods that create common elements for multiple tests, reducing complexity and improving readability.
- Tests must be unit tests, using mocks or WireMock (for endpoints).
- Tests must exercise the public contract through the interface or higher-level boundary, not by coupling test intent to the concrete implementation type, unless the test is explicitly about implementation-specific behavior.
- When a service/store/use case exposes an interface, declare the SUT through that interface in tests (for example `IWorkflowExecutor executor = new WorkflowExecutor(...)`) and validate behavior from the contract perspective.
- Use mocks (moq)
- Use Wiremock to mock http apis
- xUnit
- Tests for a component are implemented in the same branch and issue as the component changes.
- Any added or modified component must include new/adapted tests in the same change set.
- For controllers, tests must cover validations, error paths, and edge cases when applicable, not only happy path responses.

## Workflow

- Prefer fewer, consolidated branches when tasks overlap or will be merged together soon.
- Only create a dedicated branch per issue when the work is isolated and will not be merged with nearby tasks.
- Branch naming (when needed): `feature/issue-<id>-<short-title>`, `bugfix/issue-<id>-<short-title>`, or `chore/issue-<id>-<short-title>` (kebab-case).
- Implement changes with focused commits.
- Commit message convention (required): `<emoji> <category>: <short description>` in lowercase.
- Allowed categories: `feature`, `bugfix`, `hotfix`, `chore`, `refactor`, `docs`, `test`, `perf`, `build`, `ci`, `revert`.
- Category examples: `✨ feature: add tenant fare preview`, `🪲 bugfix: prevent null subscription crash`, `🚑 hotfix: patch production token validation`, `🔖 chore: bump release version  to x.y.z`.
- Commit rule for versioning (required): whenever `version.nfo`, `version_definition.json`, or project/package version fields are changed, always include a dedicated version commit.
- If there are multiple commits, keep the dedicated version commit as the final commit.
- When the user asks to "make commits", group them by functionality (backend, portal, docs/tests) and still keep the version commit as the final one.
- Push the branch and open a PR.
- PRs are reviewed by the owner before merging into main.
- After any merge to `main`, fetch and merge/rebase `origin/main` into your working branch before continuing.
- Repo maintenance tasks may be applied directly to `main` when explicitly agreed.

## Build
- Increment version numbers in `version_definition.json` as part of the every build process, property "currentVersion" incremented with +0.0.0.1, 

## Commits and versioning
- Commit message convention (required): `<emoji> <category>: <short description>` in lowercase
- A dedicated final commit is required for version changes (for example: `🔖 chore: bump version to w.x.y.z`).
- That final version commit must include **all** version-only files changed in the branch:
  - `version_definition.json`
  - `version.nfo` (when present)
  - every affected `*.csproj` where `ReleaseVersion`/`Version`/`AssemblyVersion`/`FileVersion` changed
- If a `*.csproj` has mixed changes (functional + version), split by hunk:
  - functional hunks go in the corresponding functional commit
  - only version-property hunks (`ReleaseVersion`/`Version`/`AssemblyVersion`/`FileVersion`) go in the final bump commit
- Do not include version-property hunks in functional commits.
