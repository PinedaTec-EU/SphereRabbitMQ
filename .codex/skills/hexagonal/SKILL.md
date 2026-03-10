---
name: hexagonal
description: Hexagonal Architecture (Port & Adapters) rules for TravelAgent. Apply when creating or modifying APIs, stores, providers, background services, or any component that crosses a layer boundary.
---

# Hexagonal Architecture (Port & Adapters)

---

## Modelo de capas

```
[ Primary Adapters ]         [ Domain / Application Core ]         [ Secondary Adapters ]
  Controllers                   Ports (interfaces)                    Mongo Stores
  Blazor Components             Domain Services                       GCR Providers
  BackgroundServices            Application Services                  HTTP Clients
  Hosted Services               Domain Events                         Email/Queue
```

- **Primary adapters** (izquierda): reciben input externo y lo traducen al dominio.
- **Core** (centro): define puertos (`I*Store`, `I*Provider`, `I*Bus`) e implementa lógica de negocio y aplicación. No conoce adaptadores.
- **Secondary adapters** (derecha): implementan los puertos del core para salir al mundo exterior (DB, HTTP, GCR, etc.).

---

## Puertos (Ports)

- Los puertos son **interfaces definidas en el core o en contratos del dominio**.
- Viven en `<Capability>/Interfaces/` dentro del proyecto core (`Licensing.Core`, etc.).
- Naming: `I<Role><Entity>` → `IFareStore`, `ICurrencyExchangeSettingsProvider`, `ILicenseAuditStore`.
- Un puerto no importa ningún tipo de infraestructura (`MongoDB.Driver`, `System.Net.Http`, etc.).

---

## Adaptadores secundarios (Secondary Adapters)

- Implementan puertos del core usando infraestructura concreta.
- Viven en proyectos de infraestructura **separados del core y de la API**:
  - `TravelAgent.Licensing.Mongo` → implementaciones Mongo del dominio licensing
  - `TravelAgent.Persistence.Mongo` → utilidades Mongo compartidas
- Naming: `Mongo<Entity>Store`, `Gcr<Capability>Provider`, `Http<Service>Client`.
- Regla estricta: **ningún adaptador secundario vive en proyectos de API** (`*.Api`). Las APIs son capa de aplicación/presentación, no de infraestructura.
- Si un proyecto `.Api` necesita un adaptador propio (por ejemplo, un store específico del admin), debe crearse un proyecto de infraestructura dedicado (`TravelAgent.Admin.Infrastructure`) o ubicarse en `Licensing.Mongo`.

---

## Adaptadores primarios (Primary Adapters)

- Controllers MVC: traducen HTTP → comando de aplicación → respuesta HTTP. Sin lógica de negocio.
- Blazor components/code-behind: traducen eventos UI → llamadas a Application Service. Sin lógica de dominio.
- BackgroundService/HostedService: traducen schedulers/timers → llamadas a Application/Domain Service.
- Regla: los adaptadores primarios **no acceden a stores, colecciones Mongo, ni infraestructura directamente**. Solo hablan con Application Services o Domain Services vía interfaces.

---

## Reglas de dependencia entre capas

| Desde | Puede depender de | No puede depender de |
|---|---|---|
| Core (dominio) | Contracts, TravelAgent.Core | Mongo, HTTP, GCR, cualquier infra |
| Infraestructura (Mongo, GCR) | Core, Contracts, Persistence.Mongo | Otras infras no relacionadas, APIs |
| API (controllers, application) | Core (interfaces), Contracts | `IMongoCollection` directamente, drivers concretos |
| Portal (Blazor) | Contracts, Portal.Shared | Core directamente (solo via HTTP client/API) |

---

## Reglas concretas de implementacion

- **Prohibido en capas `.Api`**: `IMongoDatabase`, `IMongoCollection<T>`, `BsonDocument`, `FilterDefinition`, `MongoDB.Driver.*` — salvo que exista un proyecto de infraestructura intermedio que exponga un puerto.
- **Prohibido en el core**: referencias a `MongoDB.*`, `System.Net.Http.HttpClient` naked, `IConfiguration` para leer settings de infraestructura.
- **IConfiguration en core**: permitida solo para bootstrapping/wiring en `ServiceCollectionExtensions`. No se inyecta en domain services ni en stores de dominio.
- **Settings externos** (GCR, SMTP, providers): siempre detrás de un puerto (`IXxxSettingsProvider`). El adaptador concreto (`GcrXxxSettingsProvider`) vive en infraestructura.
- **Dependency Injection**: el proyecto de entrada (`Program.cs` o `ServiceCollectionExtensions`) es el único que conoce la implementación concreta y la registra contra la interfaz.

---

## Comprobacion rapida antes de hacer commit

1. ¿El dominio core tiene alguna referencia nueva a infraestructura? → Mover a adaptador secundario.
2. ¿Un controller accede directamente a Mongo, GCR, o servicios externos? → Extraer a Application Service + adaptador.
3. ¿Una implementacion de `I*Store` o `I*Provider` vive dentro de un proyecto `.Api`? → Mover a proyecto de infraestructura.
4. ¿Un puerto nuevo está definido fuera del core o de los contratos? → Mover a `<Capability>/Interfaces/` en el core.
5. ¿Una clase de dominio importa un namespace de infraestructura? → Violacion de dependencia; refactorizar.
