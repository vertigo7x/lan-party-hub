---
title: 'Story 1.2 — Arranque del servidor con superficie de red mínima y datos configurables'
type: 'feature'
created: '2026-10-27'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: true
baseline_revision: 'bc42ef7a15049fdd3dcf6be9a7ffb45fa3c4d8bc'
story_key: '1-2-arranque-del-servidor-con-superficie-de-red-minima-y-datos-configurables'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/epics/epics-lan-party-hub-2026-10-26/epics.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-lan-party-hub-2026-10-26/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-1-estructura-de-solucion-y-contratos-compartidos.md'
warnings:
  - oversized
deferred:
  - summary: >-
      El ERD del servidor no declara la columna `canonicalManifest` de `INSTALLATION_MANIFEST`,
      aunque AD-22 dice que su contenido es «Manifiesto canónico y firma».
    evidence: |-
      La espina (ARCHITECTURE-SPINE.md, bloque ERD del servidor) lista cinco columnas para
      `INSTALLATION_MANIFEST`: manifestId, packageId, schemaVersion, signature y keyId. AD-22
      (tabla de propiedad de entidades) declara que el servidor es dueño del «Manifiesto canónico
      y firma», y AD-20 lo sirve como artefacto aparte: sin el cuerpo del manifiesto la tabla no
      puede cumplir su función. La migración inicial sí crea la columna. La espina debe
      alinearse; la historia que publica el manifiesto firmado (Story 2.2) es la que la usa.
    location: >-
      src/Server/Infrastructure/Persistence/Migrations/20261027133118_InitialCatalogSchema.cs
    severity: medium
  - summary: >-
      `CLIENT_REGISTRATION.machineName` no tiene índice único; la unicidad por forma canónica
      (AD-30) la debe añadir su historia propietaria mediante una migración.
    evidence: |-
      El ERD declara `string machineName UK` y AD-30 dice que la unicidad «es una restricción
      de columna» por forma canónica (sin acentos, sin signos, sin mayúsculas). La migración
      inicial deja la columna sin índice único, y una restricción UNIQUE de SQLite es
      sensible a mayúsculas, de modo que no puedeservir para la forma canónica. La regla de
      normalización no debe duplicarse en esta historia (AD-1): la endurece la historia del
      alta y el renombrado del cliente.
    location: >-
      src/Server/Infrastructure/Persistence/Migrations/20261027133118_InitialCatalogSchema.cs
    severity: medium
  - summary: >-
      El socket UDP 5353 del anunciante mDNS enlaza el comodín (`0.0.0.0`) porque
      `Haukcode.Mdns` no permite elegir interfaz, y AD-12 exige escuchar solo en interfaces privadas.
    evidence: |-
      Comprobado sobre el binario en ejecución: los sockets TCP quedaban en `192.168.1.124` y
      `172.18.16.1`, mientras que el socket UDP 5353 era `0.0.0.0`. El respondedor RFC 6762
      necesita recibir la consulta en cualquier interfaz para poder contestarla, y la biblioteca
      no expone la selección de adaptador. La pregunta de fondo —si el comodín del UDP 5353 es
      aceptable bajo AD-12 o si hace falta otro anunciante— es de la espina, y el spike S-1 es
      el lugar natural para cerrarla con evidencia en una LAN real.
    location: >-
      src/Server/Infrastructure/Networking/MdnsServiceAdvertiser.cs
    severity: medium
  - summary: >-
      El payload TXT del anuncio mDNS (`txtvers=1`) no tiene prueba que verifique que nunca
      lleva el fingerprint del ancla.
    evidence: |-
      AD-2 y AD-27 exigen que el ancla no viaje por el canal que la autentica. El TXT es un
      diccionario estático que nada muta y el ancla no existe hasta la Story 4.4, que publica
      el fingerprint por `GET /api/v1/server/info`. La prueba encaja mejor en esa historia.
    location: >-
      src/Server/Infrastructure/Networking/MdnsServiceAdvertiser.cs
    severity: low
---

<intent-contract>

## Intent

**Problem:** El servidor no arranca: `Program.cs` es un `WebApplication.CreateBuilder(args).Build().Run()` sin configuración, sin persistencia, sin red y sin métricas. El organizador no puede levantar el evento, y AD-12, AD-11 y AD-17 fijan ya la forma de esa superficie.

**Approach:** Componer el host con configuración efectiva (`flags > env > archivo`), carpeta de datos configurable con `catalog.sqlite` y migraciones de EF Core aplicadas al inicio, escucha únicamente en interfaces privadas, anuncio mDNS del servicio `_lanpartyhub._tcp` y endpoint local de métricas.

## Boundaries & Constraints

**Always:**

- El servidor escucha **solo en interfaces privadas** (RFC1918 IPv4 y ULA IPv6) de adaptadores operativos con multicast, y **solo** en el puerto HTTP (API + SignalR) y en el UDP 5353 de mDNS (AD-12, AR-17). Ningún otro puerto, ningún TLS, ninguna autenticación.
- `ASPNETCORE_URLS` (clave `urls`) se neutraliza: si no, Kestrel descarta los `Listen()` explícitos y acaba en `0.0.0.0`.
- Precedencia de configuración `flags > env > archivo`; los proveedores se registran en ese orden y el último que tiene la clave gana (AD-12, AR-23).
- `--data-dir` y `--http-port` son los **únicos** flags del servidor. Un flag desconocido, un valor ausente o un puerto inválido **fallan de forma ruidosa**; no se ignoran en silencio.
- El servidor crea en `--data-dir`: `catalog.sqlite` y el repositorio de paquetes `packages/`. La carpeta sobrevive al reemplazo del binario (AD-17).
- SQLite + EF Core, **un** archivo por rol. Las migraciones están versionadas y se aplican **automáticamente al inicio** con `MigrateAsync()`, nunca `EnsureCreated()` (AD-11, AR-12).
- El esquema de `catalog.sqlite` es el del ERD de la espina, con los **nombres y el orden exactos** de las columnas, y ninguna tabla del servidor existe en el cliente (AD-22, AD-34).
- El log del servidor es **JSON** desde el primer arranque (AD-15). La correlación `operationId`/`clientId` es de la Story 1.4.
- Las métricas se exponen **localmente** en el mismo puerto HTTP, con la forma que fija AD-15. Ninguna telemetría sale a Internet (NFR-10, AD-13).

**Never:**

- No abrir ningún puerto de tracker, P2P, DHT ni LPD; no referenciar `MonoTorrent` ni ninguna biblioteca P2P; no exponer flag ni selector de motor de transferencia (AD-13, AD-28).
- No contactar ningún servicio de Internet: ni telemetría, ni documentación, ni DNS externo, ni NuGet en tiempo de ejecución.
- No usar `EnsureCreated()`, ni `Database.Facade.EnsureCreated`, ni crear tablas a mano: solo migraciones versionadas.
- No duplicar en `Shared` ningún contrato: `Shared` sigue con exactamente 20 contratos de primer nivel y sin referencia a EF Core, Avalonia ni red.
- No meter EF Core, SQLite, mDNS ni sockets en `src/Server/Domain/` ni en `src/Server/Application/`: solo en `Infrastructure` y en la composición del host.
- No implementar todavía la lógica de negocio de los endpoints REST de catálogo, paquetes ni clientes (son de las épicas 2, 3, 5 y 9).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Flag válido | `--data-dir D:\datos --http-port 8080` | configuración efectiva con esos valores | — |
| Flag desconocido | `--tracker-port 6881` | arranque abortado | mensaje con el flag y la lista de flags admitidos |
| Valor ausente | `--data-dir` al final de `args` | arranque abortado | mensaje «missing value» |
| Valor que parece otro flag | `--data-dir --http-port 8080` | arranque abortado | no se traga el valor ni se pierde el puerto |
| Puerto inválido | `--http-port 70000` | arranque abortado | rango 1-65535 |
| Precedencia | flag, env y archivo fijan la misma clave | gana el flag; sin flag, la env; sin env, el archivo | — |
| Sin interfaz privada | máquina sin RFC1918/ULA operativa | arranque abortado | se explica que enlazar a `0.0.0.0` expondría el hub fuera de la LAN |
| `ASPNETCORE_URLS` presente | variable de entorno con `http://+:5000` | la clave `urls` se ignora; el bind sigue siendo privado | — |
| Carpeta de datos nueva | `--data-dir` inexistente | se crea con `catalog.sqlite` y `packages/` | — |
| Base ya migrada | `catalog.sqlite` en la versión actual | no se aplica nada; la operación es idempotente | — |
| Migración pendiente | `catalog.sqlite` en una versión anterior | se aplica solo la pendiente y se registra en `__EFMigrationsHistory` | — |
| mDNS falla | el anuncio no puede emitirse | **el servidor arranca igual** | el fallo se registra; la vía manual sigue operativa (AD-3) |
| Puerto ocupado | el puerto HTTP ya está en uso | arranque abortado | error del socket, sin reintentos silenciosos |

</intent-contract>

## Code Map

- `src/Server/Program.cs` -- raíz de composición mínima; hay que sustituirla por la composición real (config, Kestrel, DI, migraciones, métricas, mDNS, logging JSON).
- `src/Server/LANPartyHub.Server.csproj` -- `Microsoft.NET.Sdk.Web`, `net10.0`, sin paquetes; hay que añadir EF Core, el proveedor SQLite y mDNS. El comentario del `FrameworkReference` explica por qué no se declara (NETSDK1086).
- `src/Server/{Domain,Application,Infrastructure}/` -- solo tipos marcadores (`DomainLayer.LayerName` etc.). `Domain` y `Application` **no** pueden tocar infraestructura (guardas de `tests/UnitTests/ProjectDependencyTests.cs`).
- `src/Shared/Contracts/` -- 20 contratos de primer nivel; `Shared` no puede crecer ni referenciar EF Core, SQLite, Avalonia ni red (`SharedContractInventoryTests`).
- `tests/UnitTests/ProjectDependencyTests.cs` -- guardas: `No_production_project_references_a_sqlite_package_it_does_not_own` **permite** `Microsoft.Data.Sqlite` y `Microsoft.EntityFrameworkCore.Sqlite` en `Server`; `No_type_of_the_domain_layer_imports_infrastructure_or_a_framework` prohíbe `Microsoft.EntityFrameworkCore`, `Microsoft.Data.Sqlite` y `HttpClient` en `Domain`.
- `tests/IntegrationTests/` -- único proyecto con `Microsoft.Data.Sqlite 10.0.13`; `SqliteDownloadProgressStore` usa `Data Source={path};Pooling=False` y crea su carpeta antes de abrir. `RepositoryLayout` no está aquí: hay que replicarlo o factorizarlo.
- `tests/EndToEndTests/RepositoryLayout.cs` -- precedente de cómo el proyecto de pruebas encuentra la raíz del repo (`LAN_PARTY_HUB_REPO_ROOT` y luego subir hasta el `.sln`).
- `_bmad-output/planning-artifacts/epics/.../epics.md` líneas 968-1023 -- Story 1.2 completa: 5 AC, dependencias, casos de error, nivel de prueba **Integración** y DoD.
- `_bmad-output/planning-artifacts/architecture/.../ARCHITECTURE-SPINE.md` -- AD-11 (SQLite y migraciones), AD-12 (superficie de red), AD-13 (sin trackers/DHT), AD-15 (logging JSON y endpoint de métricas), AD-17 (carpeta de datos), AD-22 (dueño de cada entidad), AD-3 (mDNS y dirección manual equivalentes), AD-27/AD-30/AD-31 (ancla y nombre de equipo) y el ERD del servidor.
- `docs/estrategia-pruebas.md` §2.1 -- la fila «Endpoints con `WebApplicationFactory`» figura como pendiente de la 1.2: hay que actualizarla.

## Tasks & Acceptance

**Execution:**

- [x] `src/Server/LANPartyHub.Server.csproj` -- añadir `Microsoft.EntityFrameworkCore` `10.0.13`, `Microsoft.EntityFrameworkCore.Sqlite` `10.0.13`, `Microsoft.EntityFrameworkCore.Design` `10.0.13` (`PrivateAssets=all`) y `Haukcode.Mdns` `1.0.22` -- stack fijada en la espina; EF Core debe ir en la misma versión que el proveedor
- [x] `src/Server/Host/ServerCommandLine.cs` -- parser estricto de `--data-dir` y `--http-port`: flag desconocido, valor ausente, valor que parece otro flag y puerto fuera de 1-65535 abortan el arranque con mensaje; el resultado se expone como configuración de mayor prioridad -- AC2
- [x] `src/Server/Host/ServerConfiguration.cs` -- composición de la configuración efectiva: `appsettings.json` junto al binario (opcional) → variables de entorno (`__` como separador de sección) → flags en memoria; claves `dataDirectory` y `httpPort`; valor por defecto de la carpeta de datos `data/` junto al ejecutable y puerto `8080` -- AC2, precedencia `flags > env > archivo`
- [x] `src/Server/Infrastructure/Networking/PrivateAddressResolver.cs` -- resolver las direcciones unicast RFC1918 (10/8, 172.16/12, 192.168/16) y ULA IPv6 (`fc00::/7`) de los adaptadores operativos con multicast; excluir loopback, link-local, túneles y direcciones públicas; `IPAddress.IsPrivate` no existe en .NET 10 -- AC1
- [x] `src/Server/Infrastructure/Persistence/CatalogDbContext.cs` -- el `DbContext` del servidor con mapeo explícito por entidad: los siete tablas del ERD (`GAME`, `PACKAGE_DESCRIPTOR`, `INSTALLATION_MANIFEST`, `CLIENT_REGISTRATION`, `CLIENT_INSTALLATION_VIEW`, `DOWNLOAD_INTENT`, `CLIENT_ERROR`) con los nombres y el orden exactos de columna; `GAME` refleja los 18 campos de `GameDescriptor` con el mismo nombre y orden (AD-34); `CLIENT_REGISTRATION.connectionState` es columna de texto con el nombre del enum (el ERD lo declara `string`); `CLIENT_INSTALLATION_VIEW.validationStateText` es texto, nunca el enum (AD-22); `INSTALLATION_MANIFEST` guarda además el manifiesto canónico, porque AD-22 dice que su dueño es «Manifiesto canónico y firma»; `DOWNLOAD_INTENT` tiene `state` y `outcome` (AD-33) -- AC4, AD-22, AD-34
- [x] `src/Server/Infrastructure/Persistence/CatalogDbContextFactory.cs` -- `IDesignTimeDbContextFactory` con el mismo proveedor y el mismo ensamblado de migraciones que el runtime -- la herramienta `dotnet-ef` salta el host y necesita la fábrica
- [x] `src/Server/Infrastructure/Persistence/Migrations/` -- migración inicial generada con `dotnet-ef` `10.0.13` a partir del modelo, más la instantánea de modelo; la herramienta se instala con `dotnet tool install --global dotnet-ef --version 10.0.13` y debe coincidir con la versión de los paquetes EF Core -- las migraciones deben estar versionadas, no creadas a mano
- [x] `src/Server/Infrastructure/Persistence/DataDirectory.cs` -- resolver y crear `--data-dir` antes de abrir la conexión (SQLite falla con `SQLITE_CANTOPEN` si falta), y crear dentro el repositorio `packages/` -- AC3
- [x] `src/Server/Infrastructure/Networking/MdnsServiceAdvertiser.cs` -- anunciante `_lanpartyhub._tcp` sobre UDP 5353 aislado detrás de una interfaz propia del proyecto; TTL de multicast 255; el nombre de instancia es el nombre del equipo y el registro TXT solo lleva `txtvers=1` (el fingerprint del ancla lo publica `GET /api/v1/server/info`, Story 4.4); si el anuncio falla, el servidor **arranca igual** y el fallo queda registrado -- AC1 y AD-3 (la vía manual sigue siendo equivalente)
- [x] `src/Server/Host/MetricsEndpoint.cs` -- endpoint local de métricas en el mismo puerto HTTP con la forma de AD-15 (`tasa de transferencia`, `operaciones activas`, `errores por código`); los valores los instrumenta la Story 1.4 -- AC4 y Story 1.4 AC2
- [x] `src/Server/Program.cs` -- componer: logging JSON, configuración efectiva, `ConfigureKestrel` con un `Listen` por dirección privada y `builder.Configuration["urls"] = null`, `MigrateAsync()` antes de `Run()`, endpoint de métricas, arranque del anunciante mDNS y apagado ordenado -- AC1, AC3, AC4
- [x] `tests/UnitTests/ServerCommandLineTests.cs` -- flag válido, flag desconocido, valor ausente, valor que parece otro flag, puerto inválido y la precedencia `flags > env > archivo` -- AC2 y la matriz de I/O
- [x] `tests/IntegrationTests/ServerStartupTests.cs` -- arrancar el host real contra un `--data-dir` temporal: se crea `catalog.sqlite` y `packages/`, las siete tablas existen, `__EFMigrationsHistory` registra la migración, el arranque es idempotente sobre una base ya migrada, el endpoint de métricas responde, el único puerto TCP en escucha es el HTTP y su dirección es privada, y hay un socket UDP en 5353; si la máquina no tiene interfaz privada, la prueba se omite con el motivo explícito y otra prueba exige que el servidor se niegue a arrancar en ese caso -- AC1, AC3, AC4 y nivel de prueba **Integración** de la historia
- [x] `docs/estrategia-pruebas.md` -- actualizar la fila de `WebApplicationFactory` y el contenido declarado de la 1.2 en §2.1 y §5 -- el nivel de integración ya no está pendiente
- [x] `README.md` -- cómo arrancar el servidor con `--data-dir` y `--http-port` y qué contiene la carpeta de datos -- verificación reproducible

**Acceptance Criteria:**

- Given el binario del servidor recién publicado, when arranca con `--data-dir` y `--http-port`, then escucha solo en interfaces privadas y abre solo el puerto HTTP (API + SignalR) y el UDP 5353 de mDNS, and no abre ningún puerto de tracker ni de P2P
- Given el servidor arrancado, when se inspecciona la configuración efectiva, then la precedencia es `flags > env > archivo`, and el servidor no expone ningún flag, selector ni puerto de motor de transferencia P2P
- Given el servidor arrancado con un `--data-dir` nuevo, when se consulta su estado, then crea en esa carpeta su archivo de datos SQLite (`catalog.sqlite`) y su repositorio de paquetes, and the folder survives binary replacement
- Given el servidor en ejecución, when se aplican las migraciones de EF Core, then se aplican automáticamente al inicio y son versionadas
- Given a LAN local without Internet, when the server is running, then no intenta contactar ningún servicio en Internet, and does not depend on trackers, DHT ni servicios externos públicos

## Spec Change Log

- **2026-10-27 · El mecanismo del enlace privado estaba invertido.** *Disparador*: la capa de verificación de brechas comprobó con una sonda sobre el SDK 10.0.403 que, con `ASPNETCORE_URLS` puesto, Kestrel registra `Overriding address(es) 'http://+:5000'` y **conserva** los `Listen()` explícitos; lo que descarta son las direcciones de configuración. La nota de diseño afirmaba lo contrario, y la línea `builder.Configuration["urls"] = null` era un no-op sobre una clave ya vacía. *Amendado*: la nota de diseño «`urls` se neutraliza antes de configurar Kestrel» se sustituye por «El enlace privado no depende de ningún valor por defecto del framework», que describe el mecanismo real; en el código, `PreferHostingUrls` se fija a `false` de forma explícita y la línea inerte desaparece. *Estado malo evitado*: una garantía de seguridad (AD-12) apoyada en un mecanismo descrito al revés y en una línea que no hacía nada; si alguien activaba `PreferHostingUrls`, el agujero se abriría creyendo que la línea lo cubría. *KEEP*: la prueba de integración que vigila el enlace se conserva como guard, y la lista de comandos de verificación del spec mantiene las cuatro guardias del repositorio.
- **2026-10-27 · Comandos de verificación desalineados.** *Disparador*: el `git grep` del spec esperaba «sin coincidencias» y ahora encuentra `announce` en los ficheros legítimos del anuncio mDNS; además el README documentaba una comprobación de deriva de esquema que el spec no exigía. *Amendado*: el `git grep` separa ahora las palabras prohibidas de `announce`, y se añade `dotnet ef migrations has-pending-model-changes` a la lista obligatoria. *KEEP*: las cuatro guardias del repositorio siguen siendo obligatorias.

## Review Triage Log

### 2026-10-27 — Review pass
- verdicts: 53 findings — high 0, medium 33, low 16, false 4, maybe-false 0
- findings:

**Capa blind-hunter (18)**

- `[medium]` `[defer]` `INSTALLATION_MANIFEST.canonicalManifest` no está en el ERD — AD-22 declara que su dueño es «Manifiesto canónico y firma», luego la espina es la que está incompleta; el ERD debe alinearse. Diferido con entrada en `deferred:`.
- `[medium]` `[defer]` el índice único de `machineName` falta y el diferido no tenía dueño — la unicidad por forma canónica es de AD-30 y la endurece su historia mediante una migración. Diferido con entrada en `deferred:`.
- `[medium]` `[patch]` `sprint-status.yaml` seguía con la historia 1.2 en `backlog` — el fichero de seguimiento mentía sobre el estado real. Corregido en el parche.
- `[low]` `[patch]` el README no dice que los contadores de métricas están inertes hasta la Story 1.4 — un operador leería `/metrics` como roto. Añadido el aviso.
- `[medium]` `[patch]` la forma del snapshot de métricas no tiene prueba unitaria y `HostMetrics` no tiene ninguna — AD-15 fija esa forma. Prueba añadida.
- `[low]` `[patch]` `HostMetrics.ErrorsByCode` expone el `ConcurrentDictionary` vivo — un consumidor puede mutar los contadores por detrás. Ahora devuelve una copia.
- `[medium]` `[patch]` la prueba de mDNS exige `IsAdvertising == true` y falla en una red que bloquea el multicast, justo el caso que AD-3 obliga a tolerar — se le añadió el `Assert.Skip` con motivo.
- `[medium]` `[defer]` nada comprueba que el socket UDP de mDNS esté en interfaces privadas — el socket enlaza el comodín por diseño de la biblioteca y de RFC 6762; la pregunta es de la espina. Diferido con entrada en `deferred:`.
- `[medium]` `[patch]` el filtro de multicast se aplicaba al enlace HTTP, donde no hace falta — un adaptador LAN sin multicast hacía que el servidor no arrancara. Separado: multicast solo para mDNS.
- `[medium]` `[patch]` `PrivateAddressResolver` no tenía prueba unitaria y su lógica era privada — extraído un predicado puro `IsPrivate` y probado en sus límites.
- `[medium]` `[patch]` la cadena de conexión SQLite interpolaba la ruta sin escapar — un `;` en el nombre de carpeta (legal en Windows) la rompía. Reproducido por la capa de casos límite. Ahora se construye con `SqliteConnectionStringBuilder`.
- `[medium]` `[patch]` `MulticastTimeToLive = 255` no configuraba nada: solo se interpolaba en el log — o se pasa a la biblioteca o desaparece la constante.
- `[medium]` `[patch]` el anunciante guardaba el advertiser solo tras `Start()` correcto — si `Start()` fallaba tras enlazar el socket, este se fugaba y el puerto 5353 quedaba retenido.
- `[low]` `[reject]` la lista de verificación del spec no incluía `dotnet ef migrations has-pending-model-changes` — el arreglo es solo editar el spec y el guard ya está documentado en el README; no es un defecto del código.
- `[low]` `[patch]` `docs/estrategia-pruebas.md` asignaba a la 1.2 un contenido de `WebApplicationFactory` que la 1.2 no usa — fila dividida.
- `[low]` `[patch]` el `ItemGroup` `<None Update="appsettings.json">` es un no-op con un comentario engañoso — y el archivo se copia a la salida de los proyectos de prueba. Corregido.
- `[low]` `[patch]` el README nunca dice que `ASPNETCORE_URLS` se ignora a propósito — el operador ve una variable descartada sin explicación.
- `[low]` `[patch]` la matriz de I/O no tenía fila para un valor inválido que venga de la variable de entorno — añadida.
- `[low]` `[patch]` el aviso `oversized` del frontmatter no tenía detrás nada que lo explicara — anotado.

**Capa edge-case-hunter (20)**

- `[medium]` `[patch]` `;` en la ruta de la carpeta rompe la cadena de conexión — reproducido. Agrupado con el hallazgo homónimo del blind hunter.
- `[medium]` `[patch]` los fallos de arranque que no son `ServerStartupException` salen como traza de pila — el organizador ve una traza en vez del mensaje accionable.
- `[medium]` `[patch]` `--data-dir ""` (valor vacío) cae en silencio al valor por defecto — un valor ausente debe abortar.
- `[low]` `[reject]` una opción repetida descarta la primera en silencio — es la convención de .NET y la corrección añade una rama para un caso que no se va a dar en el uso diario.
- `[medium]` `[patch]` un valor vacío en `appsettings.json` o en la variable de entorno se sustituye en silencio por el valor por defecto.
- `[medium]` `[patch]` si `advertiser.Start()` falla tras enlazar el socket, este se fuga — agrupado con el hallazgo homónimo del blind hunter.
- `[low]` `[reject]` `Start()` llamado dos veces fuga el primer advertiser — no se demuestra que el servicio hospedado se registre dos veces.
- `[medium]` `[patch]` un adaptador que falla al leer sus propiedades tumba el arranque — ahora se salta ese adaptador.
- `[false]` `[reject]` un `string[]` nulo al calcular el hash del snapshot — el código ya usa `array?.Aggregate(...) ?? 0`.
- `[false]` `[reject]` el índice único de `packageId` en `INSTALLATION_MANIFEST` haría ambiguo el manifiesto vigente — el ERD declara la relación uno a uno, así que el índice la hace cumplir.
- `[low]` `[patch]` `ErrorsByCode` expone el diccionario vivo — agrupado con el hallazgo homónimo del blind hunter.
- `[medium]` `[patch]` la prueba de mDNS falla donde el multicast está bloqueado — agrupado con el hallazgo homónimo del blind hunter.
- `[medium]` `[patch]` dos pruebas que arrancan el host no tienen guarda de omisión y fallan en una máquina sin interfaz privada — agrupado con el mismo grupo.
- `[medium]` `[patch]` la prueba de `ASPNETCORE_URLS` tampoco tiene guarda de omisión — agrupado con el mismo grupo.
- `[low]` `[patch]` si `StartAsync` lanza dentro de la prueba, el listener queda vivo y envenena la siguiente — se libera en el fallo.
- `[low]` `[reject]` la carrera entre sondear un puerto libre y que Kestrel lo enlace — la ventana es mínima y la corrección reestructura la prueba.
- `[medium]` `[patch]` la neutralización de `urls` es un no-op y el comentario dice el mecanismo al revés — agrupado con el hallazgo de la capa de verificación de brechas.
- `[medium]` `[patch]` `--data-dir ""` no aborta — agrupado con el hallazgo homónimo.
- `[medium]` `[defer]` el esquema no coincide con el ERD: `canonicalManifest` de más y `machineName` sin índice único — ambos diferidos con entrada en `deferred:`.
- `[medium]` `[patch]` dos pruebas sin guarda de omisión fallan en vez de omitirse — agrupado con el mismo grupo.

**Capa verification-gap (7, pre-verificados)**

- `[medium]` `[patch]` la clasificación de direcciones privadas nunca se contrasta con los rangos privados: la prueba usa el resolver como su propio oráculo y solo rechaza loopback y `Any` — predicado puro extraído y probado, y aserción de integración endurecida a RFC1918/ULA.
- `[medium]` `[patch]` seis de las siete tablas del ERD no tienen contrato de columnas fijado — ampliada la prueba de integración con la lista esperada de columnas de las seis restantes.
- `[medium]` `[patch]` la neutralización de `urls` es un no-op y su prueba no puede observarla: Kestrel con `ASPNETCORE_URLS` registrado en el log «Overriding address(es)» y conserva los `Listen()` explícitos — el mecanismo real es `PreferHostingUrls=false`, que ahora se fija explícitamente, y el comentario y la nota de diseño del spec se corrigen.
- `[medium]` `[patch]` el «solo» de AC1 se comprueba para TCP y no para UDP — añadido el diff de sockets UDP antes/después; la pregunta del comodín del socket mDNS queda diferida.
- `[low]` `[defer]` el payload TXT del anuncio no está verificado — agrupado con el diferido homónimo.
- `[medium]` `[patch]` `MulticastTimeToLive` solo se interpola en el log — agrupado con el hallazgo homónimo del blind hunter.
- `[medium]` `[patch]` la prueba de mDNS falla donde el multicast está bloqueado y dos pruebas carecen de guarda de omisión — agrupado con el grupo de pruebas dependientes del entorno.

**Capa intent-alignment (8 divergencias)**

- `[low]` `[reject]` el binario publicado nunca se ejerce en una prueba — el nivel declarado de la historia es la integración con el host real; el artefacto publicado es de AD-17 y del guion E2E de la Story 13.1.
- `[medium]` `[patch]` el «solo interfaces privadas» se comprueba solo para TCP y el socket UDP es el comodín — agrupado con el hallazgo de la capa de verificación de brechas (diff UDP) y con el diferido del comodín.
- `[false]` `[reject]` el puerto HTTP no tiene aún API ni SignalR — es lo que fijan las historias 2, 3, 5 y 9; el puerto sí es el que exige AC1.
- `[medium]` `[patch]` la configuración efectiva no es observable en el servidor arrancado, que es la superficie que pide AC2 — se registra al arrancar.
- `[low]` `[reject]` «sobrevive al reemplazo del binario» no tiene prueba — es una propiedad estructural de tener la carpeta de datos separada, y el valor por defecto ya tiene su prueba unitaria.
- `[medium]` `[patch]` el «no contacta ningún servicio de Internet» de AC5 se sostiene solo de forma estructural — se añade la aserción de que el proceso no tiene conexiones salientes y de que el servidor no referencia cliente HTTP.
- `[false]` `[reject]` las migraciones se aplican antes de que Kestrel escuche — «al inicio» se cumple; AC4 no exige que el servidor esté ya escuchando.
- `[low]` `[patch]` la capa «archivo» de la precedencia resuelve contra la carpeta de salida de la prueba, no contra la de publicación — agrupado con el hallazgo del `ItemGroup` de `appsettings.json`.

## Design Notes

- **Sólo direcciones privadas, sin loopback.** «Escucha solo en interfaces privadas» se lee al pie de la letra: RFC1918 y ULA. El loopback no es una interfaz privada y, por el propio criterio de AD-12 (no exponer el servidor fuera de la red del evento), enlazarlo debilitaría la garantía. La prueba de integración obtiene del host la dirección realmente enlazada y conecta por ella; si la máquina no tiene ninguna, la prueba se omite con el motivo y hay otra prueba que exige que el servidor **se niegue a arrancar** en ese caso, en vez de enlazar a `0.0.0.0`.
- **El enlace privado no depende de ningún valor por defecto del framework.** Con `ASPNETCORE_URLS` puesto, Kestrel registra `Overriding address(es) 'http://+:5000'` y **conserva** los `Listen()` explícitos: lo que descarta son las direcciones de configuración, no las explícitas, porque su `PreferHostingUrls` viene a `false`. La garantía de AD-12 no puede depender de ese valor por defecto, así que `PreferHostingUrls` se fija a `false` de forma explícita en las opciones de Kestrel. La clave `urls` de la configuración queda vacía al vaciar las fuentes, y no vuelve a introducirse porque el proveedor de variables de entorno expone `ASPNETCORE_URLS` con su propio nombre. La prueba de integración es la que vigila la garantía.
- **Los flags se parsean a mano, no con el proveedor estándar.** El proveedor `CommandLine` acepta `--clave valor`, pero **ignora en silencio** los flags desconocidos y, si el valor parece otro flag, se lo traga (`--data-dir --http-port 8080` deja `data-dir = "--http-port"` y pierde el puerto). Un flag equivocado en un evento debe gritar, no pasar. Por eso el parseo es estricto y su resultado entra como la fuente de mayor prioridad.
- **mDNS: `Haukcode.Mdns` 1.0.22, aislado detrás de una interfaz propia.** La espina no fija ninguna biblioteca de mDNS y el blast radius está acotado porque AD-3 mantiene la dirección manual como vía permanente y equivalente. Entre los candidatos activos, es el único que implementa el anuncio RFC 6762/6763 (probe, tres anuncios, refresco de TTL y goodbye) y el único mantenido en 2026. Sus tipos no deben salir de `Infrastructure`; si hay que cambiarlo, solo se toca esa implementación.
- **El anuncio mDNS no puede impedir el arranque.** Si el multicast está bloqueado por la política de la red, el servidor debe seguir sirviendo: la vía manual es equivalente (AD-3) y el ancla se fija out-of-band (AD-27).
- **`MigrateAsync()`, nunca `EnsureCreated()`.** `EnsureCreated()` esquiva el historial de migraciones y hace que `Migrate()` falle después. La comprobación de que no queda ninguna migración pendiente al arrancar convierte el AC4 en algo verificable.
- **El esquema se materializa una vez, desde el ERD.** Las siete tablas del servidor se crean en la migración inicial con el nombre y el orden exactos de columna, y cada historia que necesite más columnas añade su migración. La unicidad de `machineName` por forma canónica (AD-30) la endurece su historia propietaria mediante una migración: la regla de normalización no se duplica aquí (AD-1).
- **`connectionState` se guarda como texto.** El ERD lo declara `string` y el servidor es su dueño (AD-22), así que almacena el nombre del enum; el enum `ConnectionState` de `Shared` no se persiste en otro campo.

## Verification

**Commands:**

- `dotnet build LANPartyHub.sln` -- expected: compilación sin errores ni advertencias
- `dotnet test LANPartyHub.sln` -- expected: los tres proyectos de prueba se ejecutan y pasan
- `dotnet ef migrations has-pending-model-changes --project src\Server\LANPartyHub.Server.csproj --startup-project src\Server\LANPartyHub.Server.csproj` -- expected: sin deriva entre el modelo y las migraciones
- `powershell -NoProfile -File tools/check-encoding.ps1` -- expected: PASS, sin CJK ni U+FFFD
- `powershell -NoProfile -File tools/check-epics.ps1` -- expected: PASS, sin cambios de conteo
- `git grep -in "monotorrent\|bittorrent\|swarm\|tracker" -- "*.csproj" "*.cs"` -- expected: sin coincidencias; `announce` sí aparece, y solo en el anuncio mDNS y en las guardas que prohíben esas palabras

## Auto Run Result

- **Resumen.** El servidor ahora arranca con configuración efectiva `flags > env > archivo`, carpeta de datos configurable con `catalog.sqlite` y el repositorio `packages/`, migraciones de EF Core versionadas aplicadas al arranque, enlace solo en interfaces privadas, anuncio mDNS de `_lanpartyhub._tcp` en el UDP 5353 y endpoint local de métricas en el mismo puerto HTTP.
- **Ficheros.** `src/Server/LANPartyHub.Server.csproj` (paquetes de EF Core y mDNS); `src/Server/Program.cs` (composición del host); `src/Server/Host/{ServerCommandLine,ServerConfiguration,HostMetrics,HostMetricsSnapshot,MetricsEndpoint,ServerStartupException}.cs`; `src/Server/Infrastructure/Networking/{PrivateAddressResolver,MdnsServiceAdvertiser,MdnsAdvertisementService}.cs`; `src/Server/Infrastructure/Persistence/{CatalogDbContext,CatalogDbContextFactory,DataDirectory}.cs` + `Entities/` + `Migrations/`; `src/Server/appsettings.json`; `tests/UnitTests/{ServerCommandLineTests,PrivateAddressResolverTests,HostMetricsTests}.cs`; `tests/IntegrationTests/{ServerStartupTests,ProcessTcpConnections}.cs`; `README.md`; `docs/estrategia-pruebas.md`.
- **Revisión.** 53 hallazgos en cuatro capas (blind hunter, edge case hunter, verification gap, intent alignment): 33 medium, 16 low, 4 false, 0 maybe-false. Aplicados 13 grupos de parche; 6 filas diferidas que se agrupan en 4 entradas del frontmatter `deferred:`; 10 refutadas con su razón en el triage log.
- **Verificación.** `dotnet build` 0 errores y 0 advertencias; `dotnet test` 149 unitarias + 25 de integración + 3 E2E superadas, 1 E2E omitida con motivo; `check-encoding.ps1` y `check-epics.ps1` en PASS; sin deriva de modelo de EF Core.
- **Riesgos residuales.** El socket UDP 5353 del anunciante mDNS enlaza el comodín porque la biblioteca no permite elegir interfaz (diferido 3); el ERD no declara la columna `canonicalManifest` (diferido 1) ni la unicidad canónica de `machineName` (diferido 2); el escenario E2E en LAN real sigue siendo de la Story 13.1.
- **Revisión de seguimiento recomendada:** `true`. Se parcharon más de dos entradas `medium`, así que el criterio automático de la primera pasada pide otra pasada. **Riesgo no verificado que la nombra:** la superficie del binario publicado (código de salida y mensaje por `stderr` ante un flag inválido) solo está verificada a mano, no por una prueba automática, y las pruebas dependientes del entorno —interfaz privada ausente, multicast bloqueado— se omiten en las máquinas donde no se dan. En esta máquina no se omitieron: la red tenía RFC1918 y el multicast funcionó, de modo que el camino ejercitado sí es el real.
