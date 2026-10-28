---
title: 'Story 1.3 — Arranque del cliente con layout en disco y persistencia propia'
type: 'feature'
created: '2026-10-28'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: true
baseline_revision: '7e9f11e43f83a4c310c86b7ea6b7afc0ddae2475'
story_key: '1-3-arranque-del-cliente-con-layout-en-disco-y-persistencia-prop'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/epics/epics-lan-party-hub-2026-10-26/epics.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-lan-party-hub-2026-10-26/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-2-arranque-del-servidor-con-superficie-de-red-minima-y-datos-configurables.md'
warnings:
  - oversized
deferred: []
---

<intent-contract>

## Intent

**Problem:** El cliente es una ventana de Avalonia vacía: no crea su layout en disco, no persiste nada y no tiene identidad. Un jugador que reinicia la aplicación pierde cualquier estado de sus operaciones y no puede correlacionarse con el servidor.

**Approach:** Al arrancar, el cliente resuelve su configuración efectiva, crea su carpeta de datos con el layout que fija AD-9, aplica las migraciones de EF Core sobre `state.sqlite`, emite su `clientId` una sola vez y lo persiste, y reconcilia el progreso de descarga contra el archivo parcial rechazando todo registro cuyo motor de transferencia no exista en la versión activa.

## Boundaries & Constraints

**Always:**

- La carpeta de datos del cliente es `%LOCALAPPDATA%\LanPartyHub` y contiene exactamente `install/` (raíz de instalación configurable, **único** directorio confinado), `downloads/` (parciales, ruta fija, **fuera** de la raíz de instalación), `manifests/` (caché de manifiestos firmados) y `state.sqlite` (AD-9, AD-17).
- El `clientId` es un **UUID v7** que el cliente **emite una sola vez** y persiste en `CLIENT_IDENTITY`. No se regenera al reiniciar, al reinstalar el binario mientras el archivo de datos sobreviva, ni al cambiar la versión; si se borra la carpeta de datos, el equipo aparece como cliente nuevo (AD-23).
- El `clientId` viaja y se persiste en su **forma canónica**: los 32 dígitos hexadecimales en minúsculas de `Guid.ToString("N")`, sin guiones, que es la forma que usa AD-31 para derivar el nombre efectivo del equipo.
- El esquema de `state.sqlite` es **propiedad del cliente** y contiene sus cuatro tablas del ERD —`CLIENT_IDENTITY`, `CLIENT_INSTALLATION`, `DOWNLOAD_OPERATION`, `DOWNLOAD_PROGRESS`— con el nombre y el orden exactos de columna. **Ninguna tabla del servidor se replica** (AD-11, AD-22).
- Las migraciones de EF Core están versionadas y se aplican **automáticamente al arrancar** con `MigrateAsync()`, nunca `EnsureCreated()` (AD-11).
- El progreso persistido del MVP es el **offset acumulado** (`bytesPersisted`, `totalBytes`). **No hay `chunkMap`** ni columna que lo sugiera (AD-24, AD-28).
- El progreso efectivo de una operación es la **intersección** del offset persistido y lo que el archivo parcial contiene en disco; el offset corregido se persiste (AD-11).
- Un registro persistido cuyo `transferEngine` no exista en la versión activa **se rechaza en el arranque**, en lugar de reinterpretarse (AD-24).
- Configuración del cliente por `serverAddress`, `installDirectory` y `theme`, con precedencia `flags > env > archivo` (AD-12, AR-23).
- El artefacto del cliente es **self-contained single-file para Windows x64** (AD-17). No declara soporte para ninguna otra plataforma del MVP (NFR-4).
- El `machineName` es la **etiqueta humana** del equipo y su unicidad la valida el servidor; no es una credencial ni un control de acceso, y el `clientId` es la clave técnica (AD-23, AD-30).

**Never:**

- No crear ninguna tabla del servidor en `state.sqlite`, ni copiar el esquema de `catalog.sqlite`.
- No regenerar el `clientId` en ningún arranque, ni por un cambio de versión, ni por una reinstalación.
- No interpretar el progreso de un registro cuyo motor de transferencia no sea el activo, ni deducir el formato del progreso de un parámetro configurable: el formato se deduce **del** `transferEngine` (AD-24).
- No escribir nunca un archivo parcial fuera de `downloads/`, ni dentro de la raíz de instalación ni en `%TEMP%` (AD-9).
- No confinar ningún directorio salvo la raíz de instalación configurada.
- No introducir autenticación, credenciales, token, sesión ni rol (AD-6, AD-30).
- No referenciar `MonoTorrent` ni ninguna biblioteca P2P, ni usar vocabulario de peer, swarm o fragmento en el cliente (AD-28).
- No meter EF Core, SQLite ni sockets en `src/Client/Domain/` ni en `src/Client/Application/`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Primer arranque | máquina sin carpeta de datos | se crea `%LOCALAPPDATA%\LanPartyHub` con `install/`, `downloads/`, `manifests/` y `state.sqlite`; se emite y persiste un `clientId` nuevo | — |
| Segundo arranque | la misma carpeta de datos | el `clientId` es **el mismo** que el emitido la primera vez; no se regenera | — |
| Carpeta de datos borrada | no queda `state.sqlite` | se emite un `clientId` nuevo; el equipo aparece como cliente nuevo en el servidor | comportamiento correcto de un identificador persistente (AD-23) |
| Motor de transferencia desconocido | registro persistido con `transferEngine` distinto del activo | el registro **se rechaza**: no se interpreta como operación activa, la menolak se registra y el arranque continúa | el registro se conserva en la base de datos para que otra versión pueda leerlo |
| Offset mayor que el parcial | `bytesPersisted` mayor que el tamaño del `.part` | el progreso efectivo es el tamaño del archivo y el offset persistido se corrige | — |
| Offset menor que el parcial | `bytesPersisted` menor que el tamaño del `.part` | el progreso efectivo es el offset persistido; el archivo no se trunca | — |
| Directorio de instalación cambiado | cambio en Configuración | el valor se persiste y el directorio nuevo es el único confinado | — |
| Flag sobre la configuración | `--install-directory` apunta a otra ruta | gana el flag; la ruta persistida no cambia | — |
| Flag desconocido | `--tracker-port 6881` | el arranque se aborta | mensaje con el flag y la lista de admitidos |
| Carpeta de datos no escribible | permisos denegados | el arranque se aborta | mensaje accionable, sin traza de pila |

</intent-contract>

## Code Map

- `src/Client/Program.cs` -- arranque mínimo con Avalonia (`BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`); hay que anteponer la composición: configuración, layout, migraciones, identidad y reconciliación.
- `src/Client/LANPartyHub.Client.csproj` -- `Microsoft.NET.Sdk`, `net10.0`, `WinExe`, Avalonia `12.1.4`, referencia a `Shared`; hay que añadir EF Core `10.0.13` (tres paquetes) y el perfil de publicación.
- `src/Client/{Domain,Application,Infrastructure,Presentation}/` -- solo tipos marcadores, salvo `Domain/StateMachines/` (las dos máquinas de estado ya existen y no se tocan).
- `src/Server/Host/ServerCommandLine.cs` y `src/Server/Host/ServerConfiguration.cs` -- **precedente a copiar en forma, no en código**: parser estricto que aborta con mensaje ante flag desconocido, valor ausente o puerto inválido, y composición de la configuración con la precedencia `flags > env > archivo`. `Shared` es de contratos solos (AD-18) y la espina fija tres proyectos, así que el cliente reimplementa su fontanería: lo que se comparte es el patrón, no las clases.
- `src/Server/Infrastructure/Persistence/` -- mismo patrón a replicar en el cliente: `DbContext` con mapeo explícito de tabla y columna, fábrica de design-time, migración inicial generada con `dotnet-ef` y `MigrateAsync()` antes de arrancar el host.
- `tests/IntegrationTests/ServerStartupTests.cs` -- precedente de la prueba de integración con host real: carpeta temporal, omisiones con motivo explícito cuando el entorno no permite la prueba, y disposable del host si el arranque falla.
- `tests/UnitTests/ProjectDependencyTests.cs` -- guardas que el cliente no puede romper: sin paquetes P2P, `Domain` y `Application` sin EF Core/SQLite/HTTP, `Infrastructure` sin `Presentation`, todos los proyectos en `net10.0`.
- `tests/UnitTests/SharedContractInventoryTests.cs` -- `Shared` sigue con exactamente 20 contratos de primer nivel y sin duplicación en el cliente: el cliente no puede declarar un tipo con esos nombres.
- `_bmad-output/planning-artifacts/epics/.../epics.md` líneas 1025-1092 -- Story 1.3 completa: 5 AC, casos de error, nivel de prueba **Integración** y DoD.
- `_bmad-output/planning-artifacts/architecture/.../ARCHITECTURE-SPINE.md` -- AD-9 (layout y confinado), AD-11 (SQLite, offset y reconciliación), AD-17 (artefacto y carpeta), AD-22 (dueño de cada entidad), AD-23 (`clientId`), AD-24 (`transferEngine`, «un motor, un formato»), AD-30/AD-31 (el nombre es etiqueta, no credencial), el ERD del cliente y las convenciones de configuración.
- `docs/estrategia-pruebas.md` §2.1 -- las filas del cliente SQLite y del layout están pendientes de esta historia.

## Tasks & Acceptance

**Execution:**

- [x] `src/Client/LANPartyHub.Client.csproj` -- añadir `Microsoft.EntityFrameworkCore` `10.0.13`, `Microsoft.EntityFrameworkCore.Sqlite` `10.0.13` y `Microsoft.EntityFrameworkCore.Design` `10.0.13` (`PrivateAssets=all`) -- la versión de EF Core y la de su proveedor coinciden, y coincide con la del servidor
- [x] `src/Client/Host/ClientCommandLine.cs` -- parser estricto de `--server-address`, `--install-directory` y `--theme`: flag desconocido, valor ausente, valor vacío y puerto —no aplica— inválido abortan con mensaje -- AC2 y la precedencia
- [x] `src/Client/Host/ClientConfiguration.cs` -- configuración efectiva con las claves `serverAddress`, `installDirectory` y `theme`; proveedores en el orden `archivo` → `env` → `flags`; fichero `appsettings.json` junto al binario; valores por defecto `installDirectory` = `<carpeta de datos>\install` y `theme` = `System` -- AC2, AC4
- [x] `src/Client/Infrastructure/Persistence/ClientDataLayout.cs` -- resolver `%LOCALAPPDATA%\LanPartyHub`, crear `install/`, `downloads/` y `manifests/`, y devolver las rutas de `state.sqlite` y de los parciales (`<downloads>\<packageId>.part`); la carpeta de datos debe poder inyectarse para la prueba -- AC1
- [x] `src/Client/Infrastructure/Persistence/ClientStateDbContext.cs` -- el `DbContext` del cliente con mapeo explícito de sus cuatro tablas del ERD y el nombre y el orden exactos de columna: `CLIENT_IDENTITY` (`clientId`), `CLIENT_INSTALLATION` (`gameId`, `installedVersion`, `validationState`, `installPath`, `installDirectoryId`), `DOWNLOAD_OPERATION` (`operationId`, `packageId`, `state`, `transferEngine`) y `DOWNLOAD_PROGRESS` (`operationId`, `bytesPersisted`, `totalBytes`) -- AC3
- [x] `src/Client/Infrastructure/Persistence/ClientStateDbContextFactory.cs` -- `IDesignTimeDbContextFactory` con el mismo proveedor y el mismo ensamblado de migraciones que el runtime -- la herramienta `dotnet-ef` salta el host y necesita la fábrica
- [x] `src/Client/Infrastructure/Persistence/Migrations/` -- migración inicial generada con `dotnet-ef` `10.0.13` (`dotnet tool install --global dotnet-ef --version 10.0.13`), más la instantánea de modelo -- AC3
- [x] `src/Client/Domain/ClientIdentity.cs` -- emisión del `clientId` como UUID v7 (con `Guid.CreateVersion7()` si el runtime la expone; si no, con los 48 bits de marca de tiempo Unix en milisegundos, la versión 7, la variante y el resto aleatorio) y su forma canónica en minúsculas sin guiones -- AC2
- [x] `src/Client/Application/ClientBootstrap.cs` -- secuencia de arranque: resolver la configuración, crear el layout, aplicar las migraciones, cargar o emitir la identidad, reconciliar el progreso y rechazar los registros de motor desconocido -- AC1, AC2, AC3
- [x] `src/Client/Infrastructure/Persistence/DownloadProgressReconciler.cs` -- reconciliación: el progreso efectivo es el mínimo entre el offset persistido y el tamaño del archivo parcial, y el offset corregido se persiste; además, el rechazo del registro cuyo `transferEngine` no sea el activo, que se registra y no se borra -- AC3
- [x] `src/Client/Program.cs` -- componer el arranque con la configuración efectiva y el bootstrap, propagar el error de arranque con mensaje accionable y código de salida 1, y dejar la ventana para las historias siguientes -- AC1, AC5
- [x] `src/Client/Properties/PublishProfiles/win-x64.pubxml` -- perfil de publicación self-contained single-file para `win-x64`, el único perfil del artefacto -- AC5, AD-17
- [x] `tests/UnitTests/ClientIdentityTests.cs` -- el `clientId` emitido es un UUID v7 válido (versión 7 y variante RFC 4122), su forma canónica son 32 hexadecimales en minúsculas, y dos emisiones son distintas -- AC2
- [x] `tests/UnitTests/ClientCommandLineTests.cs` -- flag válido, flag desconocido, valor ausente, valor vacío y la precedencia `flags > env > archivo` de las tres claves -- AC2, AC4
- [x] `tests/IntegrationTests/ClientStartupTests.cs` -- arranque real del cliente contra una carpeta de datos temporal: se crean las cuatro entradas del layout, `state.sqlite` tiene las cuatro tablas del cliente con su nombre y el orden exactos de columna, `__EFMigrationsHistory` registra la migración, el segundo arranque conserva el mismo `clientId`, un registro con `transferEngine` desconocido se rechaza sin reinterpretarse y el arranque continúa, y el offset persistido se corrige cuando el parcial es más corto -- AC1, AC2, AC3, AC4 y nivel **Integración** de la historia
- [x] `docs/estrategia-pruebas.md` -- actualizar §2.1 (las filas del cliente SQLite y del layout en disco) y §5 con el contenido declarado de la 1.3 -- el nivel de integración ya no está pendiente en esas filas
- [x] `.gitignore` -- añadir la negación `!src/Client/Properties/PublishProfiles/*.pubxml` y versionar `win-x64.pubxml` -- sin la negación, la regla `*.pubxml` excluía el perfil y AC5 no declaraba ninguna plataforma en la rama
- [x] `src/Client/client.appsettings.json` -- renombrar el fichero de configuración del cliente -- `appsettings.json` colisionaba con el del servidor en la carpeta de salida compartida de los proyectos de prueba
- [x] `src/Client/Host/ClientConfiguration.cs` -- corregir el orden en que `DescribeSource` recorre los proveedores y validar `serverAddress` como `host:puerto` -- el mensaje nombraba el origen equivocado y el formato nunca estuvo especificado
- [x] `src/Client/Host/ClientConfiguration.cs` -- traducir un fichero de configuración malformado a un error accionable -- el arranque abortaba con el mensaje crudo del framework
- [x] `src/Client/Infrastructure/Configuration/ClientSettingsStore.cs` y `src/Client/Presentation/MainWindow.cs` -- persistir el cambio del directorio de instalación y ofrecer la superficie mínima de Configuración -- AC4 exige que el cambio **se persista**
- [x] `src/Client/Presentation/StartupErrorWindow.cs` -- mostrar el fallo de arranque en una ventana -- el cliente es `WinExe` y no tiene consola, así que el mensaje no llegaba al jugador
- [x] `src/Client/Infrastructure/Persistence/ClientDataLayout.cs` -- crear siempre `install/` en la carpeta de datos y nombrar la ruta que falló -- la carpeta debe contener las cuatro entradas de la AC1 y el mensaje decía siempre la carpeta de datos
- [x] `src/Client/Infrastructure/Persistence/DownloadProgressReconciler.cs` -- acotar el offset a `0..partialLength` y no pisar un total desconocido -- un offset negativo sobrevivía y el total se sobrescribía con cero
- [x] `tests/UnitTests/ProjectDependencyTests.cs` -- completar la guarda de la capa `Application` con listas explícitas de permitidos y prohibidos -- la guarda omitía el registro que la propia capa usa
- [x] `tests/IntegrationTests/ClientStartupTests.cs` -- cubrir la raíz de instalación configurada, el `packageId` hostil y el parcial ausente -- las tres ramas del comportamiento no estaban ejercitadas
- [x] `src/Client/` y `tests/` -- retirar el código muerto, el `using` obsoleto y las carpetas temporales que las pruebas filtraban -- limpieza
- [x] `README.md` -- cómo arrancar el cliente, qué contiene su carpeta de datos, cómo se cambia el directorio de instalación y cómo se publica el artefacto `win-x64` -- verificación reproducible

**Acceptance Criteria:**

- Given un cliente nuevo, when arranca por primera vez, then crea `%LOCALAPPDATA%\LanPartyHub` con `install/` (raíz de instalación configurable), `downloads/` (descargas parciales), `manifests/` (caché de manifiestos) y `state.sqlite`
- Given el cliente ya arrancado antes, when arranca de nuevo, then **no** regenera su `clientId`: el UUID v7 emitido la primera vez se persiste en `ClientIdentity` y sobrevive a reinicios y a reinstalaciones del binario mientras el archivo de datos sobreviva, and el `clientId` es la **clave técnica** que deduplica la fila del servidor y correlaciona los logs, y **no** cambia ante un rechazo por nombre, ante una colisión resuelta ni ante un cambio de nombre del equipo, and el servidor **no** deduplica ni agrupa clientes por `machineName`
- Given el cliente con operaciones persistidas, when arranca, then aplica las migraciones de EF Core al inicio, and el esquema de `state.sqlite` es propiedad del cliente y **no** replica ninguna tabla del servidor, and un registro persistido cuyo `transferEngine` no exista en la versión activa se rechaza en el arranque, en lugar de reinterpretarse
- Given el cliente con un directorio de instalación configurado, when se cambia el directorio en Configuración, then el cambio se persiste con precedencia `flags > env > archivo`, and el directorio nuevo es el único confinado por la regla de confines de extracción
- Given el cliente publicado para Windows x64, when se arranca en un equipo con Windows 10 o Windows 11, then el cliente arranca y opera con normalidad en esa plataforma, and el artefacto no declara soporte para ninguna otra plataforma del MVP

## Spec Change Log

## Review Triage Log

### 2026-10-28 — Review pass
- verdicts: 51 findings — high 4, medium 17, low 17, false 13, maybe-false 0
- findings:

**Capa blind-hunter (19)**

- `[high]` `[patch]` el perfil `win-x64.pubxml` **no estaba versionado**: `.gitignore` ignora `*.pubxml`, de modo que AC5 no declaraba ninguna plataforma en la rama y el comando de publicación documentado fallaba en un clon limpio — negación en `.gitignore` y perfil versionado.
- `[medium]` `[patch]` AC4 exige que el cambio del directorio de instalación **se persista**, y nada escribía nunca la configuración: falta la superficie de Configuración y el almacén que la persiste.
- `[low]` `[patch]` `ClientBootstrapOptions.InstallDirectory` era una propiedad `required` que el bootstrap nunca leía — eliminada; el layout es la única fuente de la raíz confinada.
- `[false]` `[reject]` los errores del cliente se construyen fuera del `ErrorCatalog` — el catálogo es de **errores de API** (AD-15) y un fallo de arranque no es una respuesta de API; el servidor se comporta igual desde la Story 1.2.
- `[false]` `[reject]` `Program.Main` descarta el `ClientStartupResult` y el tema nunca llega a la presentación — aplicar el tema a la UI es de la épica 11 y así lo dice la nota de diseño.
- `[low]` `[patch]` `docs/estrategia-pruebas.md` §2.1 no tenía las filas del nivel unitario del cliente — añadidas.
- `[medium]` `[patch]` la guarda nueva de la capa `Application` omitía `Microsoft.Extensions.Logging` (que la propia capa usa), `System.Net.Sockets` y `System.Data.Common` — lista explícita de permitidos y de prohibidos, con una prueba que impide que ambas se separen.
- `[low]` `[patch]` el cliente usaba el vocabulario que su propio spec prohíbe («map of fragments») en dos comentarios — reformulados.
- `[low]` `[reject]` nada ejercita el caso de uso nuevo de `Application` con dobles de prueba — los puertos existen para que la capa sea comprobable más adelante; el nivel declarado de la historia es integración con disco y base reales.
- `[low]` `[defer]` el ayudante de confinado no implementa la regla de ruta canónica de AD-9 — la regla de confinado es de la Story 7.2; el guardián del parcial sí queda probado.
- `[medium]` `[patch]` `DescribeSource` nombraba el primer proveedor con la clave cuando el valor efectivo viene del último — se recorre en orden inverso, con prueba de dos fuentes.
- `[low]` `[patch]` el mensaje de «no escribible» nombraba siempre la carpeta de datos — ahora nombra la ruta que falló.
- `[low]` `[patch]` el «exactamente» del clause congelado chocaba con el comportamiento cuando la raíz sale de la carpeta de datos — el código se ajusta al clause: `install/` existe siempre en la carpeta de datos.
- `[low]` `[patch]` la sección «Estructura» del README quedó a medias — actualizada con el arranque del cliente y la regla nueva de `Application`.
- `[low]` `[patch]` la lista de ejecución del spec seguía sin marcar — marcada, con las tareas que la revisión añadió.
- `[medium]` `[patch]` `--server-address` aceptaba cualquier cadena no vacía y su formato nunca se especificó — se define y valida `host:puerto`, coherente con `ServerEndpoint`, y se documenta.
- `[medium]` `[patch]` un offset persistido negativo sobrevivía a la reconciliación y `totalBytes` se sobrescribía con cero — se acota el offset a `0..partialLength` y el total desconocido no se pisa.
- `[low]` `[patch]` código muerto y obsoleto — `DownloadProgressReconciliation.Empty`, un comentario que describía un respaldo inexistente y un `using` sin uso.
- `[low]` `[patch]` cada prueba dejaba sus carpetas temporales en `%TEMP%` — ahora se borran.

**Capa edge-case-hunter (19)**

- `[medium]` `[patch]` un fallo de arranque sin consola (el cliente es `WinExe`) salía con código 1 y sin que el jugador supiera por qué — ahora se muestra en una ventana mínima.
- `[low]` `[reject]` dos procesos del cliente sobre la misma carpeta podrían persistir dos identidades — la carga ya rechaza una identidad duplicada con mensaje explícito y el disparador es un error de uso.
- `[false]` `[reject]` la fila de identidad duplicada se detectaba en silencio — el código la rechaza con «Identidad duplicada».
- `[medium]` `[patch]` `DescribeSource` señalaba el origen equivocado — dup de blind hunter.
- `[medium]` `[patch]` un `client.appsettings.json` malformado abortaba con el mensaje crudo del framework — ahora dice que el fichero no es válido, lo nombra y dice qué hacer.
- `[false]` `[reject]` el parcial borrado entre `Exists` y `Length` — el reconciliador lo captura y salta la operación.
- `[medium]` `[patch]` el offset negativo — dup de blind hunter.
- `[false]` `[reject]` un fallo al guardar el offset corregido abortaba el arranque — se captura, se registra y se continúa.
- `[false]` `[reject]` `LOCALAPPDATA` vacío — el layout lo comprueba y aborta con mensaje.
- `[false]` `[reject]` un tema desconocido — la configuración lo valida y aborta.
- `[medium]` `[patch]` los dos `appsettings.json` (servidor y cliente) colisionaban en la carpeta de salida de las pruebas — el del cliente pasa a `client.appsettings.json`.
- `[false]` `[reject]` el directorio de instalación apuntando a la carpeta de descargas — la configuración lo rechaza.
- `[false]` `[reject]` el llamante pasando un directorio distinto del del layout — la comprobación existe; además la propiedad se eliminó.
- `[false]` `[reject]` una raíz de instalación relativa — el layout la resuelve contra la carpeta de datos.
- `[false]` `[reject]` una opción repetida — el parser la rechaza.
- `[false]` `[reject]` un cambio de estado TCP entre instantáneas — la descripción de la conexión no incluye el estado.
- `[low]` `[patch]` el `using System.Net.NetworkInformation;` sin uso en las pruebas del servidor — retirado.
- `[high]` `[patch]` el perfil de publicación no estaba en la revisión — dup de blind hunter.
- `[high]` `[patch]` el comando de publicación documentado fallaba en un clon limpio — dup de blind hunter.

**Capa verification-gap (5, pre-verificados)**

- `[medium]` `[patch]` la raíz de instalación configurada **nunca llegaba al disco** en ninguna prueba: una regresión que usara siempre el `install/` por defecto pasaba en verde — prueba de integración nueva con una raíz fuera de la carpeta de datos.
- `[medium]` `[patch]` el guardián del `packageId` hostil y su rama de captura no tenían prueba — integración y unitarias nuevas.
- `[medium]` `[patch]` `DescribeSource` — dup.
- `[medium]` `[patch]` la colisión de `appsettings.json` — dup.
- `[low]` `[patch]` `ClientBootstrapOptions.InstallDirectory` — dup.

**Capa intent-alignment (8 divergencias)**

- `[low]` `[defer]` AC1 se ejerce con un arranque en proceso sobre una carpeta temporal, no con el proceso real sobre `%LOCALAPPDATA%` — la carpeta es inyectable por diseño y el proceso publicado es la superficie E2E de la Story 13.1.
- `[low]` `[patch]` la tensión de `install/` (la AC1 lo nombra y a la vez lo hace configurable) — el código se ajusta a la AC: `install/` existe siempre.
- `[false]` `[reject]` las mitades de AC2 sobre la fila del servidor y la correlación de logs no tienen camino en el cliente — son de la Story 4.x y de la 1.4; el cliente solo puede garantizar la persistencia.
- `[medium]` `[patch]` AC3: el caso del parcial ausente no estaba afirmado y el total se sobrescribía con cero — añadidos.
- `[medium]` `[patch]` AC4: la cláusula de la superficie de Configuración y la del confinado no estaban — la superficie mínima y la persistencia, implementadas; la regla de confinado sigue siendo de la 7.2.
- `[high]` `[patch]` AC5: la declaración del artefacto no estaba en la rama — dup de blind hunter; el artefacto publicado nunca se lanza, eso es de la 13.1.
- `[low]` `[patch]` el diff lleva trabajo fuera de las AC (el parser estricto, la guarda de `Application`, la reescritura de las pruebas de sockets del servidor) — documentado en el PR.
- `[low]` `[defer]` la cláusula de «Pruebas necesarias» pide el cliente en ejecución y la base real: la base real es exacta, el proceso es de la 13.1.


## Design Notes

- **El `clientId` se persiste y se viaja en la forma canónica de AD-31.** Los 32 hexadecimales en minúsculas de `Guid.ToString("N")` son la forma que AD-31 usa para derivar el nombre efectivo del equipo, de modo que el identificador tiene una sola escritura en todo el sistema y no dos que puedan divergir.
- **El motor de transferencia se persiste como `HttpRange`.** AD-24 exige que el formato del progreso se deduzca **del** `transferEngine`, y el único motor del MVP es el camino HTTP con `Range` (AD-4). El valor persistido nombra la familia del motor, no el nombre de la clase de implementación, para que un refactor del tipo no invalide los datos ya guardados.
- **Rechazar un registro de motor desconocido no aborta el arranque.** El sujeto de la regla es el registro, no el proceso: se rechaza, no se interpreta como operación activa, se registra y el cliente sigue operando. El registro no se borra, para que otra versión pueda leerlo. Abortar el arranque por un registro raro dejaría al jugador sin cliente.
- **El archivo parcial se nombra por `packageId`.** El offset son «bytes válidos contiguos desde el inicio del archivo parcial» del **paquete**, y una operación nueva sobre el mismo paquete reanuda el mismo archivo; la Story 5.x hereda esta regla.
- **El layout del cliente es de AD-9 y el ERD, y no se replica ninguna tabla del servidor.** La comprobación de que `state.sqlite` no contiene ninguna tabla del servidor es explícita en la prueba de integración: las cuatro tablas del cliente se listan y se comparan con las del ERD del cliente.
- **La fontanería de configuración y de EF Core se duplica entre cliente y servidor a propósito.** `Shared` es de contratos solos (AD-18) y la espina fija tres proyectos, así que no hay dónde compartirla. Se replica el **patrón** —parser estricto, proveedores en orden, `SqliteConnectionStringBuilder`, `DbContext` con mapeo explícito, fábrica de design-time, `MigrateAsync()`— y cada lado tiene sus propias claves y su propio esquema. No es una regla de negocio duplicada, que es lo que AD-1 prohíbe.
- **El `installDirectoryId` se crea como columna pero no se puebla en esta historia.** Ninguna instalación existe todavía, así que el mecanismo que deriva el identificador opaco de la raíz elegida corresponde a la Story 7.x, la primera que registra una instalación. La raíz confinada vigente sí queda disponible: es el `installDirectory` efectivo de la configuración.
- **La carpeta de datos se puede inyectar.** `%LOCALAPPDATA%\LanPartyHub` es el valor por defecto (AD-17), pero la prueba de integración necesita una carpeta temporal: el bootstrap del cliente recibe la raíz como parámetro, igual que el servidor expone `Program.BuildServerAsync` para arrancar el host real en pruebas.
- **El artefacto declara una sola plataforma con un perfil de publicación.** `win-x64.pubxml` self-contained single-file es la declaración del artefacto (AD-17); el proyecto sigue siendo `net10.0` para que `dotnet build` y `dotnet test` no dependan del RID. El perfil necesita una negación en `.gitignore`, porque la regla `*.pubxml` lo excluye.
- **Las capas son carpetas, no proyectos.** La espina define tres proyectos en `src/`; `Infrastructure → Application → Domain` se materializa como árbol de carpetas dentro de cada desplegable, con un tipo marcador por capa para que la dirección de las dependencias sea comprobable.
- **`install/` existe siempre en la carpeta de datos; la raíz efectiva es la que confina.** La AC1 nombra las cuatro entradas de la carpeta y a la vez llama a `install/` la raíz configurable: la carpeta tiene siempre `install/`, `downloads/`, `manifests/` y `state.sqlite`, y cuando el jugador configura otra raíz, esa otra es la única confinada y `install/` queda sin uso. Un directorio de instalación **nunca** puede ser la carpeta de datos ni su carpeta padre, porque los parciales viven en `downloads/`.
- **El `serverAddress` es `host:puerto`.** `ServerEndpoint` de `Shared` lleva dirección y puerto por separado, y el cliente solo tiene una clave de configuración para el servidor: el formato tiene que traer las dos cosas, y se valida al arrancar.
- **El fallo de arranque se muestra en una ventana.** El cliente es `WinExe` y no tiene consola: un fallo antes de la ventana debe llegar al jugador de otra forma, o no llega. La ventana mínima de error es el mecanismo, y el registro en disco es el complemento.

## Verification

**Commands:**

- `dotnet build LANPartyHub.sln` -- expected: compilación sin errores ni advertencias
- `dotnet test LANPartyHub.sln` -- expected: los tres proyectos de prueba se ejecutan y pasan
- `dotnet ef migrations has-pending-model-changes --project src\Client\LANPartyHub.Client.csproj --startup-project src\Client\LANPartyHub.Client.csproj` -- expected: sin deriva entre el modelo y las migraciones del cliente
- `powershell -NoProfile -File tools/check-encoding.ps1` -- expected: PASS, sin CJK ni U+FFFD
- `powershell -NoProfile -File tools/check-epics.ps1` -- expected: PASS, sin cambios de conteo
- `git grep -in "monotorrent\|bittorrent\|swarm\|tracker" -- "*.csproj" "*.cs"` -- expected: sin coincidencias de biblioteca; las apariciones en guardas y pruebas que prohíben esas palabras son esperadas

## Auto Run Result

- **Resumen.** El cliente arranca con su configuración efectiva, crea `%LOCALAPPDATA%\LanPartyHub` con `install/`, `downloads/`, `manifests/` y `state.sqlite`, aplica las migraciones de EF Core sobre `state.sqlite`, emite su `clientId` una sola vez y lo persiste, reconcilia el progreso de descarga contra el archivo parcial y rechaza los registros cuyo motor de transferencia no exista en la versión activa. El directorio de instalación se cambia desde la ventana de Configuración y el cambio se persiste.
- **Ficheros.** `src/Client/LANPartyHub.Client.csproj` (paquetes de EF Core y perfil de publicación); `src/Client/Program.cs` (composición del arranque); `src/Client/App.cs` y `src/Client/Presentation/{MainWindow,StartupErrorWindow}.cs` (superficie mínima de Configuración y ventana de error); `src/Client/Host/{ClientCommandLine,ClientConfiguration,ClientStartupException}.cs`; `src/Client/Domain/{ClientIdentity,TransferEngines}.cs`; `src/Client/Application/{ClientBootstrap,ClientSettings,…}.cs` y `Ports/`; `src/Client/Infrastructure/Persistence/{ClientDataLayout,ClientStateDbContext,ClientStateDbContextFactory,ClientStateStore,DownloadProgressReconciler,Entities/,Migrations/}`; `src/Client/Infrastructure/Configuration/ClientSettingsStore.cs`; `src/Client/client.appsettings.json`; `src/Client/Properties/PublishProfiles/win-x64.pubxml`; `tests/UnitTests/{ClientIdentityTests,ClientCommandLineTests,ClientDataLayoutTests}.cs`; `tests/IntegrationTests/{ClientStartupTests,ProcessUdpSockets}.cs`; `tests/UnitTests/ProjectDependencyTests.cs`; `README.md`; `docs/estrategia-pruebas.md`; `sprint-status.yaml`.
- **Revisión.** 51 hallazgos en cuatro capas (blind hunter, edge case hunter, verification gap, intent alignment): 4 high, 17 medium, 17 low, 13 false. Aplicados 17 grupos de parche; 2 diferidos (la ruta canónica de AD-9 es de la Story 7.2 y el proceso publicado es de la 13.1); 5 refutados con su razón en el triage log. El hallazgo `high` de raíz: el perfil de publicación no estaba versionado porque `.gitignore` ignora `*.pubxml`, de modo que AC5 no declaraba ninguna plataforma en la rama.
- **Verificación.** `dotnet build` 0 errores y 0 advertencias; `dotnet test` 200 unitarias + 45 de integración + 3 E2E superadas, 1 E2E omitida con motivo; sin deriva de modelo de EF Core en el cliente; `check-encoding.ps1` y `check-epics.ps1` en PASS; `dotnet publish -p:PublishProfile=win-x64` produce un único ejecutable self-contained.
- **Riesgos residuales.** El escenario E2E sobre una LAN real y el artefacto publicado en ejecución siguen siendo de la Story 13.1; el `installDirectoryId` existe como columna sin poblarse hasta la Story 7.x; la ruta canónica de AD-9 (resolución de enlaces simbólicos) la implementará la Story 7.2.
- **Revisión de seguimiento recomendada:** `true`. Se parchearon más de dos entradas `medium` y una `high`, así que el criterio automático de la primera pasada pide otra. **Riesgo no verificado que nombra:** el artefacto publicado nunca se lanza en una prueba —la publicación se ejecuta y produce el ejecutable, pero arrancarlo como proceso en un equipo con Windows 10/11 es la superficie E2E de la Story 13.1—, y el arranque del cliente se ejerce en proceso contra una carpeta inyectada, nunca contra `%LOCALAPPDATA%` real.
