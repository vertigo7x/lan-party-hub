---
title: 'Story 9.1: Tabla densa de clientes conectados'
type: 'feature'
created: '2026-11-09'
status: 'in-review'
baseline_revision: 'e4d8d486bb0ad4e1459ebb7d621f0d65d27a4128'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '_bmad-output/implementation-artifacts/epic-9-context.md'
  - '_bmad-output/planning-artifacts/architecture/architecture-lan-party-hub-2026-10-26/ARCHITECTURE-SPINE.md'
warnings:
  - oversized
deferred: []
---

<intent-contract>

## Intent

**Problem:** El organizador no tiene ninguna vista de los equipos de la LAN. El servidor ya persiste
la fila de `CLIENT_REGISTRATION`, pero no existe operación de registro del cliente, niendpoint que
devuelva la lista de clientes, ni tabla en la interfaz del servidor: el organizador no puede saber
qué equipos están conectados, ni cómo se llaman de verdad, ni qué versión hablan.

**Approach:** Cerrar el contrato REST del registro en una sola operación (alta, reconexión y
renombrado en `POST /api/v1/clients`), derivar de forma determinista el nombre efectivo del equipo
cuando dos clientes proponen el mismo nombre, exponer la vista de clientes por REST
(`GET /api/v1/clients`) y pintar esa vista como una **tabla densa** de 40 px en la sección «Clientes»
del servidor, con el cliente registrando su nombre efectivo al conectarse.

## Boundaries & Constraints

**Always:**

- `POST /api/v1/clients` es el **único** punto de entrada del cliente al servidor: alta, reconexión y
  renombrado ocurren en esa operación. No existe endpoint adicional de registro, de renombrado ni de
  administración de nombres (AD-32).
- Cuerpo del registro con forma exacta `{ clientId, machineName, clientVersion, protocolVersion }`,
  **sin `schemaVersion`**: el registro es la única mutación exenta (AD-32, AD-5).
- Respuesta del registro con forma exacta `{ clientId, machineName, connectionState, lastSeenAt, renamed }`,
  donde `machineName` es **siempre el nombre efectivo** y `renamed` vale `true` **únicamente** cuando
  hubo colisión y el nombre devuelto difiere del propuesto (AD-32).
- Estados HTTP: `200 OK` en éxito (nunca `201`), `422 Unprocessable Content` por nombre mal formado,
  `426 Upgrade Required` por versión incompatible —comprobado **antes** de validar el nombre (AD-32).
- La vista de clientes se obtiene **exclusivamente** por `GET /api/v1/clients`. El evento
  `ClientsSnapshotChanged` **no existe** y no se añade al hub (AD-29, R-MVP-9).
- `ClientRegistrationView` es el contrato único y ya existe en `Shared`: `clientId`, `machineName`,
  `clientVersion`, `protocolVersion`, `connectionState`, `lastSeenAt` (AD-18, AD-29). **No** se añade
  ningún campo nuevo ni ningún tipo nuevo a `Shared`.
- El `clientId` es la clave técnica de la fila y **nunca** cambia. El servidor no deduplica, no agrupa,
  no fusiona ni reasigna filas por `machineName` (AD-23, AD-30).
- Reglas de forma del `machineName`: no vacío tras recortar; entre 1 y 64 **puntos de código Unicode**
  (`System.Text.Rune`); sin caracteres de control; sin espacios iniciales, finales ni consecutivos;
  set admitido de letras, dígitos, `.`, `-`, `_` y espacio interior simple (AD-30).
- Unicidad comparada por forma canónica: NFKC → eliminación de marcas `Mn`/`Mc`/`Me` → minúsculas
  Unicode invariantes, **en ese orden** (AD-30).
- La colisión **no es un error**: el registro tiene éxito con `renamed: true` y el nombre efectivo.
  El nombre efectivo derivado es `stem_truncado` + `"-"` + los 32 dígitos hexadecimales en minúsculas
  del `clientId` en forma `Guid.ToString("N")`; sufijo de **33 puntos de código fijos**, `stem`
  recortado a **≤ 31 puntos de código** por puntos de código (nunca partiendo un emoji), `stem` de
  reserva `equipo` si quedara vacío. Sin búsqueda de nombres libres ni contadores (AD-31).
- Un nombre efectivo derivado **vuelve a ser admisible** por las reglas de forma y **no necesita
  volver a ser derivado** (AD-31).
- Reintento de reconexión: el cliente que se registra con su `clientId` y su **nombre efectivo**
  conserva fila y nombre; el servidor actualiza `connectionState`, `lastSeenAt` y `clientVersion`
  y **no** dispara la derivación, de modo que un cliente reconectado nunca colisiona consigo mismo
  (AD-30, AD-32).
- Rechazo por nombre inválido y colisión se registran en el logging estructurado **JSON** con
  `clientId` y `operationId` de correlación y con `code` del `ErrorCatalog` **distintos entre sí**
  (AD-15, AD-25, AD-30).
- El `machineName` **no es una credencial**: no hay contraseña, token, sesión ni rol; no autentica
  ninguna petición ni confiere permisos (AD-6, AD-12, AD-30).
- La tabla es **densa**, no una colección de tarjetas: una fila por cliente, **40 px** de alto, celdas
  de dato en `{typography.body-strong}` y unidades en `{typography.meta}`, separador inferior de 1 px
  `{colors.border-hairline}`, **orden estable por nombre de equipo**, fila seleccionable para ver su
  detalle (UX-DR-14, DESIGN `data-table-row`).
- La columna de nombre muestra **siempre el nombre efectivo**, nunca el nombre propuesto que colisionó
  (AD-30, UX-DR-14).
- La tabla muestra: identificador, nombre del equipo, estado de conexión, juegos y versiones
  instalados, descargas activas, progreso de las instalaciones y errores registrados (FR-10). Las
  columnas cuyo dato pertenece a las historias 9.2 y 9.3 se **reservan visibles** con su encabezado
  pero sin dato todavía, y su contenido llega en 9.2 y 9.3.
- Documentación y microcopia en español; identificadores, contratos, miembros y comentarios de código
  en inglés (convención del repositorio).

**Never:**

- **No** añadir ningún tipo público nuevo a `Shared`: `SharedContractInventoryTests` afirma que
  declara exactamente 22 contratos. Los DTOs de petición y respuesta viven en `src/Server/Host/`.
- **No** añadir métodos al hub `CatalogHub`: la vista de clientes viaja por REST (AD-29).
- **No** añadir autenticación, autorización, contraseñas, tokens, sesiones ni roles (AD-6, AD-12).
- **No** buscar nombres libres, no probar candidatos sucesivos, no incrementar contadores (AD-31).
- **No** cambiar el `clientId` de nadie, no fusionar filas, no reasignar la fila de otro cliente
  (AD-23, AD-30).
- **No** crear endpoint adicional de registro, de renombrado ni de administración de nombres (AD-30).
- **No** usar `System.Net.Http` en el servidor: el ensamblado del servidor no lo referencia
  (`ProjectDependencyTests`).
- **No** mencionar las cadenas «Infrastructure» ni «Presentation» en `src/Server/Application/**`
  (`ProjectDependencyTests`); la capa Application solo puede importar `Microsoft.Extensions.Logging`.
- **No** inventar microcopia fuera de `StateDisplayMap`: el texto del estado de conexión sale de
  `StateDisplayMap.GetText(ConnectionState)` (AD-25).
- **No** instalar dependencias P2P ni vocabulario de peer/swarm/tracker/seeding (AD-28).
- **No** modificar `sprint-status.yaml`: es propiedad del orquestador.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| ALTA_SIMPLE | `machineName` "EQ-PC" libre, `clientId` nuevo | `200 OK`, cuerpo `{ clientId, machineName: "EQ-PC", connectionState, lastSeenAt, renamed: false }`; fila persistida con `machineName` "EQ-PC" | Sin error |
| RECONEXION | `clientId` conocido + `machineName` igual a su nombre efectivo | `200 OK`, `renamed: false`, **mismo** `machineName`; se actualizan `connectionState`, `lastSeenAt`, `clientVersion`; no se deriva nombre | Sin error |
| RENOMBRADO_LIBRE | `clientId` conocido + `machineName` nuevo y libre | `200 OK`, `renamed: false`, `machineName` = el nuevo nombre propuesto | Sin error |
| COLISION | dos `clientId` distintos, mismo `machineName` propuesto | `200 OK` en el segundo, `renamed: true`, `machineName` = `stem` + `-` + 32 hex del `clientId`; **ambos** clientes coexisten con nombres efectivos distintos; el `clientId` de ninguno cambia | La colisión **no** es error: no genera `ApiError` ni se cuenta como error en la métrica; se registra en el log con su `code` propio |
| COLISION_TRUNCADA | `machineName` de 64 puntos de código (p. ej. 64 ideogramas CJK) que colisiona | `machineName` efectivo de **exactamente 64** puntos de código = 31 del `stem` + 33 del sufijo; el recorte cae en límite de punto de código (no parte ningún ideograma ni emoji) | Sin error |
| NOMBRE_INVALIDO | `machineName` vacío tras recortar, > 64 puntos de código, con control, con espacios iniciales/finales/consecutivos, o con carácter fuera del set | `422 Unprocessable Content`, cuerpo `ApiError` con `code` `MACHINE_NAME_INVALID`, `message`, `cause` y `nextStep` obligatorios; el cliente **queda sin registrar** y la fila no se escribe | Error único con `code` propio, distinto del de «firma inválida» y del de «fingerprint de ancla no coincide»; contado en la métrica por su `code` |
| VERSION_INCOMPATIBLE | cabeceras `X-Protocol-Version` fuera de rango | `426 Upgrade Required` con `X-Server-Version` y `X-Minimum-Protocol-Version`, cuerpo `ApiError` con `code` `CLIENT_VERSION_INCOMPATIBLE` | La comprobación de versión ocurre **antes** de cualquier validación de nombre |
| LISTA | `GET /api/v1/clients` con ≥ 1 cliente registrado | `200 OK` con `ClientRegistrationView[]` (camelCase), orden **estable por nombre de equipo**; una fila por cliente | Sin error |
| LISTA_VACIA | `GET /api/v1/clients` sin clientes | `200 OK` con `[]` | Sin error |
| COLUMNA_NOMBRE | fila de un cliente que colisionó | La columna de nombre muestra el **nombre efectivo** (el derivado), nunca el propuesto | Sin error |

</intent-contract>

## Code Map

- `src/Shared/Contracts/ClientRegistrationView.cs:7` -- **Ya existe** el contrato de la vista con la
  forma exacta exigida. Se consume tal cual; no se toca.
- `src/Shared/Contracts/ConnectionState.cs:7` -- enumeración `AnchorVerified` / `AnchorUnverified` /
  `Disconnected`; es la que se persiste en `connectionState`.
- `src/Shared/Contracts/ErrorCatalog.cs:25` -- `MachineNameInvalidCode = "MACHINE_NAME_INVALID"` ya
  existe con sus tres partes. Falta **un** código propio para la colisión (p. ej.
  `MachineNameCollisionCode`); se añade aquí y nada más.
- `src/Shared/Contracts/StateDisplayMap.cs:59` -- `GetText(ConnectionState)` ya da el texto de la
  columna de estado. `GetText(string code)` (`:83`) sirve para el texto de un error.
- `src/Shared/Contracts/ApiError.cs:12` -- `record ApiError(string Code, string Message, string Cause,
  string NextStep)`; la forma única de error de la API.
- `src/Server/Domain/Clients/ProtocolCompatibility.cs:23` -- **molde a imitar**: clase estática con
  reglas puras y sin transporte. Aquí vive la nueva política de `machineName`.
- `src/Server/Domain/Catalog/CatalogDomainExceptions.cs:12` -- molde de las excepciones de dominio:
  base abstracta + concretas selladas con el valor infractor en una propiedad pública.
- `src/Server/Application/Ports/ICatalogSnapshotRepository.cs:20` -- molde de puerto: interfaz en
  `Application/Ports`, XML doc en inglés citando AD.
- `src/Server/Application/Ports/IDownloadIntentRepository.cs:56` -- molde del tipo «row» de un puerto:
  `public sealed record DownloadIntentRow(...)` declarado en el mismo fichero que la interfaz.
- `src/Server/Application/Catalog/GetCatalogSnapshot.cs:45` -- molde del caso de uso: `ExecuteAsync(
  CancellationToken)`, `ArgumentNullException.ThrowIfNull`, `ConfigureAwait(false)`, `ILogger<T>`.
- `src/Server/Application/ApplicationLayer.cs:7` -- marcador de capa; no hay fachada ni registro de DI.
- `src/Server/Infrastructure/Persistence/CatalogDbContext.cs:144` -- mapeo de `CLIENT_REGISTRATION`
  (clave `clientId`, `machineName`, `clientVersion`, `protocolVersion`, `connectionState` **texto**,
  `lastSeenAt` **ISO-8601 texto**). La unicidad por forma canónica la endurece esta historia.
- `src/Server/Infrastructure/Persistence/Entities/ClientRegistrationEntity.cs:11` -- entidad de la fila.
- `src/Server/Infrastructure/Persistence/DownloadIntentRepository.cs:105` -- **`EnsureClientRegistrationAsync`
  crea hoy una fila mínima con `MachineName = clientId`**: el registro nuevo debe **completar** esa
  fila (buscar por `clientId` antes de crear) y nunca duplicarla.
- `src/Server/Host/CatalogEndpoint.cs:33` -- molde de endpoint simple: clase estática, `Route`,
  `SerializerOptions = new(JsonSerializerDefaults.Web)` (camelCase), `Serialize<T>`, `Map(WebApplication)`.
- `src/Server/Host/GamesEndpoint.cs:318` -- molde de escritura de error: `metrics.RecordError(code)` primero,
  `logger.LogWarning` con el `code` y el estado, respuesta `application/json; charset=utf-8`, y el
  mensaje de la excepción **nunca** llega al cuerpo.
- `src/Server/Host/GameRequest.cs:28` -- molde de DTO de petición del servidor: clase con propiedades
  `{ get; set; }` y un método de proyección que lanza la excepción de dominio.
- `src/Server/Program.cs:296` -- el gate de versión (`ClientVersionMiddleware`) se aplica a todo
  `/api/v1` salvo `/api/v1/admin`, y **antes** que la correlación: el `426` ya sale antes de que el
  endpoint vea el nombre.
- `src/Server/Program.cs:322` -- punto donde se mapean los endpoints (`GamesEndpoint.Map(app)`, etc.).
- `src/Server/Program.cs:200` -- registro manual de DI de la capa Application e Infrastructure.
- `src/Server/Presentation/Catalog/CatalogWindow.cs:29` -- molde de ventana del servidor: sin XAML,
  navegación lateral con la entrada «Clientes» **ya presente pero muerta** (`:108`), `Grid` de 2 columnas,
  `ScrollViewer` + `StackPanel` de filas, `RefreshAsync` en `Opened` (`:186`), borde separador de 1 px
  (`:354-360`).
- `src/Server/Presentation/Catalog/CatalogViewModel.cs:116` -- molde del view model: lista + propiedad
  `Rows` que proyecta a un `record` declarado en el mismo fichero + `Count` + `Replace`.
- `src/Server/Presentation/Catalog/GameFormView.cs:38` -- **fuente de los tokens de tipografía del
  servidor**: `MonospaceFontFamily` (Cascadia Mono…) y `MetaFontFamily` (Segoe UI…), tamaños 14 y 12.
  El servidor **no** tiene `DesignTokens` propio (el del cliente es otro sistema y otro ensamblado).
- `src/Server/Presentation/ServerApp.cs:49` -- composición de la UI: crea el scope, resuelve los casos
  de uso y abre las ventanas secundarias como diálogo desde `window.Opened`.
- `src/Client/Domain/ClientIdentity.cs:33` -- `CanonicalId` = `Guid.ToString("N")`, 32 hex en minúsculas.
- `src/Client/Application/ClientBootstrap.cs:79` -- el cliente carga o emite su `clientId` y **no**
  persiste ningún `machineName`: hoy se deriva de `Environment.MachineName` en cada arranque.
- `src/Client/Application/Ports/IClientRequestHeaders.cs:41` -- `MachineName` ya existe en el puerto.
- `src/Client/Infrastructure/Networking/ClientRequestHeadersHandler.cs:47` -- cabecera
  `X-Client-Machine-Name` ya estampada en todas las peticiones del cliente.
- `src/Client/Host/ClientCorrelation.cs:130` -- `ClientCorrelationFactory` recibe `Environment.MachineName`
  y lo recorta; es el origen del nombre propuesto del cliente.
- `src/Client/Host/CatalogSyncComposition.cs:111` -- molde de construcción de `HttpClient` con
  `BaseAddress` + `ClientRequestHeadersHandler` (con `InnerHandler` asignado).
- `src/Client/Application/Connection/ServerAvailabilityMonitor.cs:117` -- el cliente detecta disponible/
  no disponible el servidor; es donde se puede enganchar el registro al conectar y la desconexión.
- `tests/IntegrationTests/TestServer.cs:101` -- servidor real con Kestrel; `Assert.Skip` si no hay
  interfaz privada; `CreateClient()` con `BaseAddress`.
- `tests/UnitTests/SharedContractInventoryTests.cs:15` -- **obligatorio**: `Shared` declara exactamente
  22 contratos; añadir un tipo público rompe el test.
- `tests/UnitTests/HubEventContractTests.cs:50` -- **obligatorio**: el hub no declara métodos
  servidor→cliente.
- `tests/UnitTests/ProjectDependencyTests.cs:233` -- **obligatorio**: la capa Application del servidor
  no puede mencionar «Infrastructure»/«Presentation» ni importar marcos de trabajo; el ensamblado del
  servidor no puede usar `System.Net.Http`.
- `tests/UnitTests/ErrorCatalogTests.cs:107` -- ya afirma que existe el código de rechazo por nombre.

## Tasks & Acceptance

**Execution:**

- `src/Server/Domain/Clients/MachineNamePolicy.cs` -- **NUEVO**. Clase estática con reglas puras y sin
  transporte, al estilo de `ProtocolCompatibility`: (a) `IsValid` que comprueba las seis reglas de
  forma de AD-30 midiendo en **puntos de código** (`System.Text.Rune`), sin controles, sin espacios
  iniciales/finales/consecutivos y con el set admitido; (b) `Canonicalize` que aplica NFKC → eliminación
  de marcas `Mn`/`Mc`/`Me` → minúsculas Unicode invariantes, **en ese orden**; (c) `Derive` que
  construye el nombre efectivo = `stem` recortado a ≤ 31 puntos de código + `"-"` + los 32 hex del
  `clientId` (`Guid.ToString("N")`), con recorte por puntos de código que **nunca parte** un punto de
  código y con `stem` de reserva `equipo` si quedara vacío. Sin búsqueda de nombres libres, sin
  contadores, sin E/S.
- `src/Server/Domain/Clients/ClientDomainExceptions.cs` -- **NUEVO**. Excepción de dominio para el
  nombre mal formado, con el nombre propuesto infractor en una propiedad pública, siguiendo el molde
  de `CatalogDomainExceptions`.
- `src/Server/Application/Ports/IClientRegistrationRepository.cs` -- **NUEVO**. Puerto con dos
  operaciones: `FindByClientIdAsync` y `FindByCanonicalMachineNameAsync` (para detectar la colisión),
  más `SaveAsync` para la alta o la actualización de la fila. Declara en el mismo fichero el
  `record ClientRegistrationRow(...)` con los datos que el caso de uso necesita. No menciona
  «Infrastructure» ni «Presentation».
- `src/Server/Application/Clients/RegisterClient.cs` -- **NUEVO**. Caso de uso del registro. Orden
  normativo: validar la forma del nombre → si la fila existe por `clientId`, **reconexión**: conservar
  nombre y fila, actualizar `connectionState`/`lastSeenAt`/`clientVersion`; si no existe, buscar por
  **forma canónica** del nombre propuesto → si hay otra fila con distinto `clientId`, **colisión**:
  derivar el nombre efectivo (AD-31), persistirlo y devolver `renamed: true`; si no hay colisión,
  adoptar el nombre propuesto y devolver `renamed: false`. El `clientId` **no cambia** nunca. Registra
  en el log estructurado el rechazo por nombre inválido y la colisión, cada uno con **su propio
  `code`** del `ErrorCatalog` y con `clientId` y `operationId` de correlación.
- `src/Server/Application/Clients/ListClients.cs` -- **NUEVO**. Caso de uso que devuelve
  `ClientRegistrationView[]` con la forma exacta de `Shared`, ordenados de forma **estable por nombre
  de equipo**.
- `src/Server/Infrastructure/Persistence/ClientRegistrationRepository.cs` -- **NUEVO**. Adaptador
  sobre `CatalogDbContext` con `AsNoTracking` en las lecturas y `SaveChangesAsync` en la escritura.
  Al **completar** una fila ya existente (creada en su forma mínima por `DownloadIntentRepository`)
  **no** la duplica: la busca por `clientId` y la actualiza.
- `src/Server/Infrastructure/Persistence/Migrations/<timestamp>_ClientRegistrationMachineNameUniqueness.cs`
  -- **NUEVO**. Migración versionada que endurece la unicidad de `machineName` **por forma canónica**
  en `CLIENT_REGISTRATION`. SQLite no normaliza, luego la columna canónica se materializa como una
  columna adicional con índice único, alimentada desde el caso de uso (el servidor es el único que
  escribe la fila, así que la puede mantener). Actualiza también
  `CatalogDbContextModelSnapshot.cs` y el `.Designer.cs` con el mismo patrón que las migraciones
  existentes.
- `src/Server/Host/ClientRegistrationRequest.cs` -- **NUEVO**. DTO de petición del servidor con la
  forma exacta `{ clientId, machineName, clientVersion, protocolVersion }` (sin `schemaVersion`), con
  propiedades `{ get; set; }` y un método de proyección al dominio que lanza la excepción de dominio
  del nombre inválido. Vive en el servidor, **no** en `Shared`.
- `src/Server/Host/ClientRegistrationResponse.cs` -- **NUEVO**. DTO de respuesta del servidor con la
  forma exacta `{ clientId, machineName, connectionState, lastSeenAt, renamed }`.
- `src/Server/Host/ClientsEndpoint.cs` -- **NUEVO**. Endpoint con las dos rutas:
  `GET /api/v1/clients` (devuelve `ClientRegistrationView[]` serializado con camelCase, `200 OK`) y
  `POST /api/v1/clients` (alta, reconexión y renombrado). En el `POST` el nombre se valida **después**
  de que el gate de versión ya haya actuado (el middleware corre antes del endpoint), y un nombre
  mal formado responde `422` con `ApiError` (`code` propio, `cause` y `nextStep` obligatorios); la
  colisión responde `200 OK` con `renamed: true`. Ninguna excepción alcanza el cuerpo de la respuesta;
  el mensaje de la excepción solo vive en el log. Llama a `metrics.RecordError(code)` solo en los
  rechazos: la colisión **no** es un error.
- `src/Server/Program.cs` -- **EDITAR**. Registrar en DI el puerto, sus dos casos de uso y el
  adaptador; y mapear `ClientsEndpoint.Map(app)` junto a los demás endpoints.
- `src/Shared/Contracts/ErrorCatalog.cs` -- **EDITAR**. Añadir **un** código nuevo y propio para la
  colisión de nombre (p. ej. `MachineNameCollisionCode`), con sus tres partes en español. **No** tocar
  `MachineNameInvalidCode`, que ya existe y es el del rechazo. No añadir ningún tipo a `Shared`.
- `src/Server/Presentation/Clients/ClientsViewModel.cs` -- **NUEVO**. View model de la sección, con el
  molde de `CatalogViewModel`: lista de clientes, propiedad `Rows` que proyecta a un
  `record ClientRow(...)` declarado en el mismo fichero, `Count` y `Replace`. La proyección ordena de
  forma **estable por nombre de equipo** y compone el texto del estado de conexión con
  `StateDisplayMap.GetText(ConnectionState)` (no se inventa microcopia).
- `src/Server/Presentation/Clients/ClientsWindow.cs` -- **NUEVO**. Ventana de la sección «Clientes»
  construida en código, sin XAML, al estilo de `CatalogWindow`. Tabla **densa**: encabezado de columnas
  + una fila por cliente de **40 px** de alto, con `Grid` de columnas de ancho fijo y borde separador
  inferior de 1 px; celdas de dato en tipografía `body-strong` (14 px) y unidades en `meta` (12 px),
  reutilizando `GameFormView.MetaFontFamily`/`MonospaceFontFamily`; identificador y versión en
  monoespaciada. Columnas: identificador, nombre del equipo, estado de conexión, juegos y versiones
  instalados, descargas activas, progreso de las instalaciones y errores registrados — las cuatro
  últimas **reservadas** con su encabezado y sin dato, porque las rellenan las historias 9.2 y 9.3.
  La fila es seleccionable y muestra su detalle. Refresco por REST al abrir y con un botón.
- `src/Server/Presentation/ServerApp.cs` -- **EDITAR**. Abrir la sección «Clientes» desde la
  navegación (la entrada «Clientes» ya existe en `CatalogWindow` como etiqueta muerta) y resolver el
  caso de uso `ListClients` desde el scope de DI.
- `src/Client/Application/Ports/IClientRegistrationClient.cs` -- **NUEVO**. Puerto del cliente para
  registrar el equipo ante el servidor.
- `src/Client/Application/Clients/RegisterWithServer.cs` -- **NUEVO**. Caso de uso del cliente que
  llama al registro con `{ clientId, machineName, clientVersion, protocolVersion }`, **adopta el
  nombre efectivo** devuelto por el servidor (para que el cliente vuelva a usarlo y no colisione
  consigo mismo) y **no** reintenta con el nombre propuesto si el servidor lo rechaza: el rechazo
  significa que el cliente queda sin registrar y no puede operar (AD-32).
- `src/Client/Infrastructure/Networking/ClientRegistrationHttpClient.cs` -- **NUEVO**. Adaptador HTTP
  del puerto, con `HttpClient` y `BaseAddress` al estilo de `CatalogSyncComposition`.
- `src/Client/Host/ClientRegistrationComposition.cs` -- **NUEVO**. Composición que registra el
  cliente ante el servidor al conectarse (usando `ServerAvailabilityMonitor` como señal de
  disponibilidad) y persiste el **nombre efectivo** en el estado local del cliente, de modo que el
  nombre propuesto en las reconexiones siguientes sea el efectivo y no colisione consigo mismo.
- `src/Client/Infrastructure/Persistence/Entities/ClientIdentityEntity.cs` -- **EDITAR**. Añadir la
  columna del **nombre efectivo** del equipo, para que sobreviva al reinicio.
- `src/Client/Infrastructure/Persistence/Migrations/<timestamp>_ClientEffectiveMachineName.cs` --
  -- **NUEVO**. Migración versionada que añade la columna, y su `.Designer.cs` y el
  `ClientStateDbContextModelSnapshot.cs` actualizados.
- `src/Client/Infrastructure/Persistence/ClientStateStore.cs` -- **EDITAR**. Leer y guardar el nombre
  efectivo junto a la identidad.
- `tests/UnitTests/MachineNamePolicyTests.cs` -- **NUEVO**. Cubre la matriz de nombres: los seis
  criterios de forma (vacío, > 64 puntos de código, control, espacios iniciales/finales/consecutivos,
  carácter fuera del set, longitud mínima), la forma canónica (NFKC, diacríticos, minúsculas, guion
  largo `U+2011`), y la derivación: sufijo de 33 puntos de código, `stem` recortado a ≤ 31, longitud
  total ≤ 64, `stem` de 64 ideogramas CJK recortado a 31, emoji **no partido**, `stem` de reserva
  `equipo`, y la garantía de que dos `clientId` distintos producen nombres derivados distintos.
- `tests/IntegrationTests/ClientRegistrationTests.cs` -- **NUEVO**. Nivel **integración** contra el
  servidor real en ejecución (`TestServer`): alta simple, reconexión, renombrado a nombre libre,
  renombrado a nombre que colisiona, colisión entre dos clientes distintos, nombre inválido → `422`
  con `ApiError` y `code` propio, versión incompatible → `426` **antes** de validar el nombre,
  `GET /api/v1/clients` con la forma exacta y el orden estable por nombre de equipo, y el log JSON
  con `code` distintos para el rechazo y para la colisión.
- `tests/IntegrationTests/ClientsTableTests.cs` -- **NUEVO**. Nivel **integración** de la tabla
  densa: se instancia la ventana en Avalonia headless y se comprueba que hay una fila por cliente,
  que el alto de la fila es de 40 px, que el orden es estable por nombre de equipo, que la columna
  de nombre muestra el **nombre efectivo** y que la fila es seleccionable.

**Acceptance Criteria:**

- Given la sección «Clientes» del servidor, when Luis la abre, then ve una **tabla densa**, no una
  colección de tarjetas, con una fila por cliente y **40 px** de alto, con las celdas de dato en
  `{typography.body-strong}` y las unidades en `{typography.meta}`, con el orden estable por nombre de
  equipo y con la fila seleccionable para ver su detalle *(FR-10, UX-DR-14)*.
- Given la tabla de clientes, when se listan las columnas, then aparecen identificador, nombre del
  equipo, estado de conexión, juegos y versiones instalados, descargas activas, progreso de las
  instalaciones y errores registrados *(FR-10, UX-DR-14)*.
- Given un cliente compatible, when se conecta y se desconecta, then el servidor refleja la conexión
  y la desconexión en la tabla *(FR-10)*.
- Given la vista de clientes, when se solicita, then se obtiene **exclusivamente** por REST con
  `GET /api/v1/clients`, que devuelve `ClientRegistrationView[]` con la forma exacta (`clientId`,
  `machineName`, `clientVersion`, `protocolVersion`, `connectionState`, `lastSeenAt`), y **no existe**
  el evento `ClientsSnapshotChanged` en el contrato *(FR-10, R-MVP-9, AR-3, AD-29)*.
- Given el servidor, when se inspecciona la persistencia, then `ClientRegistration` es propiedad del
  servidor y su **clave** es el `clientId`: el servidor **no** deduplica, **no** agrupa, **no** fusiona
  ni reasigna filas por `machineName` *(AR-13, AR-14, AD-23, AD-30)*.
- Given dos clientes con **distinto** `clientId` que proponen el **mismo** `machineName`, when ambos
  se registran, then el servidor **no rechaza** al segundo ni lo deja invisible: le asigna un
  `machineName` **único derivado** del propuesto —el `stem` recortado más el guion y los **32 dígitos
  hexadecimales** del `clientId`— y lo **persiste**, y ambos equipos **coexisten** en la tabla con
  **nombres efectivos distintos**, y el `clientId` de cada uno **no cambia** *(FR-10, AR-14, AD-30)*.
- Given un cliente cuyo `machineName` propuesto no cumple las reglas de forma, when se registra ante
  el servidor, then el registro se **rechaza** con un `ApiError` del `ErrorCatalog` cuyo `code` es
  **propio** y **distinto** del de «firma inválida» y del de «fingerprint de ancla no coincide», con
  `cause` y `nextStep` **obligatorios**, y el cliente **queda sin registrar y no puede operar**, y el
  error se presenta con `message`, `cause` y `nextStep` tal cual, sin texto inventado fuera de la tabla
  *(FR-10, AR-4, AR-14, AD-30)*.
- Given un `machineName` **no válido** o una **colisión** resuelta, when el servidor registra el
  hecho, then lo hace en el logging estructurado **JSON** con `clientId` y `operationId` de
  correlación, y con un `code` del `ErrorCatalog` **distinto** en cada caso *(AR-14, AR-20, AD-15,
  AD-25, AD-30)*.
- Given un cliente que cambia el nombre de su equipo, when se registra de nuevo, then propone el
  nuevo nombre y el servidor aplica la misma regla: si el nuevo nombre está libre lo adopta, y si
  colisiona le asigna el nombre único derivado y se lo notifica, y el `clientId` **no cambia** en
  ningún caso, y el servidor **no ofrece** en el MVP una operación de renombrado de otro cliente
  *(FR-10, AR-14, AD-30)*.
- Given la vista de clientes, when el servidor expone la columna de nombre del equipo, then muestra
  **siempre el nombre efectivo**, y el `machineName` **no** es una credencial: no hay contraseña,
  token, sesión ni rol asociados, no se usa para autenticar ninguna petición y no confiere permisos
  *(FR-10, AR-14, AD-6, AR-12, AD-30)*.
- Given un cliente cuyo `machineName` propuesto está en la longitud máxima admitida y **colisiona**,
  when el servidor deriva el nombre efectivo, then el nombre efectivo es `stem_truncado` + el sufijo
  fijo `"-"` seguido de los **32 dígitos hexadecimales en minúsculas** de `Guid.ToString("N")` del
  `clientId`, y la longitud del nombre efectivo **nunca excede 64 puntos de código** (`System.Text.Rune`),
  y el sufijo tiene **longitud fija de 33 puntos de código**, y la truncación del `stem` **cae siempre
  en un límite de punto de código** y el `stem` truncado **no puede quedar vacío** (si lo estuviera, se
  usa el `stem` de reserva `equipo`), y el nombre efectivo derivado **vuelve a ser admisible** y
  **nunca necesita volver a ser derivado** y su **unicidad está garantizada por construcción**, sin
  búsqueda de nombres libres *(FR-10, AR-14, AD-30, AD-31)*.
- Given un nombre propuesto que colisiona con el de otro `clientId` ya registrado, when el servidor
  deriva el nombre efectivo, then el nombre efectivo es una **función pura** de (`stem`, `clientId`):
  el servidor **no** busca nombres libres, **no** incrementa ningún contador, **no** prueba candidatos
  sucesivos y **no** puede agotar el espacio de nombres, y dos nombres efectivos derivados iguales en
  forma canónica **implican el mismo `clientId`**, y el `clientId` del cliente **no cambia**, no se
  fusiona ninguna fila y no se reasigna la fila de otro cliente *(FR-10, AR-14, AD-30, AD-31)*.
- Given un cliente que se registra, when llama al punto de entrada del registro del cliente, then
  envía un cuerpo con la forma exacta `{ clientId, machineName, clientVersion, protocolVersion }`,
  donde `clientId` es el UUID v7 emitido por el cliente y `clientVersion` y `protocolVersion` son los
  **mismos valores** que las cabeceras `X-Client-Version` y `X-Protocol-Version`, y el cuerpo **no**
  lleva `schemaVersion`, y recibe una respuesta con la forma exacta `{ clientId, machineName,
  connectionState, lastSeenAt, renamed }` *(FR-10, AR-14, AD-5, AD-6, AD-22, AD-23, AD-30, AD-32)*.
- Given un cliente que se registra ante el servidor, when el servidor responde al registro, then el
  **alta**, la **reconexión** y el **renombrado** ocurren en la **misma** operación de registro, que es
  el **único** punto de entrada del cliente al servidor, y el éxito responde **`200 OK`** —no
  `201 Created`—, con el nombre efectivo y el indicador `renamed`, y un nombre mal formado responde
  **`422 Unprocessable Content`** con la forma única `ApiError` y un `code` **propio**, y una versión
  incompatible responde **`426 Upgrade Required`** con la versión de servidor requerida y el rango
  mínimo soportado, y esa comprobación ocurre **antes** de cualquier validación de nombre, y la
  **colisión no es un error**, y una **reconexión** **conserva** la fila y el nombre *(FR-10, AR-14,
  AD-5, AD-6, AD-15, AD-22, AD-23, AD-25, AD-29, AD-30, AD-32)*.

## Spec Change Log

## Review Triage Log

## Design Notes

**Decisiones tomadas con la evidencia disponible (no hay preguntas abiertas):**

- **El registro es un endpoint del servidor, no un evento del hub.** AD-29 fija que la vista de
  clientes viaja por REST y que `ClientsSnapshotChanged` no existe; AD-32 fija que el registro ocurre
  en `POST /api/v1/clients`. Por eso la historia añade **un** endpoint con dos rutas, no un método de
  hub (`HubEventContractTests` seguiría viendo un hub sin métodos servidor→cliente).
- **El `426` sale antes que el `422` sin trabajo extra en el endpoint.** El gate de versión
  (`ClientVersionMiddleware`) ya se aplica a todo `/api/v1` salvo `/api/v1/admin` y **corre antes** del
  endpoint (`src/Server/Program.cs:296`). Por eso la validación del nombre, que vive en el caso de
  uso, solo se alcanza cuando la versión ya es compatible: el orden normativo de AD-32 se cumple por
  composición del pipeline y no por un `if` en el handler.
- **La fila mínima que crea `DownloadIntentRepository` se completa, no se duplica.** Ese adaptador crea
  una `CLIENT_REGISTRATION` mínima (`MachineName = clientId`) para que la clave foránea de
  `DOWNLOAD_INTENT` se cumpla. El caso de uso de registro **busca por `clientId` antes de crear**, así
  que una fila existente se completa con el nombre efectivo en lugar de crear una segunda.
- **La unicidad por forma canónica se materializa en una columna.** SQLite no normaliza, así que
  «único por forma canónica» no se puede expresar con un índice sobre `machineName`. La migración
  añade una columna canónica con índice único, y el servidor —único escritor de la fila— la mantiene.
  La comparación por forma canónica sigue siendo la regla; la columna solo la hace exigible por la
  base de datos.
- **Las columnas de 9.2 y 9.3 se reservan, no se inventan.** FR-10 pide que la tabla muestre juegos y
  versiones instalados, descargas activas, progreso de instalaciones y errores registrados, pero esos
  datos **no existen todavía** (los producen las historias 9.2 y 9.3, que dependen de esta). La tabla
  muestra los encabezados de las cuatro columnas para que la forma sea la final, y su contenido llega
  en las historias siguientes. Los datos que esta historia sí produce —identificador, nombre y estado
  de conexión— van rellenos.
- **El texto del estado de conexión sale de `StateDisplayMap`.** `StateDisplayMap.GetText(ConnectionState)`
  ya existe y da «Ancla verificada» / «Ancla sin verificar» / «Desconectado». La vista no escribe
  microcopia propia (AD-25).
- **La tipografía del servidor se reutiliza, no se inventa.** El servidor no tiene `DesignTokens` (el
  del cliente es otro ensamblado con otros tamaños: `meta` de 11 px allí, 12 px aquí). La tabla usa
  `GameFormView.MetaFontFamily`/`MetaFontSize` y `MonospaceFontFamily`/`MonospaceFontSize`, que son
  los tokens que el servidor ya usa en el resto de su interfaz.
- **La fila se hace seleccionable con el patrón de Avalonia más simple.** La selección y el detalle se
  resuelven con un control de fila que alterna su fondo al recibir el foco y un panel de detalle que
  muestra los datos de la fila seleccionada, sin inventar un sistema de navegación nuevo.

## Verification

**Commands:**

- `dotnet build LANPartyHub.sln` -- expected: SUCCESS, sin errores ni advertencias nuevas.
- `dotnet test LANPartyHub.sln` -- expected: SUCCESS, todos los tests en verde, incluidos los nuevos de
  `MachineNamePolicyTests`, `ClientRegistrationTests` y `ClientsTableTests`, y los tests de contrato
  existentes (`SharedContractInventoryTests`, `HubEventContractTests`, `ProjectDependencyTests`,
  `ErrorCatalogTests`) **sin modificar**.
- `powershell -NoProfile -File tools/check-encoding.ps1` -- expected: SUCCESS, sin CJK ni U+FFFD en los
  ficheros tocados.
- `powershell -NoProfile -File tools/check-epics.ps1` -- expected: SUCCESS, sin deriva de trazabilidad AR.
