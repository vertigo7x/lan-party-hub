---
title: 'Story 1.1 — Estructura de solución y contratos compartidos'
type: 'feature'
created: '2026-10-27'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: 'db2aa1bd72ed1a7ab80da127289d9fb732ca7a45'
story_key: '1-1-estructura-de-solución-y-contratos-compartidos'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/epics/epics-lan-party-hub-2026-10-26/epics.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-lan-party-hub-2026-10-26/ARCHITECTURE-SPINE.md'
  - '{project-root}/AGENTS.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** El repositorio es greenfield: no hay solución, ni contratos compartidos, ni estructura de pruebas. Doce épicas posteriores dependen de esas decisiones; fijarlas ahora es más barato que refactorizarlas después.

**Approach:** Crear la solución .NET 10 (`global.json`, 3 proyectos `src`, 3 proyectos de test) y poblar `Shared` con el inventario de contratos de AD-18, dejando el grafo de dependencias hacia dentro y declarada la estrategia de pruebas por niveles.

## Boundaries & Constraints

**Always:**

- `Shared` solo contiene contratos: DTOs, identificadores, enumeraciones, eventos de SignalR y versión de protocolo. Sin EF Core, Avalonia, red ni lógica de negocio.
- `Shared` no referencia otro proyecto; `Server` y `Client` sí referencian `Shared`.
- Dependencias hacia adentro: `Infrastructure → Application → Domain`. Ningún tipo de `Domain` importa infraestructura, EF Core, Avalonia ni HTTP.
- El proyecto del MVP no referencia `MonoTorrent` ni ninguna biblioteca P2P.
- Target `net10.0`, SDK pineado en `global.json` (`10.0.403`).
- `DownloadState` e `InstallationState`: enumeraciones cerradas, con transiciones explícitas y estado terminal por operación, idénticas en cliente y servidor.
- La microcopia de estado sale solo de `StateDisplayMap`; los errores, solo de `ErrorCatalog`.
- Identificadores opacos UUID v7, fechas UTC ISO-8601, `sizeBytes` en bytes exactos.
- El inventario de `Shared` es la enumeración exhaustiva de AD-18 **ampliada** con `ServerEndpoint` (AD-3) y `ConnectionState` (AD-27): 20 contratos. El AC5 de `epics.md` se corrige para alinearse con la espina, con entrada en su `.memlog.md`.
- Rama por historia y PR con revisión antes de merge; nunca push directo a `main`.

**Never:**

- Duplicar en `Server` o `Client` cualquier contrato de `Shared`.
- Meter lógica de negocio o dependencias de infraestructura en `Shared`.
- Introducir autenticación o autorización de cliente.
- Introducir vocabulario de peer, swarm, tracker o seeding.
- Inventar microcopia o códigos de error de casos que ninguna historia posee todavía.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Transición válida | `(Queued, Downloading)` | aceptada | — |
| Transición inválida | `(Queued, Completed)` | rechazada | el estado no cambia; error de dominio |
| Estado terminal | `(Completed, Downloading)` | rechazada | `Completed` es terminal de descarga |
| Resultado no fallido | `OperationOutcome.PackageWithdrawn` | distinto de `Failed` | no es `ApiError` ni `ClientError` |
| Código desconocido | `code` fuera del catálogo | texto genérico | el código nunca es el texto principal |

</frozen-after-approval>

## Code Map

- `ARCHITECTURE-SPINE.md` -- AD-18 (tabla de contratos y formas exactas), AD-10 (máquinas de estado), AD-16 (tres niveles), AD-28 (sin P2P), AD-34 (`GameDescriptor`), «Consistency Conventions»
- `epics.md` -- Story 1.1 (líneas 882-964): los 7 AC y los «Pruebas necesarias»; las historias 4.1/4.3 exigen `ServerEndpoint` aquí
- `EXPERIENCE.md` -- microcopia de estados ya fijada; `docs/plan.md` -- §8 severidades, §10 checklist y DoD, §11 artefactos
- `AGENTS.md` -- política de ramas, SDK pineado, guards. No existe código: `src/` y `tests/` son nuevos

## Tasks & Acceptance

**Execution:**

- [x] `global.json` -- SDK `10.0.403` con `rollForward: latestFeature` -- sin él la compilación no es reproducible
- [x] `LANPartyHub.sln` -- solución con los 6 proyectos -- grafo de dependencias verificable
- [x] `src/Shared/LANPartyHub.Shared.csproj` -- `net10.0` sin `PackageReference` -- AC1/AC4
- [x] `src/Shared/Contracts/` -- contratos de AD-18 con sus formas exactas, incluidos `OperationOutcome`, `GameDescriptor`, `ServerEndpoint` y `ConnectionState`; enumeraciones **cerradas** con nomenclatura inglesa para estados y resultados (`DownloadState`, `InstallationState`, `OperationOutcome`, `ConnectionState`, `CatalogViewState`); `operatingSystem`, `distributionType`, `installationType` y `activeOperations[].operationType` son **cadenas**, tal como fija el ERD de la espina (líneas 707-709), y su vocabulario cerrado lo restringe el módulo de dominio que los posee -- AC5/AC6
- [x] `src/Shared/Contracts/StateDisplayMap.cs` y `ErrorCatalog.cs` -- tabla exhaustiva de microcopia (textos de `EXPERIENCE.md`) y catálogo único con entrada genérica y `cause`/`nextStep` obligatorios -- AD-10/AD-15
- [x] `src/Server/LANPartyHub.Server.csproj` + `src/Server/{Domain,Application,Infrastructure}/` -- `Microsoft.NET.Sdk.Web`, referencia a `Shared`, capas como carpetas y tipo marcador por capa -- AC1/AC2
- [x] `src/Client/LANPartyHub.Client.csproj` + `src/Client/{Domain,Application,Infrastructure,Presentation}/` -- Avalonia `12.1.4` (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`), referencia a `Shared`, capas como carpetas -- AC1/AC2
- [x] `src/Client/Domain/StateMachines/` -- `DownloadStateMachine` e `InstallationStateMachine` con transiciones explícitas, estado terminal y rechazo de toda transición inválida; se declaran **una sola vez**, en el ejecutor -- AC6
- [x] `tests/UnitTests/` -- tests de las máquinas de estado, del inventario de contratos (los exigidos existen en `Shared` y ninguno en `Server`/`Client`) y de la ausencia de referencias P2P en los `.csproj` -- AC3/AC6
- [x] `tests/IntegrationTests/` -- contenido declarado y un test real de SQLite en archivo -- AC7
- [x] `tests/EndToEndTests/` -- escenario de aceptación declarado (SM-1: dos o más clientes en LAN real) con su precondición explícita -- AC7
- [x] `tests/*/` -- runner común: `xunit.v3` `3.2.2` + `xunit.runner.visualstudio` `3.1.5` + `Microsoft.NET.Test.Sdk` `18.11.0`; solo `IntegrationTests` añade `Microsoft.Data.Sqlite` `10.0.13` -- AD-16, stack fijada en la espina
- [x] `docs/estrategia-pruebas.md` -- clasificar los 13 casos de §10 por nivel mínimo de detección, añadir el caso derivado de AD-27 y declarar que «cliente compartiendo fragmentos con otros» es caso de la PoC P2P y sale del conjunto obligatorio del MVP -- AC7/AR-21/AD-16/AD-28
- [x] `_bmad-output/planning-artifacts/epics/epics-lan-party-hub-2026-10-26/epics.md` -- corregir el AC5 de la Story 1.1 para que su lista de tipos de `Shared` coincida con AD-18/AD-33/AD-34 más `ServerEndpoint` y `ConnectionState`; registrar el cambio con `uv run _bmad/scripts/memlog.py append --path <ese .memlog.md> --type decision --text "..."` y **no** editar el `.memlog.md` a mano -- el AC5 actual es una transcripción incompleta de AD-18 y las historias 2.1, 3.1, 4.1, 4.3 y 9.2 dependen de esos contratos
- [x] `README.md` -- comandos de compilación y prueba -- verificación reproducible

**Acceptance Criteria:**

- Given la solución creada, when se inspecciona el grafo, then `Shared` no referencia otro proyecto, `Server`/`Client` referencian `Shared`, las dependencias apuntan hacia dentro y ningún `.csproj` referencia MonoTorrent ni biblioteca P2P
- Given `Shared`, when se inventarían sus tipos, then solo hay contratos, sin lógica de negocio ni dependencia de EF Core, Avalonia o red, y los tipos exigidos por AD-18/AD-33/AD-34 y por los consumidores del backlog existen sin duplicación en `Server` ni `Client`
- Given `DownloadState` e `InstallationState`, when se declaran, then son enumeraciones cerradas con transiciones explícitas y estado terminal por operación, y toda transición inválida se rechaza sin cambiar el estado
- Given los tres proyectos de prueba, when se clasifica la estrategia, then cada nivel tiene contenido declarado, la checklist de §10 queda clasificada por nivel mínimo de detección y el caso de compartición de fragmentos queda fuera del conjunto obligatorio del MVP

## Implementation Notes

- **Decisión humana (2026-10-27): opción A.** El inventario de `Shared` se amplía a los 20 contratos que exigen AD-18/AD-33/AD-34 y los consumidores del backlog, y el AC5 de `epics.md` se corrige para alinearse con la espina, con entrada en el `.memlog.md` del run de épicas.
- **Hallazgo S1 del verificador (2026-10-27), resuelto en el spec, no en el código.** La tarea de `src/Shared/Contracts/` y la nota de diseño pedían enumeraciones cerradas para `InstallationType`, `DistributionType` y `OperationType`. El ERD de `ARCHITECTURE-SPINE.md` (líneas 707-709) declara `string operatingSystem`, `string distributionType` y `string installationType`, y AD-34 exige que la tabla `GAME` y el DTO no diverjan: **el código es correcto y la especificación estaba mal**. Se corrigen la tarea y la nota de diseño. `OperationType` sigue siendo cadena porque AD-18 no define ese contrato y `Shared` debe quedarse en los 20 tipos que fijan AD-18, AD-3 y AD-27.
- **El subagente de implementación condensó la tarea de `src/Shared/Contracts/`**, que quedaba fuera del bloque congelado. El bloque `<frozen-after-approval>` no se tocó.
- **Falso positivo descartado:** el salto de línea inicial de `LANPartyHub.sln` (BOM + CRLF antes de la cabecera) es lo que produce `dotnet new sln` y `dotnet sln list` lo tolera; se restauró el fichero tras una edición byte a byte que lo dejó inválido.
- **Riesgo diferido a la Story 1.2:** el ERD declara `CLIENT_REGISTRATION.connectionState` como `string` mientras que el DTO `ClientRegistrationView` lo lleva como el enum `ConnectionState`. La conversión enum→columna es trivial y sin pérdida, pero el servidor debe fijar esa decisión al crear el esquema.
- **Riesgo diferido a las Stories 2.2 y 6.1:** AD-2 exige que la firma Ed25519 cubra el JSON **canónico** del manifiesto (sin espacios superfluos, claves ordenadas). `System.Text.Json` no ordena claves por defecto: el canonizador tiene que existir antes de que la firma sea verificable.

## Spec Change Log

- **2026-10-27 · Spec incorrecta en los tipos de los metadatos de catálogo.** *Disparador*: el verificador detectó que `GameDescriptor.distributionType`, `GameDescriptor.installationType` y `GameDescriptor.operatingSystem` se implementaron como `string` y `ActiveOperation.operationType` también, mientras la tarea y la nota de diseño exigían enumeraciones cerradas. *Amendado*: la tarea de `src/Shared/Contracts/` enumera ahora solo las enumeraciones que la espina exige (`DownloadState`, `InstallationState`, `OperationOutcome`, `ConnectionState`, `CatalogViewState`) y declara los cuatro campos restantes como cadenas; la nota de diseño «Vocabulario limitado al MVP» se sustituye por «Vocabulario de los metadatos de catálogo en cadena» con la cita del ERD (líneas 707-709) y la razón por la que `OperationType` no se introduce en `Shared`. *Estado malo evitado*: un `Shared` con 23 contratos de primer nivel en lugar de 20, que habría roto el AC5 recién alineado con AD-18 y la afirmación del test `Shared_declares_exactly_the_required_contracts_and_nothing_else`. *KEEP*: el bloque congelado, la matriz de I/O y la lista de los 20 contratos no se tocan.

## Review Triage Log

| ID | Ubicación | Veredicto | Evidencia y ruta |
|---|---|---|---|
| B1 | `_bmad-output/implementation-artifacts/epic-1-context.md:31` | medium | La lista de contratos sigue siendo la de 16 mientras `epics.md` ya dice 20; es el contexto que cargan las historias 1.2-1.4. → patch (grupo H) |
| B2 | `docs/estrategia-pruebas.md` §3 | low | AD-16 mete «ancla de confianza no coincidente rechazada antes de persistir» en el bucket unitarias/integración y esa fila no existe en la tabla. → patch (grupo G) |
| B3 | `docs/estrategia-pruebas.md` §2 y §5 | medium | AC7 exige «contenido declarado» por nivel; el documento declara el de AD-16 pero no dice qué historia implementa cada punto (endpoints con `WebApplicationFactory` y motor de transferencia HTTP no tienen dueño). → patch (grupo G) |
| B4 | `src/Shared/Contracts/StateDisplayMap.cs` | medium | No hay sobrecarga para `ConnectionState`, el enum que añade este mismo cambio; AD-27 exige que ese texto viva en `Shared`. → patch (grupo B) |
| B5 | `EXPERIENCE.md:210` | false | AD-10 fija exactamente tres estados de vista derivados; el catálogo de insignias es vocabulario de UI de la épica 11 y «en ejecución» pertenece a `IGameProcessRunner` (AD-19, historia 8.1). |
| B6 | `src/Shared/Contracts/ErrorCatalog.cs:61` | low | `ToApiError` sustituye el código entrante por `UNKNOWN_ERROR`, de modo que un código que el catálogo no conoce se pierde. → patch (grupo C) |
| B7 | `src/Shared/Contracts/ErrorCatalog.cs:10` | low | AD-25 dice «Ambas son **datos, no código**»: el catálogo mantiene un `Entry` paralelo a `ApiError` y una fábrica que lo construye. → patch (grupo C) |
| B8 | `tests/UnitTests/SharedContractInventoryTests.cs:45` | false | Los tipos anidados son miembros de su contrato, no contratos aparte: el recuento de 20 es correcto. La ceguera ante duplicados `internal` es B21/V23. |
| B9 | `src/Shared/Contracts/ClientStateChanged.cs:27` | low | `state` es cadena y la justificación del Design Note citaba el ERD de `DOWNLOAD_OPERATION`, que AD-22 tipa como enum: la cita es errónea y el contrato no dice qué cadena es. → patch (grupo D) |
| B10 | `src/Shared/Contracts/InstallationProgressUpdated.cs` | false | AD-18 fija la **forma** del payload (la lista de campos), no el vocabulario de valores; `phase` es por definición una etiqueta de progreso libre. `operationType` se resuelve en el grupo D. |
| B11 | `src/Shared/Contracts/GameDescriptor.cs:40` | false | El `string` del ERD es la columna serializada, igual que `categories[]`, `tags[]`, `images[]` y `dependencies[]`; no hay divergencia con AD-34. Se registra en Implementation Notes. |
| B12 | `src/Shared/Contracts/InstallationManifest.cs:44` | low | `SetLaunchConfig` repite el triple `path`/args/`workingDir` que ya declara `LaunchSettings`: dos formas del mismo concepto. → patch (grupo E) |
| B13 | `src/Shared/Contracts/InstallationManifest.cs:39` | medium | El discriminador `operation` y el orden de miembros ya son contrato de la forma canónica que AD-2 obliga a firmar; nada lo registra. → patch (grupo F) |
| B14 | `docs/estrategia-pruebas.md` §3.1 | medium | AD-8 rechaza por cuatro motivos (sin firma, firma inválida, `schemaVersion` desconocida, `sha256` distinto) y §3.1 afirma «los tres rechazos». → patch (grupo G) |
| B15 | `tests/EndToEndTests/Sm1AcceptanceScenarioTests.cs:41` | medium | Los tres tests comparan literales contra literales del mismo ensamblado: no demuestran que el escenario sea el de PRD §9. → patch (grupo G) |
| B16 | `README.md:43` | medium | «Reglas estructurales que la propia suite verifica» enumera garantías que la suite no comprueba (dirección hacia dentro, duplicación, `FrameworkReference`). → patch (grupo A) |
| B17 | `tests/UnitTests/RepositoryLayout.cs:48` | low | `FindRoot` sube buscando el `.sln`; en un layout sin él, toda la suite unitaria falla al inicializar. → patch (grupo J) |
| B18 | `.memlog.md` del run de épicas | reject | La atribución sí consta en el texto de la entrada («decisión humana 2026-10-27»); el log es de solo anexado y no admite edición, y anexar una entrada duplicada sería peor que la etiqueta ausente. |
| V1 | `DownloadStateMachine.cs:41` | reject | El estado sale de una transición declarada o del constructor; un valor fuera del enum exige un cast inválido del desarrollador, no demostrado. El arreglo añade guardas para estados no demostrados. |
| V2 | `DownloadStateMachine.cs:74` | reject | Mismo motivo que V1. |
| V3 | `DownloadStateMachine.cs:31` | reject | El constructor solo recibe estados declarados en el producto; una guarda sería una guarda para un estado no demostrado. |
| V4 | `InstallationStateMachine.cs:51` | reject | Mismo motivo que V1. |
| V5 | `InstallationStateMachine.cs:84` | reject | Mismo motivo que V1. |
| V6 | `InstallationStateMachine.cs:41` | reject | Mismo motivo que V3. |
| V7 | `DownloadStateMachine.cs:17-27` | false | AD-10 solo admite `Paused → Cancelled` y la UX ofrece «Cancelar» desde el estado pausado (`EXPERIENCE.md:350`). El diagrama es normativo. |
| V8 | `InstallationStateMachine.cs:17-37` | false | La tabla reproduce exactamente el diagrama de AD-10; no hay arista `Pending → Failed` ni de declinación de confirmación porque la espina no las define. |
| V9 | `StateDisplayMap.cs:12` | medium | Mismo hallazgo que B4. → patch (grupo B) |
| V10 | `StateDisplayMap.cs:12` | false | El `throw` por defecto ES la garantía de exhaustividad que AD-10 exige («un texto que no está en la tabla no existe»). |
| V11 | `ClientStateChanged.cs:27` | low | Sin contrato explícito de qué cadena es `state`. → patch (grupo D) |
| V12 | `ErrorCatalog.cs:66-77` | low | `Entries` expone el array vivo: un consumidor puede mutarlo. → patch (grupo C) |
| V13 | `ApiError.cs:12` | reject | El productor emite siempre los cuatro campos y todavía no existe deserializador; una guarda en el tipo sería una guarda para un estado no demostrado. |
| V14 | `ClientStateChanged.cs:11` | reject | El cliente es el productor y siempre pasa listas no nulas; no demostrado. |
| V15 | `ServerEndpoint.cs:9` | reject | El productor es el descubrimiento y la dirección manual de la historia 4.1; la validación del puerto pertenece a ese módulo. |
| V16 | `DownloadProgressUpdated.cs:11` | reject | El productor es el módulo de descarga de la historia 5.2; no demostrado. |
| V17 | `InstallationManifest.cs:39-46` | reject | El deserializador y su traducción a `ErrorCatalog` son de las historias 2.2 y 6.1; no hay punto de traducción que arreglar aquí. |
| V18 | `SqliteDownloadProgressStore.cs:92` | reject | Es ayudante de prueba, no código de producción, y el recorte no se ha demostrado. |
| V19 | `SqliteDownloadProgressStore.cs:22` | reject | La prueba crea el directorio antes de abrir el fichero; no demostrado. |
| V20 | `SqliteDownloadProgressStore.cs:95` | low | `Dispose` llama a `SqliteConnection.ClearAllPools()`, que vacía los pools de todo el proceso: puede tumbar otra clase de prueba en paralelo. → patch (grupo I) |
| V21 | `Sm1AcceptanceScenario.cs:42` | reject | La ejecución sobre LAN real es de la historia 13.1; la 1.1 solo declara el escenario (AC7). |
| V22 | `Sm1AcceptanceScenarioTests.cs:41-53` | reject | Mismo motivo que V21. |
| V23 | `SharedContractInventoryTests.cs:45` | medium | Comprobado con sonda: un `internal record ApiError` en `src/Server/Domain/` deja los 93 tests en verde, y AC5 prohíbe la duplicación aunque sea interna. → patch (grupo A) |
| V24 | `SharedContractInventoryTests.cs:128` | medium | Comprobado con sonda: `System.Net.Http` en `Shared` no lo detecta la comprobación, y AD-18 prohíbe también la dependencia de red. → patch (grupo A) |
| V25 | `ProjectDependencyTests.cs:156` | medium | Comprobado con sonda: `new System.Net.Http.HttpClient()` sin `using` pasa la comprobación del `Domain`. → patch (grupo A) |
| V26 | `ProjectDependencyTests.cs:184` | medium | Solo se comprueba que `Domain` no mencione `Infrastructure`; nada impide que `Application` importe `Infrastructure`. → patch (grupo A) |
| V27 | `ProjectDependencyTests.cs:233` | medium | La guarda afirma que SOLO `IntegrationTests` referencia SQLite: la historia 1.2, que debe llevar SQLite al servidor, hará fallar la suite con un falso positivo. → patch (grupo A) |
| V28 | `ProjectDependencyTests.cs:136` | low | `Assert.All` sobre TODOS los paquetes del cliente: cualquier paquete nuevo que no sea de Avalonia rompe la guarda con un mensaje engañoso. → patch (grupo A) |
| V29 | `ProjectDependencyTests.cs:13-19` | reject | La lista cubre los nombres plausibles y la espina nombra MonoTorrent; la alternativa (afirmar el conjunto exacto de paquetes) rompería legítimamente las historias 1.2 y 5.x. |
| V30 | `RepositoryLayout.cs:12` | low | Mismo hallazgo que B17. → patch (grupo J) |
| V31 | `src/Client/App.cs:19-26` | reject | El cliente solo se configura con el lifetime de escritorio clásico de Avalonia; la rama no se alcanza en el producto. |
| V32 | `global.json:3-4` | reject | `latestFeature` es el valor por defecto de `dotnet new globaljson` y el que fija el spec aprobado; acota el SDK a la banda 10.0.x y no hay un estado mal que evitar. La regresión sí se cubre con A5. |
| V33 | `StateDisplayMap.cs` | medium | Mismo hallazgo que B4/V9. → patch (grupo B) |
| V34 | `StateDisplayMap.cs` | false | La tabla guarda un texto por estado y toma el de las filas de detalle de `EXPERIENCE.md`; el vocabulario en minúsculas es el de las insignias, que es de la épica 11. |
| V35 | `GameDescriptor.cs:40` | false | Mismo hallazgo que B11. |
| V36 | `ClientRegistrationView.cs:13-19` | low | `LastSeenAt` puede viajar con un offset distinto de UTC y la convención de la espina exige UTC. → patch (grupo C) |
| V37 | `ClientErrorReported.cs:13` | false | El campo es obligatorio y vacío cuando no hay operación; hacerlo nullable violaría AD-22 («ningún payload admite campos opcionales fuera de esta lista»). |
| G1 | `SharedContractInventoryTests.cs:94` y `:105` | medium | Comprobado con sonda: la duplicación `internal` pasa. → patch (grupo A) |
| G2 | `ProjectDependencyTests.cs:140` | medium | Comprobado con sonda: la referencia completamente cualificada pasa. → patch (grupo A) |
| G3 | `SharedContractInventoryTests.cs:128` | medium | Comprobado con sonda: la pila de red no está en la lista de prohibidos. → patch (grupo A) |
| G4 | `StateDisplayMap.cs` | medium | Mismo hallazgo que B4/V9/V33. → patch (grupo B) |
| G5 | `LANPartyHub.sln` | medium | Comprobado con sonda: quitar `LANPartyHub.IntegrationTests` del `.sln` deja `dotnet test` en verde y el nivel de integración sin ejecutar. → patch (grupo A) |
| G6 | `global.json` | medium | Nada en el repositorio comprueba que exista ni que fije el SDK, que es lo que `AGENTS.md` exige. → patch (grupo A) |
| G7 | `ErrorCatalog.cs:66-77` | low | El XML doc de `TryGet` dice «without inventing one» pero asigna la entrada genérica al `out` y devuelve `false`: el doc y el comportamiento contradicen. → patch (grupo C) |

## Design Notes

- **Las capas son carpetas, no proyectos.** La espina define tres proyectos en `src/`; `Infrastructure → Application → Domain` se materializa como árbol de carpetas en cada desplegable, con un tipo marcador por capa para que la dirección de las dependencias sea comprobable.
- **Las máquinas de estado se declaran una sola vez.** AD-10 las sitúa en `Domain`, AD-18 prohíbe lógica en `Shared` y AD-1 prohíbe duplicar reglas entre servidor y cliente. El cliente es el único ejecutor (AD-10), así que las tablas de transición viven en `src/Client/Domain/StateMachines/` y el servidor solo proyecta; los enumeraciones viven en `Shared`, de modo que ambos lados comparten valores por construcción.
- **Vocabulario de los metadatos de catálogo en cadena.** `operatingSystem`, `distributionType` e `installationType` son cadenas porque el ERD de la espina las declara así (líneas 707-709) y AD-34 exige que la tabla `GAME` y el DTO no diverjan. Su vocabulario cerrado (solo `Portable` en el MVP, FR-8; solo la distribución desde el servidor, AD-28) lo restringe el módulo de dominio que los posee, no `Shared`. `activeOperations[].state` es una cadena con el nombre del valor, como hace la espina con `connectionState` y `validationStateText`; `operationType` es cadena por la misma razón: AD-18 no define un contrato `OperationType` y `Shared` debe quedarse en los 20 contratos que fijan AD-18, AD-3 y AD-27. Los identificadores son cadenas opacas UUID v7: un tipo fuerte por identificador multiplicaría el inventario de `Shared` y rompería el AC de «exactamente estos tipos».

## Verification

**Commands:**

- `dotnet build LANPartyHub.sln` -- expected: compilación sin errores ni advertencias
- `dotnet test LANPartyHub.sln` -- expected: los tres proyectos de prueba se ejecutan y pasan
- `powershell -NoProfile -File tools/check-encoding.ps1` -- expected: PASS, sin CJK ni U+FFFD
- `powershell -NoProfile -File tools/check-epics.ps1` -- expected: PASS (el backlog no se modifica salvo el AC5, si se aprueba la opción A)
