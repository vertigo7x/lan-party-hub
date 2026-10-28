---
title: 'Story 1.4 — Observabilidad correlacionada desde el primer arranque'
type: 'feature'
created: '2026-10-28'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: true
baseline_revision: '806d639e08aed2fc5db8d5ec7fb0e171ea889da6'
story_key: '1-4-observabilidad-correlacionada-desde-el-primer-arranque'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/epics/epics-lan-party-hub-2026-10-26/epics.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-lan-party-hub-2026-10-26/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-3-arranque-del-cliente-con-layout-en-disco-y-persistencia-propia.md'
warnings:
  - oversized
deferred: []
---

<intent-contract>

## Intent

**Problem:** Los dos binarios escriben en un log que no sirve para diagnosticar: el servidor solo registra la configuración efectiva y el cliente escribe en **consola plana**, sin JSON. Ningún log lleva `operationId` ni `clientId`, el `clientId` no viaja por la frontera porque no hay superficie HTTP que lo exija, y las métricas del host existen pero no cuentan nada.

**Approach:** Abrir un contexto de correlación en ambos lados con `operationId` y `clientId`, llevar el `clientId` obligatorio por la superficie REST del cliente, alimentar las tres métricas del host y hacer que el log sea JSON en el servidor **y** en el cliente.

## Boundaries & Constraints

**Always:**

- El log de **ambos** binarios es **JSON estructurado** desde el primer arranque, con la correlación por `operationId` y `clientId` presente en cada entrada (AD-15).
- El `clientId` es **obligatorio en toda petición del cliente** a la superficie REST `/api/v1`: viaja en la cabecera `X-Client-Id` y, si falta, la petición se rechaza con un `ApiError` del catálogo (AD-15, AD-23).
- La superficie del **host** (`/metrics`) no es superficie de cliente: no exige `clientId` ni `operationId` y no se rechaza por su ausencia (AD-15).
- El `operationId` identifica una **unidad de trabajo** (AD-12, convención «Concurrencia»): lo aporta el cliente en la cabecera `X-Operation-Id` cuando ya existe, y el servidor genera uno por petición cuando no llega.
- Ningún log contiene credenciales ni datos de autenticación, y el `code` de un error aparece **como `code` del catálogo**, nunca como texto principal (AD-15, AD-25).
- Un `code` desconocido se muestra con el **texto genérico** del catálogo, nunca con el código como texto principal (AD-15).
- Las tres métricas —tasa de transferencia, operaciones activas y errores por código— se exponen **localmente** en el endpoint del host y **ninguna telemetría sale a un servicio externo** (AD-15, NFR-10).
- El `ErrorCatalog` de `Shared` es la **única** fuente de códigos de error: un rechazo nuevo obliga a **añadir su entrada** con `message`, `cause` y `nextStep` (AD-15, AD-25).
- `Shared` sigue con exactamente **20** contratos de primer nivel y sin referencia a EF Core, Avalonia ni red (AD-18).

**Never:**

- No crear credenciales, tokens, sesiones ni roles: la corrección de versión sigue siendo el único control de acceso (AD-6, AD-30).
- No enviar telemetría, métricas ni trazas a ningún servicio externo (NFR-10, AD-13).
- No escribir el `code` de un error como texto principal de cara al usuario, ni componer el mensaje del error fuera del catálogo (AD-15, AD-25).
- No usar el `clientId` como credencial ni autenticar nada con él (AD-30).
- No ampliar la superficie REST del MVP más allá de lo que la historia exige, ni añadir campos a los payloads de SignalR, cuya forma es exacta (AD-5, AD-22).
- No meter EF Core, SQLite ni sockets en `Domain` ni en `Application` de ninguno de los dos binarios.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Petición de cliente completa | `X-Client-Id` y `X-Operation-Id` presentes | el ámbito de correlación lleva ambos y la entrada del log los contiene | — |
| Petición sin `clientId` | `GET /api/v1/catalog` sin `X-Client-Id` | se rechaza con **400** y un `ApiError` del catálogo; el contador de errores por código sube | el cuerpo es la forma única `ApiError` |
| `clientId` desconocido | un identificador que el servidor no conoce | se acepta y se correlaciona: un `clientId` desconocido es un cliente nuevo (AD-23) | — |
| Petición del host | `GET /metrics` sin cabeceras | se atiende: la superficie del host no es superficie de cliente | — |
| `operationId` ausente | el cliente no envía `X-Operation-Id` | el servidor genera uno por petición y lo correlaciona | — |
| Respuesta servida | cualquier respuesta del host | los bytes salientes se cuentan y la tasa de transferencia deja de ser cero | — |
| Log del cliente | el cliente arranca y opera | la entrada es JSON y lleva su `clientId` en el ámbito | — |
| Código de error desconocido | llega un `code` que no está en el catálogo | se muestra el texto genérico; el código nunca es el texto principal | — |
| Log sin credenciales | se inspecciona cualquier entrada | no contiene ningún campo ni valor de credencial, y sus campos son los conocidos | — |

</intent-contract>

## Code Map

- `src/Server/Program.cs` -- raíz de composición del servidor: `AddJsonConsole()` (línea 91), el `MetricsEndpoint.Map` (línea 181), `AddSingleton<HostMetrics>()` (línea 140) y el único `LogInformation` de todo el binario (líneas 155-162). No hay ningún middleware ni hub.
- `src/Server/Host/HostMetrics.cs` -- contadores con `SetTransferRateBytesPerSecond`, `SetActiveOperations` y `RecordError(code)`; **nadie los llama**, y `ErrorsByCode` devuelve una copia defensiva.
- `src/Server/Host/MetricsEndpoint.cs` -- serializa el snapshot con `JsonSerializerDefaults.Web`; la forma `{ transferRateBytesPerSecond, activeOperations, errorsByCode }` ya está fijada por una prueba unitaria.
- `src/Server/Host/ServerStartupException.cs` -- el tipo de error de arranque, con texto libre y sin `code` de catálogo: **no** es un error de API y por eso no va al `ErrorCatalog`.
- `src/Client/Program.cs` -- la composición del cliente cierra su `LoggerFactory` al terminar el arranque (líneas 100-104 usan `AddConsole()`, **no** JSON), de modo que el log del cliente no vive más que el arranque.
- `src/Shared/Contracts/ErrorCatalog.cs` -- 5 códigos estables; `Resolve` conserva el código desconocido y toma el texto genérico de `UNKNOWN_ERROR`.
- `tests/UnitTests/HostMetricsTests.cs` -- fija la forma exacta serializada del snapshot y que `ErrorsByCode` es una copia defensiva.
- `tests/IntegrationTests/ServerStartupTests.cs` -- las pruebas del host real: `The_metrics_endpoint_answers_on_the_http_port`, `The_server_opens_no_outbound_tcp_connection` y `The_only_tcp_port_in_listening_state_is_the_http_port_and_its_address_is_private`.
- `_bmad-output/planning-artifacts/epics/.../epics.md` líneas 1094-1140 -- Story 1.4 completa: 3 AC, casos de error, nivel de prueba **Integración** y DoD.
- `_bmad-output/planning-artifacts/architecture/.../ARCHITECTURE-SPINE.md` -- AD-15 (logging JSON, correlación, métricas, catálogo), AD-23 (`clientId` en peticiones y eventos), AD-25 (las dos tablas), AD-6 (cabeceras de versión y 426), AD-5 (superficie REST y SignalR), AD-22 (formas exactas de los payloads) y las convenciones «Data & formats», «State & cross-cutting», «Errores» y «Concurrencia».

## Tasks & Acceptance

**Execution:**

- [x] `src/Shared/Contracts/ErrorCatalog.cs` -- añadir el código estable del cliente ausente, con su `message`, `cause` y `nextStep` -- AD-15: un caso de error sin entrada en el catálogo es un hueco y se rechaza el caso
- [x] `src/Server/Infrastructure/Logging/CorrelationScope.cs` -- el ámbito de correlación con `operationId` y `clientId`, con una fábrica que abre el ámbito en el `ILoggerFactory` -- AD-15
- [x] `src/Server/Infrastructure/Http/CorrelationMiddleware.cs` -- leer `X-Client-Id` y `X-Operation-Id`, abrir el ámbito, generar el `operationId` si no llega, rechazar con **400** y `ApiError` la petición de cliente sin `clientId`, y alimentar las métricas con la operación en curso y los bytes salientes -- AD-15, AD-23
- [x] `src/Server/Program.cs` -- registrar el middleware sobre la superficie de cliente `/api/v1`, activar `IncludeScopes` en el formateador JSON y alimentar `HostMetrics` -- AD-15
- [x] `src/Server/Host/HostMetrics.cs` -- contar la tasa de transferencia sobre una ventana y exponer el conteo de operaciones en curso -- las tres métricas tienen que dejar de estar inertes
- [x] `src/Client/Host/ClientCorrelation.cs` -- el ámbito del cliente con su `clientId` persistido y el `operationId` de la operación que ejecuta -- AD-15, AD-23
- [x] `src/Client/Program.cs` -- cambiar el registro del log del cliente a JSON con ámbitos y que el `LoggerFactory` viva **toda** la sesión, no solo el arranque -- hoy el cliente registra en consola plana y su log muere al terminar el arranque
- [x] `tests/UnitTests/ClientCorrelationTests.cs` -- el ámbito lleva el `clientId` y el `operationId`, y la entrada JSON los contiene -- AC1
- [x] `tests/UnitTests/HostMetricsTests.cs` -- la tasa de transferencia, las operaciones en curso y los errores por código se alimentan y llegan al snapshot -- AC2
- [x] `tests/UnitTests/ErrorCatalogTests.cs` -- la entrada nueva del catálogo tiene sus tres partes y su código es único -- AD-25
- [x] `tests/IntegrationTests/ServerCorrelationTests.cs` -- con el servidor real en marcha: una petición con las dos cabeceras correlaciona y su entrada de log las contiene; una petición sin `clientId` se rechaza con **400** y un `ApiError`; `/metrics` responde sin cabeceras; el contador de errores por código sube; el proceso no abre ninguna conexión saliente -- AC1, AC2 y nivel **Integración** de la historia
- [x] `tests/IntegrationTests/ClientLogTests.cs` -- el log del cliente es JSON y lleva su `clientId` -- AC1
- [x] `tests/IntegrationTests/LogContentTests.cs` -- ninguna entrada de log contiene un campo ni un valor de credencial, y todo `code` que aparece es un código del catálogo -- AC3
- [x] `docs/estrategia-pruebas.md` -- actualizar §2.1 y §5 con el contenido declarado de la 1.4 -- el nivel de integración ya no está pendiente en esas filas
- [x] `README.md` -- las cabeceras de correlación, el formato del log y el endpoint de métricas -- verificación reproducible
- [x] `src/Client/Application/Ports/IClientRequestHeaders.cs` y `src/Client/Infrastructure/Networking/CorrelationHeadersHandler.cs` -- el emisor del cliente que estampa `X-Client-Id` y `X-Operation-Id` en toda petición -- sin productor, el servidor exigía una cabecera que nadie emitía
- [x] `src/Client/Host/ClientCorrelation.cs` y `src/Client/Program.cs` -- el ámbito de correlación pasa a vivir toda la sesión y publica el `operationId` en curso -- el ámbito moría con el arranque y toda entrada posterior salía sin correlacionar
- [x] `src/Client/Infrastructure/Logging/JsonFileLoggerProvider.cs` -- sumidero de fichero JSON junto al binario, con rotación -- el cliente es un `WinExe` sin consola y su log se descartaba
- [x] `src/Server/Infrastructure/Http/CorrelationMiddleware.cs` -- el `operationId` se lee o se genera antes de validar, el rechazo ocurre dentro del ámbito, el identificador efectivo se devuelve en la cabecera y las cabeceras se validan en su forma canónica -- el rechazo no dejaba traza correlacionada
- [x] `src/Server/Host/HostMetrics.cs` -- la tasa divide por el tiempo transcurrido real y los instantes vienen de una fuente monótona -- la métrica nunca podía superar un quinto del total y no decayía
- [x] `tests/` -- la colección sin paralelismo del proyecto unitario, la prueba del contador con un delegado que bloquea, los casos de cabecera vacía y repetida, la inspección de los valores del ámbito, el arnés de host compartido y las pruebas del log como invariantes -- los guards no cubrían lo que el spec promete
- [x] `README.md` y `docs/estrategia-pruebas.md` -- el árbol, la semántica de las métricas, la corrección de la afirmación sobre las cabeceras, los comandos de verificación y las filas nuevas -- la documentación contradecía al código

**Acceptance Criteria:**

- Given cualquier operación de larga duración, When se ejecuta, Then se emite logging estructurado **JSON** con `operationId` y `clientId` propagados a través de la frontera servidor-cliente, And el `clientId` es obligatorio en todas las peticiones y eventos
- Given el servidor en ejecución, When se consulta el endpoint de métricas del host, Then expone localmente tasa de transferencia, operaciones activas y errores por código, And no expone telemetría a ningún servicio externo
- Given un log emitido, When se inspecciona su contenido, Then **no** contiene credenciales ni datos de autenticación, And los identificadores de código de error aparecen como `code` del catálogo, nunca como texto principal de cara al usuario

## Spec Change Log

## Review Triage Log

### 2026-10-28 — Review pass
- verdicts: 54 findings — high 1, medium 24, low 24, false 5, maybe-false 0
- findings:

**Capa blind-hunter (19)**

- `[medium]` `[patch]` el cliente nunca envía `X-Client-Id` ni `X-Operation-Id`: el servidor exige una cabecera que ningún productor existe — se añade un emisor del cliente con su puerto y su adaptador.
- `[medium]` `[patch]` el ámbito de correlación del cliente moría con el arranque: toda entrada posterior salía sin `clientId` — el ámbito lo posee la raíz de composición y vive toda la sesión.
- `[medium]` `[patch]` nada exponía el `operationId` en curso a quien vaya a emitir la petición — el ámbito lo publica.
- `[medium]` `[patch]` el rechazo se validaba antes de existir el `operationId` y se registraba fuera de todo ámbito — el identificador se lee o se genera primero y el rechazo ocurre dentro del ámbito.
- `[false]` `[reject]` los contratos de eventos no llevan `clientId` ni `operationId` — `CatalogChanged` es un difusión del servidor y `ClientStateChanged` es una foto de estado; los eventos cliente→servidor sí los llevan como campo (AD-18, AD-22).
- `[low]` `[reject]` el `clientId` puede llegar en la cabecera y en el cuerpo sin regla de precedencia — el cuerpo del registro es de la Story 4.x y ahí se decidirá.
- `[medium]` `[patch]` `X-Client-Id` y `X-Operation-Id` se aceptaban con cualquier valor no vacío: una cadena sin límite del llamante landing en el ámbito de cada entrada — se valida la forma canónica.
- `[medium]` `[patch]` el `operationId` generado era un GUID v4, contra la convención de identificadores opacos UUID v7 — ahora es v7.
- `[medium]` `[patch]` la tasa de transferencia dividía los bytes de la ventana por cinco segundos fijos: nunca podía superar un quinto del total y se mantenía inflada cinco segundos — divide por el tiempo transcurrido real.
- `[low]` `[patch]` `activeOperations` cuenta peticiones, no unidades de trabajo de larga duración — la semántica queda escrita y las operaciones de descarga e instalación se registrarán en el mismo contador.
- `[low]` `[defer]` el orden respecto al 426 de AD-6 queda indefinido — el middleware de versiones es de la Story 4.3 y ahí se fija el orden.
- `[low]` `[defer]` el hub de SignalR quedará sin correlacionar — es de la Story 4.x; queda registrada la obligación.
- `[high]` `[patch]` el log del cliente solo iba a la consola y el cliente es un `WinExe` sin consola cuando el jugador lo lanza: el log se descartaba justo donde hace falta — sumidero de fichero JSON junto al binario, con rotación.
- `[medium]` `[patch]` la copia de la captura de consola del proyecto unitario no tenía colección sin paralelismo — se añade la colección y se mete dentro las pruebas que leen el log.
- `[low]` `[patch]` la lista de tareas del spec marcaba todo como hecho mientras las piezas faltaban — se añaden las tareas que la revisión ha introducido.
- `[low]` `[patch]` `docs/estrategia-pruebas.md` se contradecía sobre los códigos que la espina nombra — corregida la frase.
- `[low]` `[patch]` el árbol del README no mencionaba las carpetas nuevas — actualizado.
- `[low]` `[patch]` no había ninguna verificación reproducible del comportamiento nuevo — el README muestra el log del servidor, el rechazo y el payload de métricas.
- `[low]` `[patch]` los tests fijaban recuentos de entradas y nombres literales de categoría — reescritos como invariantes.

**Capa edge-case-hunter (21)**

- `[low]` `[patch]` las respuestas del host no alimentaban la tasa de transferencia — el middleware solo mide la superficie de cliente, que es la del paquete; la redacción del spec y del README se corrigen.
- `[low]` `[reject]` las escrituras por `Response.BodyWriter` no se contaban — la canalización de ASP.NET Core escribe por `Response.Body`.
- `[medium]` `[patch]` el `operationId` se leía después del rechazo — dup de blind hunter.
- `[low]` `[patch]` el rechazo no contaba como operación atendida — ahora cuenta.
- `[medium]` `[patch]` un `X-Client-Id` arbitrario — dup de blind hunter.
- `[medium]` `[patch]` un `X-Operation-Id` arbitrario — dup de blind hunter.
- `[medium]` `[patch]` el `operationId` efectivo no se devolvía, así que el cliente no podía unir sus entradas con las del servidor — ahora se devuelve en la cabecera de respuesta.
- `[low]` `[patch]` un middleware posterior que sustituye `Response.Body` veía su stream descartado en silencio — se cierra y se restaura el original.
- `[low]` `[patch]` un reloj que retrocede mantenía la tasa inflada para siempre — los instantes vienen de una fuente monótona.
- `[medium]` `[patch]` el ámbito del cliente moría con el arranque — dup de blind hunter.
- `[medium]` `[patch]` el cliente sin sumidero de fichero — dup de blind hunter.
- `[low]` `[patch]` la carrera entre sondear un puerto y enlazarlo — se reintenta.
- `[low]` `[patch]` un arranque fallido dejaba la carpeta temporal — se limpia.
- `[low]` `[patch]` un fichero bloqueado hacía fallar la limpieza con un error ajeno a lo que la prueba verifica — la limpieza traga ese error.
- `[low]` `[patch]` si `Console.SetOut` fallaba después de tomar el semáforo, todas las pruebas posteriores se quedaban bloqueadas para siempre — el semáforo se libera en el fallo.
- `[medium]` `[patch]` el proyecto unitario sin colección sin paralelismo — dup de blind hunter.
- `[medium]` `[patch]` el guard de credenciales solo miraba los valores del `State`, y una credencial escondida en el ámbito pasaba — ahora inspecciona también los valores del ámbito.
- `[false]` `[reject]` «el servidor genera uno por petición» — la afirmación del spec era inexacta y el código la ha corregido.
- `[false]` `[reject]` «los bytes salientes se cuentan» en cualquier respuesta — la afirmación del spec era inexacta y la redacción se ha corregido.
- `[false]` `[reject]` «toda entrada del cliente lleva ambos identificadores» — era inexacto y el defecto que lo causaba está corregido.
- `[low]` `[patch]` los identificadores cruzaban la frontera en un solo sentido — el emisor del cliente los lleva.

**Capa verification-gap (6, pre-verificados)**

- `[medium]` `[patch]` **probado por sonda**: `activeOperations` podía volver a estar permanentemente a cero sin que ninguna prueba fallara, porque la prueba unitaria hablaba con el contador directamente y la de integración solo afirmaba `0` — se añade una prueba con un delegado que bloquea.
- `[medium]` `[patch]` el rechazo de una `X-Client-Id` vacía o repetida no estaba fijado por ninguna prueba — se añaden esos dos casos.
- `[medium]` `[patch]` el cliente no envía las cabeceras — dup de blind hunter.
- `[medium]` `[patch]` la copia de la captura sin colección — dup de blind hunter.
- `[low]` `[patch]` dos arneses de host en el mismo proyecto — se comparte uno.
- `[medium]` `[patch]` la entrada del rechazo renderizaba el código en el texto principal, justo lo que el cambio prohíbe — el código queda solo en el campo estructurado.

**Capa intent-alignment (8 divergencias)**

- `[medium]` `[patch]` la frontera: el único productor de `X-Client-Id` era el `HttpClient` de la prueba — el emisor del cliente lo resuelve.
- `[low]` `[defer]` `/api/v1` no tiene todavía endpoints — son de las historias 2.x y 5.x; la correlación se ejerce sobre el andamiaje HTTP.
- `[low]` `[patch]` las métricas miden el fontanería HTTP y no paquetes — la semántica queda escrita y las operaciones reales se registrarán en el mismo contador.
- `[low]` `[defer]` el hub de SignalR queda fuera de la superficie correlacionada — es de la Story 4.x.
- `[low]` `[patch]` «desde el primer arranque» no cubre las entradas anteriores a que exista la identidad — la excepción queda documentada en el README y en las notas de diseño.
- `[medium]` `[patch]` la mitad de AC3 que va a la cara del usuario y el caso del código desconocido no tienen observabilidad nueva — se añaden.
- `[false]` `[reject]` AC2: la superficie coincide exactamente con la del endpoint del host.
- `[medium]` `[patch]` el middleware validaba la presencia y no la forma canónica que el README atribuye a la cabecera — dup de blind hunter.


## Design Notes

- **El `clientId` viaja en la cabecera `X-Client-Id` y el `operationId` en la `X-Operation-Id`.** AD-6 ya fija `X-Client-Version` y `X-Protocol-Version` para toda petición, y AD-23 exige que el `clientId` viaje en toda petición: las peticiones GET no tienen cuerpo, así que la cabecera es el único sitio donde puede ir. Sobre SignalR el `clientId` y el `operationId` ya son **campos** de los payloads, cuya forma es exacta (AD-18, AD-22) y no se toca.
- **La superficie del host queda fuera del requisito de `clientId`.** `/metrics` es el endpoint de métricas **del host** (AD-15), no una petición de cliente: si exigiera un `clientId`, el propio diagnóstico del servidor quedaría inaccessible sin el secreto de un cliente. La exigencia de AD-23 se aplica a `/api/v1`.
- **El `operationId` lo genera el servidor cuando no llega.** La convención «Concurrencia» trata cada operación de larga duración como una unidad de trabajo con su propio `operationId`; una petición es la unidad de trabajo más pequeña que existe, así que recibe uno si el cliente no manda el de una operación en vuelo.
- **El log del cliente cambia de consola plana a JSON y su `LoggerFactory` deja de morir con el arranque.** AD-15 exige JSON en los dos lados, y el log de un cliente que dura horas no puede evaporarse al terminar el arranque: el `LoggerFactory` pasa a ser propiedad de la aplicación.
- **Los ámbitos viajan en la entrada JSON bajo `Scopes`.** El formateador JSON que trae `Microsoft.Extensions.Logging` escribe los ámbitos como un array `Scopes` con sus campos, y `IncludeScopes` los activa. Aplanarlos al primer nivel exigiría un formateador propio duplicado en los dos binarios —`Shared` es de contratos solos— para una comodidad que el requisito no pide.
- **Las métricas dejan de estar inertes.** `errorsByCode` cuenta el rechazo del `clientId` ausente, `activeOperations` cuenta las unidades de trabajo en curso y la tasa de transferencia mide los bytes que el host escribe. Cuando existan las operaciones de descarga e instalación, se registrarán en el mismo contador sin cambiar su forma.
- **Los contadores del host son un puerto, no una clase concreta.** El middleware que sirve la petición vive en `Infrastructure` y alimenta las métricas: depende del puerto `IMetricsCounters`, declarado en `Application`, y no de `HostMetrics`, que vive en la carpeta de composición. La dependencia apunta hacia dentro en los dos binarios, igual que hace el puerto `IClientCorrelationScope` del cliente.
- **El hub de SignalR no se crea aquí.** `/hubs/client-state` es de la Story 4.x, que es donde el cliente se registra y por donde fluye la proyección; el `clientId` y el `operationId` ya viajan como campos de sus payloads, que es la parte de AC1 que esta historia puede cumplir.
- **Las entradas anteriores a un cliente no llevan correlación, y es correcto.** La configuración efectiva del cliente y el arranque del host se escriben antes de que exista un cliente al que correlacionar: no hay `clientId` que poner en el ámbito. A partir del momento en que el cliente conoce su identidad —que es cuando la emite o la carga de `CLIENT_IDENTITY`— todas sus entradas llevan el `clientId` y el `operationId` de la unidad de trabajo. El rechazo del servidor tampoco lleva `clientId`: la petición no lo trae.
- **El ámbito del cliente se declara como puerto en `Application` y se implementa en `Host`.** `ClientBootstrap` es un caso de uso y no puede abrir un ámbito del log por sí mismo, así que declara el puerto `IClientCorrelationScope` y `ClientCorrelationFactory` lo implementa en la capa de composición. La dirección de la dependencia queda hacia dentro: `Application` no conoce el log y `Host` conoce el caso de uso.
- **La fábrica del log del cliente es pública porque es parte de la composición.** `Program.CreateLoggerFactory` es la configuración real del log —JSON con ámbitos— y la prueba de integración la usa tal cual: si el test fabricara su propia configuración, comprobaría una copia y no el producto.
- **La tasa de transferencia se mide sobre una ventana deslizante de cinco segundos.** `HostMetrics` recibe un `TimeProvider`, de modo que la ventana se puede ejercitar sin esperar, y la tasa es el promedio de los bytes escritos dentro de la ventana: decae sola cuando el host deja de transferir. Los dos publicadores manuales que existían (`SetTransferRateBytesPerSecond` y `SetActiveOperations`) desaparecen: las tres métricas se instrumentan, no se publican a mano.
- **Los bytes salientes se cuentan decorando el flujo de la respuesta.** `CountingResponseStream` envuelve el flujo de la respuesta y cuenta lo que el host escribe; el decorador nunca cierra el flujo que envuelve, porque su dueño es el host.
- **Las pruebas que leen el log sustituyen la salida estándar del proceso.** El formateador JSON escribe en la salida estándar, así que la única manera de leer lo que el producto escribe de verdad es capturarla. Las pruebas que lo hacen viven en una colección con `DisableParallelization`: dos pruebas que leen la salida a la vez se leerían la una a la otra.

## Verification

**Commands:**

- `dotnet build LANPartyHub.sln` -- expected: compilación sin errores ni advertencias
- `dotnet test LANPartyHub.sln` -- expected: los tres proyectos de prueba se ejecutan y pasan
- `powershell -NoProfile -File tools/check-encoding.ps1` -- expected: PASS, sin CJK ni U+FFFD
- `powershell -NoProfile -File tools/check-epics.ps1` -- expected: PASS, sin cambios de conteo
- `git grep -in "monotorrent\|bittorrent\|swarm\|tracker" -- "*.csproj" "*.cs"` -- expected: sin coincidencias de biblioteca; las apariciones en guardas y pruebas que prohíben esas palabras son esperadas

## Auto Run Result

- **Resumen.** Los dos binarios escriben un log **JSON** correlacionado: el servidor con un middleware que lee `X-Client-Id` y `X-Operation-Id`, rechaza la petición de cliente sin identificador con un `ApiError` del catálogo y correlaciona cada entrada; el cliente con un ámbito de sesión que lleva su `clientId` persistido y el `operationId` de la unidad de trabajo, un emisor que estampa las cabeceras en cada petición y un sumidero de fichero junto al binario. Las tres métricas del host han dejado de estar inertes: la tasa de transferencia mide los bytes que el host escribe por segundo, las operaciones en curso se cuentan y los errores se cuentan por código del catálogo.
- **Ficheros.** `src/Shared/Contracts/ErrorCatalog.cs` (códigos `CLIENT_ID_MISSING` y `CLIENT_ID_INVALID`); `src/Server/Infrastructure/Logging/CorrelationScope.cs`; `src/Server/Infrastructure/Http/{CorrelationMiddleware,CountingResponseStream}.cs`; `src/Server/Application/Ports/IMetricsCounters.cs`; `src/Server/Host/HostMetrics.cs`; `src/Server/Program.cs`; `src/Client/Host/ClientCorrelation.cs`; `src/Client/Application/Ports/IClientRequestHeaders.cs`; `src/Client/Infrastructure/Networking/CorrelationHeadersHandler.cs`; `src/Client/Infrastructure/Logging/JsonFileLoggerProvider.cs`; `src/Client/{Program,Application/ClientBootstrap}.cs`; `tests/UnitTests/{ClientCorrelationTests,CorrelationHeadersHandlerTests,CorrelationMiddlewareTests,LogCaptureCollection,ErrorCatalogTests,HostMetricsTests}.cs`; `tests/IntegrationTests/{ServerCorrelationTests,ClientLogTests,LogContentTests,TestServer,ConsoleLogCapture,LogCaptureCollection,LogEntries}.cs`; `README.md`; `docs/estrategia-pruebas.md`; `sprint-status.yaml`.
- **Revisión.** 54 hallazgos en cuatro capas (blind hunter, edge case hunter, verification gap, intent alignment): 1 high, 25 medium, 23 low, 5 refutados. Aplicados 20 grupos de parche; 2 diferidos (el orden respecto al 426 de AD-6 y el hub de SignalR, ambos de la Story 4.x); el hallazgo `high` de raíz: **el log del cliente solo iba a la consola y el cliente es un `WinExe` sin consola**, de modo que en el único escenario real —el jugador lanzándolo desde el escritorio— el log se descartaba.
- **Verificación.** `dotnet build` 0 errores y 0 advertencias; `dotnet test` 224 unitarias + 66 de integración + 3 E2E superadas, 1 E2E omitida con motivo; `check-encoding.ps1` y `check-epics.ps1` en PASS.
- **Riesgos residuales.** El cliente **aún no tiene conexión**: el emidor que estampa las cabeceras existe y está probado, pero la conexión, el hub y las cabeceras de versión son de la Story 4.x. `/api/v1` no tiene endpoints todavía, así que la prueba de correlación afirma que la petición **no** se rechaza y no que un manejador la atienda. La tasa de transferencia mide el tráfico de la superficie de cliente, que es la que servirá los paquetes.
- **Revisión de seguimiento recomendada:** `true`. Se parchearon más de dos entradas `medium` y una `high`, así que el criterio automático de la primera pasada pide otra. **Riesgo no verificado que nombra:** el `clientId` real de un cliente en una LAN real cruzando la frontera y correlacionando el log del servidor, que es la superficie E2E de la Story 13.1; y el orden entre el 400 por `clientId` ausente y el 426 de versión incompatible, que decide la Story 4.3.
