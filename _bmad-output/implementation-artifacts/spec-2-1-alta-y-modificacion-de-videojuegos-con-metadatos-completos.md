---
title: 'Alta y modificación de videojuegos con metadatos completos'
type: 'feature'
created: '2026-10-29'
status: 'in-review'
review_loop_iteration: 0
followup_review_recommended: false
baseline_revision: '3469bf5'
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-lan-party-hub-2026-10-26/ARCHITECTURE-SPINE.md'
warnings:
  - 'oversized'
deferred: []
---

<intent-contract>

## Intent

**Problem:** El servidor del evento no tiene ningún módulo de catálogo: la tabla `GAME` de `catalog.sqlite` existe con sus dieciocho columnas, pero ningún caso de uso la escribe, no hay superficie REST para el catálogo y no existe la pantalla de administración donde la organizadora da de alta un videojuego. El resultado es que el catálogo del evento está vacío y no hay forma de registrar ni de modificar un juego con sus metadatos obligatorios.

**Approach:** Cerrar el módulo `Catalog` del servidor de extremo a extremo: un caso de uso de aplicación que registra y modifica videojuegos sobre un puerto de repositorio, un adaptador EF Core que persiste en `catalog.sqlite`, la ruta `POST /api/v1/admin/games` que expone el alta y la modificación, y la pantalla «Catálogo» del servidor con su formulario de alta y edición que marca los campos obligatorios de FR-1 y presenta el tamaño en bytes exactos.

## Boundaries & Constraints

**Always:**
- `GameDescriptor` de `Shared` es el **único** contrato de metadatos de videojuego: 18 campos, orden fijo, sin campos opcionales ni condicionales (AD-34, AD-18). No se altera, no se extiende, no se duplica en `Server` ni en `Client`.
- La tabla `GAME` persiste **exactamente** los mismos 18 campos, con el **mismo** nombre y en el **mismo** orden (AD-18, AD-22). `sizeBytes` en bytes exactos; `launchConfiguration` en JSON canónico con la forma de `InstallationManifest.LaunchSettings`.
- `Game` es propiedad del servidor y vive en `catalog.sqlite`. Ningún módulo del servidor accede a la tabla de otro módulo (AR-13, AR-1).
- El identificador del juego es un UUID v7 opaco en forma canónica de 32 dígitos hexadecimales en minúsculas (`Guid.CreateVersion7().ToString("N")`).
- Toda mutación lleva `schemaVersion` en el cuerpo (AD-5). `GameDescriptor` no lleva ese campo y no puede añadirlo (AD-34), así que el cuerpo de la petición es un tipo **distinto** del descriptor.
- La versión del juego es una cadena opaca del editor, nunca un número.
- Los identificadores de la API (`gameId`) son opacos y no autentican nada: el servidor es de confianza por diseño y esta historia **no** introduce autenticación ni autorización de cliente (AD-30, política del repositorio).
- Toda respuesta de error usa la forma única `ApiError` = `{ code, message, cause, nextStep }` con `code` estable tomado de `ErrorCatalog`, y `cause`/`nextStep` obligatorios (AD-15, AD-18).
- La UI y los mensajes están en español; los identificadores, los nombres de tipo, los miembros y los comentarios de código en inglés (convención de nomenclatura de la espina).
- Las mutaciones de datos persistidos ocurren **solo** en `Infrastructure`, a través de un puerto de repositorio; la UI nunca toca EF Core (convención «State & cross-cutting»).
- Excepciones de dominio tipadas en `Domain`; un adaptador las traduce a la forma de error de API (convención «Errores»).

**Never:**
- No se implementa la publicación de paquetes, el manifiesto firmado, la licencia de publicación ni el bloqueo de publicación: pertenecen a Story 2.2 y 2.3.
- No se implementa `GET /api/v1/catalog`, `GET /api/v1/games/{gameId}` ni ninguna otra ruta del catálogo: pertenecen a historias posteriores.
- No se añade ningún tipo nuevo a `Shared`: AD-18 enumera exhaustivamente sus 20 contratos y `SharedContractInventoryTests` lo fija.
- No se crea un proyecto nuevo en la solución: son exactamente 6 y AD-1 exige un único artefacto desplegable para el servidor.
- No se implementa el sistema de tokens y tema de la interfaz: pertenece a Epic 11. La tipografía que exige AC4 se resuelve de forma local y acotada en el formulario.
- No se modifica la migración inicial `20261027133118_InitialCatalogSchema`: el esquema ya cumple AD-18.
- No se toca el vocabulario P2P: el MVP no es P2P (AD-13, AD-28).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| ALTA_VÁLIDA | Cuerpo con `schemaVersion` y los 18 metadatos de FR-1 presentes | `201` con el `GameDescriptor` persistido; la fila de `GAME` tiene los 18 valores con el nombre y el orden de AD-34; `gameId` es un UUID v7 canónico generado por el servidor | No error |
| MODIFICACIÓN_VÁLIDA | Cuerpo con `schemaVersion`, un `gameId` existente y versión, tamaño o configuración de lanzamiento distintos | `200` con el descriptor actualizado; el `gameId` **no** cambia; la fila conserva el resto de los metadatos | No error |
| ALTA_SIN_METADATOS_OBLIGATORIOS | Cuerpo con `schemaVersion` y algún metadato de FR-1 vacío o ausente | `422` con `ApiError`; el `code` nombra el campo que falta; **no** se escribe ninguna fila | `code` estable de `ErrorCatalog`, `message`/`cause`/`nextStep` en español y no vacíos |
| CUERPO_SIN_SCHEMA_VERSION | Cuerpo sin `schemaVersion` | `400` con `ApiError`; no se escribe ninguna fila | `code` estable de `ErrorCatalog` |
| CUERPO_CON_SCHEMA_VERSION_INCOMPATIBLE | Cuerpo con `schemaVersion` que el servidor no soporta | `400` con `ApiError`; no se escribe ninguna fila | `code` estable de `ErrorCatalog` |
| JUEGO_INEXISTENTE_EN_MODIFICACIÓN | Cuerpo con un `gameId` que no está en `GAME` | `404` con `ApiError`; no se escribe ninguna fila | `code` estable de `ErrorCatalog` |
| NOMBRE_DUPLICADO | Alta con un nombre que ya existe, con otro `gameId` | `201`; los dos registros se distinguen por `gameId` y `version` | No error |
| PERSISTENCIA_FALLIDA | Error de EF Core al guardar | `500` con `ApiError`; la operación queda registrada en el log con su `operationId` | `code` estable de `ErrorCatalog`; la excepción nunca alcanza el mensaje |

</intent-contract>

## Code Map

- `src/Shared/Contracts/GameDescriptor.cs` -- **CONTRATO DE LECTURA, NO SE TOCA.** Ya existe con los 18 campos de AD-34 en el orden exacto. `launchConfiguration` es `InstallationManifest.LaunchSettings` (`ExecutablePath`, `Arguments`, `WorkingDirectory`).
- `src/Shared/Contracts/InstallationManifest.cs` -- `InstallationManifest.LaunchSettings` es el tipo de `launchConfiguration`; se reutiliza, no se duplica.
- `src/Shared/Contracts/ApiError.cs` -- forma única del error de API `{ code, message, cause, nextStep }`.
- `src/Shared/Contracts/ErrorCatalog.cs` -- catálogo estable de códigos. **Se extiende** con los códigos nuevos de esta historia (metadato obligatorio ausente, `schemaVersion` ausente o incompatible, juego inexistente). Es el único lugar donde vive un código de error.
- `src/Server/Infrastructure/Persistence/Entities/GameEntity.cs` -- fila de `GAME`; ya existe con los 18 campos. Se reutiliza.
- `src/Server/Infrastructure/Persistence/CatalogDbContext.cs` -- `DbSet<GameEntity> Games` ya declarado; el mapeo explícito de las 18 columnas ya existe. **No se toca.**
- `src/Server/Infrastructure/Persistence/Migrations/20261027133118_InitialCatalogSchema.cs` -- crea `GAME` con las 18 columnas exactas. **No se toca**; no hace falta migración nueva.
- `src/Server/Application/Ports/IMetricsCounters.cs` -- puerto existente; `RecordError(code)` alimenta la métrica de errores por código.
- `src/Server/Domain/DomainLayer.cs` -- marcador de la capa de dominio; el marcador de la capa de aplicación está en `src/Server/Application/ApplicationLayer.cs`.
- `src/Server/Application/ApplicationLayer.cs` -- marcador de la capa de aplicación.
- `src/Server/Infrastructure/InfrastructureLayer.cs` -- marcador de la capa de infraestructura.
- `src/Server/Host/MetricsEndpoint.cs` -- **PLANTILLA del patrón de endpoint**: `public const string Route`, `Map(WebApplication app, ...)`, `app.MapGet(Route, () => Results.Text(Serialize(...), "application/json", Encoding.UTF8))`.
- `src/Server/Host/HostMetrics.cs` -- implementación de `IMetricsCounters`; `RecordError` cuenta por código.
- `src/Server/Infrastructure/Http/CorrelationMiddleware.cs` -- **`ClientIdHeaderName`, `OperationIdHeaderName`, `CanonicalIdentifierLength` (32)**. El middleware rechaza con `400` toda petición de `/api/v1` sin `X-Client-Id` canónico. La superficie de administración **no** es superficie de cliente: `Program.cs` instala el middleware con `app.UseWhen(...)` y ese predicado se ajusta para excluir `/api/v1/admin`.
- `src/Server/Infrastructure/Logging/CorrelationScope.cs` -- `CorrelationScopeFactory.Open(operationId, clientId)`; el log es JSON con scopes (`OperationId`, `ClientId`).
- `src/Server/Program.cs` -- raíz de composición: registro de DI, `builder.Logging.AddJsonConsole`, migraciones al arranque, `app.UseWhen(...)` de la correlación, `MetricsEndpoint.Map(app, ...)`. Aquí se mapea la ruta de administración.
- `src/Server/LANPartyHub.Server.csproj` -- `Microsoft.NET.Sdk.Web`, referencia a `Shared`, EF Core 10.0.13, `Haukcode.Mdns` 1.0.22.
- `src/Client/Presentation/MainWindow.cs` -- **PLANTILLA DEL ESTILO DE UI**: UI construida en código C# (no hay XAML en el repositorio), sin MVVM, sin `INotifyPropertyChanged`.
- `src/Client/App.cs` -- patrón de `Avalonia.Application` (`Initialize()` con `FluentTheme`, `OnFrameworkInitializationCompleted`).
- `src/Client/LANPartyHub.Client.csproj` -- versiones Avalonia fijadas: `12.1.4` (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`).
- `tests/IntegrationTests/TestServer.cs` -- **PLANTILLA DE INTEGRACIÓN**: `TestServer.StartAsync(dataDirectory, ct)` arranca el host real (Kestrel real, sockets reales) sobre una carpeta temporal con `catalog.sqlite` real; `CreateClient()` devuelve un `HttpClient` con la base del host.
- `tests/IntegrationTests/ServerStartupTests.cs` -- plantilla de aserción sobre la tabla real vía `pragma_table_info('GAME')` y `SELECT name FROM sqlite_master`.
- `tests/IntegrationTests/ServerCorrelationTests.cs` -- plantilla de prueba de endpoint `/api/v1` con cabeceras de correlación y el helper `AssertRejected(body, expectedCode)` que valida `{ code, message, cause, nextStep }`.
- `tests/UnitTests/SharedContractInventoryTests.cs` -- fija **exactamente 20** contratos públicos en `Shared`; su lista no cambia porque esta historia no añade nada a `Shared`.
- `tests/UnitTests/ProjectDependencyTests.cs` -- fija **exactamente 6** proyectos en la solución; `No_type_of_the_domain_layer_imports_infrastructure_or_a_framework` prohíbe la cadena `Avalonia` en `src/Server/Domain`; `No_type_of_the_application_layer_imports_a_framework` prohíbe `Avalonia` y `System.Net.Http` en `src/Server/Application`; `No_type_of_the_infrastructure_layer_references_the_presentation_layer` prohíbe la cadena `Presentation` en `src/Server/Infrastructure`.
- `docs/plan.md` -- §8: escala S0/S1/S2/S3; máximo **dos** ciclos automáticos de corrección y revisión por historia.

### Anclajes verificados en el código (investigación del 2026-10-29)

Estos detalles vienen de leer el código real; el implementer los usa como plantilla literal.

- **`GameDescriptor` es un `record` posicional**, no un record con propiedades: `public sealed record GameDescriptor(string GameId, string Name, ...)` con los 18 parámetros en el orden de AD-34. `Categories`, `Tags`, `Images` y `Dependencies` son `IReadOnlyList<string>`; `SizeBytes` es `long`; `MinPlayers`/`MaxPlayers` son `int`. `LaunchConfiguration` es `InstallationManifest.LaunchSettings`, que a su vez es `record LaunchSettings(string ExecutablePath, IReadOnlyList<string> Arguments, string WorkingDirectory)`.
- **`GameEntity` es una `class` con propiedades**, no un record: `string[]` en las colecciones y `Shared.Contracts.InstallationManifest.LaunchSettings` **con nombre completamente cualificado** (sin `using`). El mapeo de EF Core es **config-based dentro de `OnModelCreating`**, con un bloque lambda `modelBuilder.Entity<GameEntity>(entity => { ... })`, y las columnas de array se configuran con el helper privado `ConfigureStringArray(...)` (que aplica `HasConversion(StringArrayToJsonConverter)`, `IsRequired()` y el `StringArrayComparer`). `launchConfiguration` usa `HasConversion(LaunchSettingsToJsonConverter)`. El `DbContext` **no se toca**.
- **`ErrorCatalog` es el único hogar de los códigos.** Estilo a seguir: `public const string <NombrePascal>Code = "NOMBRE_EN_MAYUSCULAS";` y luego una entrada `new(codigo, mensaje, causa, siguientePaso)` en el array privado `CatalogEntries`, con los tres textos **en español**. Códigos actuales: `UNKNOWN_ERROR`, `ANCHOR_FINGERPRINT_MISMATCH`, `MANIFEST_SIGNATURE_INVALID`, `PACKAGE_INTEGRITY_MISMATCH`, `MACHINE_NAME_INVALID`, `CLIENT_ID_MISSING`, `CLIENT_ID_INVALID`. El `code` viaja como campo estructurado del log, nunca en el texto del mensaje (AD-25). Existe `ErrorCatalog.Resolve(code)` que devuelve la entrada conocida y, para un código desconocido, conserva el código pero toma los textos de `UNKNOWN_ERROR`.
- **`CorrelationMiddleware` no conoce rutas**: rechaza *toda* petición que ve. El recorte a `/api/v1` vive **solo** en el predicado de `app.UseWhen` de `Program.cs`, que hoy es `context.Request.Path.StartsWithSegments(ClientSurfacePrefix, StringComparison.OrdinalIgnoreCase)` con `ClientSurfacePrefix = "/api/v1"`. Como `StartsWithSegments("/api/v1")` **también** captura `/api/v1/admin/...`, la exclusión de la superficie de administración es **un cambio de ese predicado en `Program.cs`**, y nada más.
- **Patrón de endpoint** (`MetricsEndpoint`): `public const string Route`, `private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);`, un `public static string Serialize(...)` y un `public static void Map(WebApplication app, ...)` con `app.MapGet(Route, () => Results.Text(Serialize(...), "application/json", Encoding.UTF8))`. El `Map` de la nueva ruta se llama desde `Program.cs` junto a `MetricsEndpoint.Map(app, ...)`.
- **`IMetricsCounters`**: `RecordBytesWritten(long)`, `OperationStarted()`, `OperationCompleted()`, `RecordError(string code)`.
- **UI**: no hay ningún `.axaml` en el repositorio; la UI del cliente se construye en código C# con `new StackPanel { Margin = new Thickness(16), Spacing = 8, Children = { ... } }` y `TextBox { PlaceholderText = ... }`, `Button { Content = ... }`, `TextBlock { TextWrapping = TextWrapping.Wrap }`. El `App` del cliente deriva de `Avalonia.Application` con el tipo **completamente cualificado** porque `LANPartyHub.Client.Application` colisiona con el namespace `Avalonia.Application`: `public sealed class App : Avalonia.Application`. El `ServerApp` de esta historia tiene la **misma** colisión (`LANPartyHub.Server.Application`) y debe usar el tipo cualificado igual.
- **Colisión de namespace en el servidor**: `LANPartyHub.Server.Application` (capa) y `LANPartyHub.Server.Application.Ports` ya existen; los casos de uso nuevos van en `LANPartyHub.Server.Application.Catalog` y `src/Server/Presentation` **no** puede contener la palabra `Presentation` referenciada desde `src/Server/Infrastructure` (guard de substring).
- **Guards que la historia tiene que respetar**:
  - `SharedContractInventoryTests` cuenta los tipos públicos de nivel superior exportados del ensamblado `Shared` y exige **exactamente 20**. También comprueba que **ningún** tipo de `Server`/`Client` se llame como un contrato de `Shared` (usa `DeclaredTopLevelTypes`, que incluye los no públicos): un `GameDescriptor` en `Server` rompería el guard. El DTO de la petición **no** puede llamarse `GameDescriptor`.
  - `ProjectDependencyTests` comprueba los **6** proyectos de la solución leyendo `LANPartyHub.sln`, y hace **substring** sobre el texto de los ficheros: en `src/Server/Domain` y `src/Client/Domain` no puede aparecer la cadena `Avalonia` (ni `Infrastructure`, ni `Microsoft.EntityFrameworkCore`, ni `System.Net.Http`); en `src/Server/Application` y `src/Client/Application` no puede aparecer `Avalonia`, `System.Net.Http`, `System.Data.Common`, `System.Net.Sockets`, `System.Data.Common`, `Microsoft.EntityFrameworkCore` ni `Microsoft.AspNetCore`; en `src/Server/Infrastructure` y `src/Client/Infrastructure` no puede aparecer la cadena `Presentation` **ni en un comentario**. La capa `Application` solo puede importar `Microsoft.Extensions.Logging` del framework.
  - `Shared` no puede referenciar ningún paquete, framework ni proyecto.
- **Estilo de repositorio (sin `.editorconfig`)**: 4 espacios, namespaces de ámbito de fichero, XML doc en **cada** tipo, miembro y parámetro públicos (`<summary>`, `<param>`, `<returns>`, `<exception cref>`), `ArgumentNullException.ThrowIfNull` al inicio de los métodos públicos con referencias, `var` libre, expresiones de colección `[...]` en lugar de `new[] { }`, `const` para constantes y `private static readonly` para cachés estáticas. Texto de log, comentarios y documentación **en español**; identificadores y nombres de tipo **en inglés**.
- **Guards de verificación**: `tools/check-encoding.ps1` solo recorre ficheros `*.md` de `docs`, `_bmad-output`, `_bmad`, `AGENTS.md` y `tools`; falla con BOM UTF-8 al inicio, con cualquier CJK (`U+3000–U+303F`, `U+3040–U+309F`, `U+30A0–U+30FF`, `U+3400–U+4DBF`, `U+4E00–U+9FFF`, `U+AC00–U+D7AF`, `U+F900–U+FAFF`, `U+FF00–U+FFEF`) y con `U+FFFD`. `tools/check-epics.ps1` solo lee el `epics.md` de la épica: recuentos de epicas/historias/GWT, paridad Given==When==Then, continuidad de IDs de historia, rango de identificadores citados (`FR 1–11`, `NFR 1–10`, `UX-DR 1–45`, `AR 1–25`, `AD 1–34`, `SM 1–5`, `SM-C 1–2`), trazabilidad AR bidireccional y marcadores de plantilla (`TODO`, `TBD`, `{{...}}`, `<PLACEHOLDER>`). Ninguno de los dos toca `src/` ni `tests/`.
- **Plantilla de prueba de la tabla real** (`ServerStartupTests`): `SELECT name FROM pragma_table_info('GAME')` comparado con la lista de columnas separada por comas, y `SELECT name FROM sqlite_master WHERE type = 'table'`. El `TestServer` arranca el host real con `Program.BuildServerAsync(["--data-dir", folder, "--http-port", port])` sobre una carpeta temporal, y `catalog.sqlite` lo crea el propio host.
- **Contexto de continuidad de la épica anterior**: la última historia de la Epic 1 (`1-4-observabilidad-correlacionada-desde-el-primer-arranque`) está fusionada en `main` y de ella se hereda el patrón de correlación (`X-Client-Id`, `X-Operation-Id`, forma única `ApiError`, log JSON con scopes) y la instrumentación de métricas (`HostMetrics`).


## Tasks & Acceptance

**Execution:**
- `src/Shared/Contracts/ErrorCatalog.cs` -- añadir los códigos estables de esta historia (metadato obligatorio ausente, `schemaVersion` ausente, `schemaVersion` incompatible, juego inexistente) y sus entradas de tres partes en español -- AD-15 exige un código estable por caso de fallo y `ErrorCatalog` es su único hogar (AD-18).
- `src/Server/Domain/Catalog/GameRegistration.cs` -- entidad de dominio `Game` con los 18 metadatos, invariantes de FR-1 y excepciones tipadas de dominio para los campos ausentes y los rangos inválidos -- AD-1: las reglas viven en `Domain` y no en el adaptador; el mensaje nombra el campo concreto que falla (AD-25).
- `src/Server/Domain/Catalog/CatalogDomainExceptions.cs` -- excepciones de dominio tipadas (`GameMetadataMissingException`, `GameNotFoundException`, `SchemaVersionNotSupportedException`) -- convención «Errores»: el adaptador traduce la excepción a `ApiError`, nunca al revés.
- `src/Server/Application/Ports/IGameRepository.cs` -- puerto de repositorio del módulo `Catalog` con las operaciones de alta y modificación -- AD-1 y convención «State & cross-cutting»: la mutación de lo persistido ocurre solo en `Infrastructure` a través de un puerto.
- `src/Server/Application/Catalog/RegisterGame.cs` y `src/Server/Application/Catalog/ModifyGame.cs` -- casos de uso que validan FR-1, generan el `gameId` UUID v7, delegan la persistencia en el puerto y devuelven el `GameDescriptor` -- el caso de uso no depende del adaptador (AR-1).
- `src/Server/Infrastructure/Persistence/GameRepository.cs` -- adaptador EF Core que escribe y lee la tabla `GAME` mapeando los 18 campos con el mismo nombre y el mismo orden -- AD-34 y AD-22: la tabla y el DTO no pueden divergir.
- `src/Server/Host/GamesEndpoint.cs` -- endpoint `POST /api/v1/admin/games` que deserializa el cuerpo con `schemaVersion`, invoca el caso de uso y traduce las excepciones de dominio a `ApiError` -- AD-5 (mutación con `schemaVersion` en el cuerpo) y plantilla `MetricsEndpoint.Map`.
- `src/Server/Host/GameRequest.cs` -- DTO de la petición de administración: `schemaVersion` + los 18 campos de `GameDescriptor` -- AD-34 impide añadir `schemaVersion` al descriptor, así que el cuerpo es un tipo **distinto**; vive en `Server` porque AD-18 fija exhaustivamente los 20 contratos de `Shared` y no se puede ampliar sin enmendar la espina.
- `src/Server/Program.cs` -- registrar los casos de uso y el repositorio en el DI, y mapear la ruta de administración; ajustar el predicado de `app.UseWhen` para que la superficie `/api/v1/admin` no exija `X-Client-Id` -- la organizadora no es un cliente de la LAN: el `clientId` es clave de correlación, no credencial (AD-30, AD-15), y la política del repositorio prohíbe añadir autenticación de cliente.
- `src/Server/Presentation/Catalog/GameFormViewModel.cs` -- modelo de vista del formulario de alta y edición: refleja los 18 campos, marca como obligatorio lo que FR-1 exige y expone el tamaño derivado en GB decimales -- AC4 y convención de la espina: el texto de UI en español, los identificadores en inglés.
- `src/Server/Presentation/Catalog/GameFormView.cs` -- ventana del formulario construida en código C# (patrón `MainWindow` del cliente), con la ruta de lanzamiento y la versión en monoespaciada y el tamaño en el token `size` con la unidad `GB` en el token `meta` -- AC4 (UX-DR-2, UX-DR-5, UX-DR-44) y regla única de tamaños: GB decimales (10⁹ bytes), separador decimal español, exactamente un decimal.
- `src/Server/Presentation/Formatting/SizeFormatter.cs` -- formateador de tamaño: bytes exactos a la forma «<n>,<d> GB» con separador decimal español y exactamente un decimal -- AD-7: la conversión a GB decimales es de presentación y no del modelo; nunca MiB/GiB, nunca dos decimales, nunca sin unidad.
- `src/Server/Presentation/Catalog/CatalogWindow.cs` -- ventana de la sección «Catálogo» del servidor: columna de navegación y área principal, con la lista de videojuegos y el acceso al formulario -- DESIGN.md: el servidor tiene columna izquierda de navegación (Catálogo, Clientes, Paquetes) y área principal.
- `src/Server/Presentation/ServerApp.cs` y `src/Server/Presentation/Catalog/CatalogViewModel.cs` -- aplicación Avalonia del servidor y modelo de vista de la sección, siguiendo el patrón de `src/Client/App.cs` -- AD-1: el servidor es un único artefacto desplegable, así que la UI vive dentro de `src/Server` y no en un proyecto nuevo.
- `src/Server/LANPartyHub.Server.csproj` -- referenciar `Avalonia`, `Avalonia.Desktop` y `Avalonia.Themes.Fluent` en la versión `12.1.4` -- la versión la fija la espina. Ojo: hoy el servidor **no** referencia Avalonia y `ProjectDependencyTests.The_avalonia_packages_are_pinned_to_the_version_of_the_spine` solo mira el csproj del **cliente**; esta historia añade la referencia del servidor y esa comprobación se queda como está (no se toca el guard para no ampliar el alcance).
- `tests/UnitTests/GameMetadataTests.cs` -- pruebas unitarias de los invariantes de dominio: metadato obligatorio ausente identificado por su nombre, `minPlayers` mayor que `maxPlayers` rechazado, versión como cadena opaca, `sizeBytes` en bytes exactos -- AC1 y AC2; el dominio es la capa que sostiene las reglas (AD-1).
- `tests/UnitTests/SizeFormatterTests.cs` -- pruebas unitarias del formateador: «7,6 GB» para 7_600_000_000 bytes, separador decimal español, exactamente un decimal, nunca dos decimales ni unidad ausente -- AC4 y regla única de tamaños.
- `tests/IntegrationTests/GameCatalogTests.cs` -- pruebas de integración contra `catalog.sqlite` real y el host real: alta que persiste los 18 campos con el nombre y el orden de AD-34 (leídos con `pragma_table_info`), modificación que conserva el `gameId` y refleja el cambio, dos registros con el mismo nombre distinguidos por `gameId` y versión, rechazo de metadato obligatorio ausente sin escribir fila, y rechazo sin `schemaVersion` -- AC1, AC2, AC3, AC5; el nivel de prueba que exige la historia es integración.
- `tests/IntegrationTests/ServerCorrelationTests.cs` -- extender para comprobar que la superficie de administración no exige `X-Client-Id` y que el registro de la operación queda correlacionado por `operationId` -- AD-15: la correlación es obligatoria en toda operación.

**Acceptance Criteria:**
- Given la sección Catálogo del servidor, when la organizadora da de alta un videojuego, then se registran identificador único, nombre, descripción, categorías, etiquetas, carátula, imágenes, versión, tamaño, jugadores mínimo y máximo, sistema operativo, tipo de distribución, tipo de instalación, configuración de lanzamiento, dependencias, licencia y restricciones de distribución, and esos metadatos viajan con la forma exacta del contrato `GameDescriptor` de `Shared` —los mismos campos, en el mismo orden, sin campos opcionales ni condicionales— y la entidad `GAME` del servidor los persiste con ese mismo nombre y ese mismo orden.
- Given un videojuego persistido, when se consulta, then conserva un identificador único y todos los metadatos obligatorios, and dos registros con el mismo nombre se distinguen por identificador y versión.
- Given un videojuego guardado, when se modifica su versión, tamaño o configuración de lanzamiento, then el registro conserva su identificador y refleja el cambio.
- Given la sección Catálogo del servidor, when se abre el formulario de alta o edición, then el formulario marca como obligatorio lo que FR-1 exige y la ruta de lanzamiento y la versión se muestran en monoespaciada, and el tamaño se introduce en bytes exactos y se presenta como valor decimal derivado («7,6» en el token de tamaño y «GB» en el token de meta).
- Given el servidor, when se inspecciona la persistencia, then `Game` es propiedad del servidor y vive en `catalog.sqlite`, and ningún módulo del servidor accede a la tabla de otro módulo.
- Given una petición de alta o modificación, when el cuerpo no lleva `schemaVersion` o lleva una versión no soportada, then se rechaza con un `ApiError` de tres partes y no se escribe ninguna fila.
- Given una petición de alta con un metadato obligatorio vacío, when se procesa, then se rechaza con un `ApiError` cuyo `code` nombra el campo que falta y no se escribe ninguna fila.
- Given una modificación de un `gameId` inexistente, when se procesa, then se responde `404` con un `ApiError` y no se escribe ninguna fila.
- Given el servidor en marcha, when se registra un juego, then la entrada del log lleva el `operationId` de la unidad de trabajo y el error se cuenta por su código estable cuando la operación falla.

## Spec Change Log

## Review Triage Log

## Design Notes

**Por qué el DTO de la petición vive en `Server` y no en `Shared`.** AD-5 dice «los DTOs de API son de `Shared`» y AD-18 enumera exhaustivamente los 20 contratos de `Shared`, con un guard (`SharedContractInventoryTests`) que lo fija. AD-34 exige que `GameDescriptor` tenga una lista de campos **exacta** y que no admita campos opcionales, así que el `schemaVersion` que AD-5 exige en el cuerpo de la mutación no puede entrar en el descriptor. Añadir un contrato nuevo a `Shared` obligaría a enmendar la espina (AD-18) y el guard, y esa es una decisión de arquitectura que excede el alcance de una historia. La lectura que se aplica: `Shared` aloja los contratos **compartidos entre servidor y cliente** —la forma congelada que ambos lados hablan—, y `GameDescriptor` es precisamente el contrato de ese tipo. El cuerpo de una petición de **administración** lo consume solo el servidor, no el cliente, y por tanto es un tipo del servidor. El descriptor es la forma de la **respuesta** y esa sí viaja por `Shared`.

**Por qué la superficie de administración no pide `X-Client-Id`.** `CorrelationMiddleware` rechaza con `400` toda petición bajo `/api/v1` que no lleve un `X-Client-Id` canónico, porque esa superficie es la del cliente de la LAN. La ruta de administración la usa el organizador, que no es un cliente ni tiene identidad de cliente en el catálogo. El `clientId` es, por AD-30, una clave de correlación y no una credencial, y la política del repositorio prohíbe expresamente añadir autenticación o autorización de cliente: la LAN es de confianza por diseño. La solución no es relajar el middleware para toda la superficie, sino que el predicado de `app.UseWhen` deje de cubrir `/api/v1/admin`, de modo que la superficie del cliente sigue exigiendo su identificador y la de administración conserva la correlación por `operationId`, que es lo que AD-15 exige para toda operación.

**Por qué la UI del servidor vive dentro de `src/Server`.** AD-1 fija el servidor como «un monolito modular en un único proceso y un único artefacto desplegable», la espina declara solo dos artefactos desplegables (`src/Server` y `src/Client`) y `ProjectDependencyTests` fija exactamente 6 proyectos en la solución. Crear un `src/ServerUI` violaría las tres cosas a la vez. La UI del servidor es por tanto una capa `Presentation` dentro del proyecto del servidor, que es exactamente la forma en que el cliente ya tiene la suya (`src/Client/Presentation`). La aplicación es una `Avalonia.Application` como la del cliente, y el servidor sigue siendo un único binario: la UI y el host HTTP conviven en el mismo proceso.

**Sobre los tokens de tipografía.** AC4 exige que el tamaño se presente en el token `{typography.size}` y la unidad en `{typography.meta}`, y que la ruta de lanzamiento y la versión vayan en monoespaciada. El sistema de tokens y tema es de la Epic 11, que es posterior. Esta historia resuelve la AC de forma local y acotada: define en la capa `Presentation` del servidor las constantes de fuente y de cuerpo que reproduce lo que la AC exige (monoespaciada para literales y para el valor derivado del tamaño, cuerpo pequeño para la unidad), sin anticipar el sistema de tokens ni tocar la UI del cliente. Queda documentado como deuda a favour de la Epic 11.

**El tamaño se introduce en bytes exactos y se presenta derivado.** AD-7 exige que `sizeBytes` viva en bytes exactos y que la conversión a GB decimales sea una regla de presentación. El formulario, por tanto, **escribe y edita bytes exactos** (el valor persistido es el que la organizadora teclea, y no se redondea al escribir), y **presenta** el valor derivado con un decimal y separador español. La regla única de tamaños no admite MiB/GiB, ni dos decimales, ni la ausencia de unidad.

## Verification

**Commands:**
- `dotnet build LANPartyHub.sln` -- expected: SUCCESS, sin errores ni advertencias nuevas.
- `dotnet test LANPartyHub.sln` -- expected: SUCCESS, todos los tests en verde, incluidos los nuevos de `GameMetadataTests`, `SizeFormatterTests` y `GameCatalogTests`.
- `powershell -NoProfile -File tools/check-encoding.ps1` -- expected: SUCCESS, `OK: sin codepoints CJK ni U+FFFD`.
- `powershell -NoProfile -File tools/check-epics.ps1` -- expected: SUCCESS, sin huecos de recuento, paridad ni trazabilidad AR.

**Manual checks:**
- Inspeccionar la tabla `GAME` de la `catalog.sqlite` creada por la prueba de integración con `pragma_table_info('GAME')`: las 18 columnas con el nombre y el orden de AD-34, sin columnas nuevas.
- Inspeccionar el cuerpo de un `ApiError` de rechazo: `code`, `message`, `cause` y `nextStep` presentes y no vacíos, con el código tomado de `ErrorCatalog`.
