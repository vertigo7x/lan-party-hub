---
title: 'Story 9.2: Proyección de instalaciones y operaciones en curso'
type: 'feature'
created: '2026-11-10'
status: 'done'
route: 'dispatch'
review_loop_iteration: 1
baseline_commit: '8f98cb24d27b02fd7fd4fe2349520c4db0510cae'
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** El organizador ve equipos registrados, pero la tabla no muestra instalaciones ni operaciones reales: el cliente publica listas vacías, el servidor no las proyecta y las celdas siguen reservadas.

**Approach:** Implementar los AC1–AC7 de la 9.2 con publicación cliente→servidor, persistencia de instalaciones, operaciones vivas en memoria y una vista REST de todos los equipos. Enmendar primero los contratos exactos de AD-18/22/29/32/33; conservar la historia completa. La implementación final y la reparación ejecutadas por Build Lite deben usar `minimax-coding-plan/MiniMax-M3`.

## Boundaries & Constraints

**Always:** un único `GET /api/v1/clients`, sin petición adicional por equipo; usar `StateDisplayMap` para textos de estado/resultado; `ClientStateChanged.ValidationState` sigue siendo enum, `ClientInstallationView.ValidationStateText` sigue siendo `TEXT`; `PackageWithdrawn` es resultado, nunca `ApiError`/`ClientError`. Mostrar la fase real de instalación como identificador literal reportado, aunque esté en inglés, sin traducirla ni inventar porcentaje. Actualizar inventario de `Shared`, versión de protocolo y consumidores antes de dar por válidas las nuevas formas. Conservar instalaciones retiradas mientras el cliente las reporte. Si falla la lectura local, comunicar conexión/desconexión mediante un evento cliente→servidor separado que solo actualiza `connectionState`, sin publicar listas vacías ni crear clientes no registrados; enmendar AD/contratos y versión del protocolo antes de usarlo.

**Never:** autenticar clientes, publicar `installPath`, guardar el estado de máquina del cliente como enum del servidor, añadir migración vacía, usar un evento servidor→cliente con la lista de equipos ni declarar terminada la 9.2 solo con pruebas de payloads sintéticos.

## I/O & Edge-Case Matrix

| Escenario | Entrada | Resultado | Guarda |
| --- | --- | --- | --- |
| Datos reales | Cliente registrado con juegos y descarga activa | Una fila REST enriquecida y tabla con estado/progreso reales | Rechazar estados desconocidos sin corromper los otros datos |
| Instalación | Fase reportada sin bytes en `InstallationProgressUpdated` | Fase real, ningún porcentaje ficticio | Sin cambio del payload de progreso |
| Retirada | `OperationOutcomeReported` con `PackageWithdrawn` | Resultado visible, sin error; instalación conservada | Upsert por cliente/juego; deduplicar eventos |
| Desconexión | Reconexión o lectura local fallida | El nuevo evento declara `connectionState` sin sustituir juegos/operaciones; la siguiente lectura válida recupera el estado real | No crear filas para clientes no registrados ni publicar listas vacías por error |

</frozen-after-approval>

## Code Map

- `.build-lite/plans/9-2.md` — plan local validado con las rutas autorizadas.
- `_bmad-output/planning-artifacts/architecture/architecture-lan-party-hub-2026-10-26/ARCHITECTURE-SPINE.md` — AD-18/22/29/32/33 aún contradicen la ampliación; enmendar antes de tocar `Shared`.
- `src/Shared/Contracts/` — formas exactas, enum frente a microcopia y versión; evento nuevo `OperationOutcomeReported.cs`.
- `src/Client/Host/ClientRegistrationComposition.cs` — hoy publica `[], []`; `src/Client/Application/Ports/IClientStateStore.cs` expone datos locales; `src/Client/Host/InstallationComposition.cs` y `src/Client/Application/CatalogSync/CatalogSyncService.cs` originan fase y retirada; `src/Client/Infrastructure/Networking/ClientStateHubPublisher.cs` envía eventos.
- `src/Server/Host/CatalogHub.cs`, `src/Server/Application/Clients/ReportClientState.cs` — solo conexión hoy; el esquema ya tiene `validationStateText` y `outcome`.
- `src/Server/{Application/Clients/ListClients.cs,Host/ClientsEndpoint.cs,Presentation/Clients/ClientsViewModel.cs,Presentation/Clients/ClientsWindow.cs}` — lista REST y cuatro celdas vacías.

## Tasks & Acceptance

**Execution:**
- [x] `_bmad-output/planning-artifacts/architecture/architecture-lan-party-hub-2026-10-26/ARCHITECTURE-SPINE.md` — fijar formas nuevas, propiedad de operaciones, REST único, resultado terminal y protocolo; preservar compatibilidad de consumidores.
- [x] `src/Shared/Contracts/{ClientRegistrationView,OperationOutcomeReported,VersionInfo}.cs` y `tests/UnitTests/{SharedContractInventoryTests,HubEventContractTests,ClientRegistrationViewTests}.cs` — definir y fijar los contratos nuevos antes de publicar.
- [x] `src/Client/{Host/ClientRegistrationComposition.cs,Host/InstallationComposition.cs,Application/CatalogSync/CatalogSyncService.cs,Infrastructure/Networking/ClientStateHubPublisher.cs}` — publicar listas reales, progreso y resultado terminal en cambios y reconexión.
- [x] `src/Server/{Application/Clients/ReportClientState.cs,Application/Clients/ClientOperationsProjection.cs,Application/Ports/IClientInstallationViewRepository.cs,Infrastructure/Persistence/ClientInstallationViewRepository.cs,Host/CatalogHub.cs,Program.cs}` — proyección idempotente y segura por cliente, memoria concurrente y DI.
- [x] `src/Server/{Application/Clients/ListClients.cs,Infrastructure/Persistence/ClientRegistrationRepository.cs,Host/ClientsEndpoint.cs,Presentation/Clients/ClientsViewModel.cs,Presentation/Clients/ClientsWindow.cs}` — respuesta única y celdas reales; pruebas unitarias e integración de la matriz.
- [x] `ARCHITECTURE-SPINE.md`, `src/Shared/Contracts/`, `src/Client/Infrastructure/Networking/ClientStateHubPublisher.cs`, `src/Client/Host/ClientRegistrationComposition.cs`, `src/Server/Host/CatalogHub.cs` y tests de contrato/integración — declarar y publicar el evento de solo conexión autorizado, respetando el registro y sin modificar una instantánea completa cuando el store falla.
- [x] `src/Client/Application/Install/InstallationCoordinator.cs` y `src/Server/Application/Clients/ClientOperationsProjection.cs` — conservar/publicar cada fase en orden una sola vez, limpiar la operación en cancelación inicial y no resucitarla con progreso tardío; rechazar estados desconocidos sin afectar otras operaciones.
- [x] `tests/UnitTests/ClientsTableTests.cs`, `tests/IntegrationTests/ClientProjectionTests.cs` y tests focalizados — cubrir celdas de juegos/bytes/fase, `DOWNLOAD_INTENT.outcome`, `CLIENT_ERROR` real, fase/cancelación y actualización de conexión sin SQLite.

**Acceptance Criteria:**
- Given un cliente con operaciones activas, when publica su estado y progreso, then la tabla muestra descargas reales y fase de instalación real.
- Given un cliente registrado con instalaciones, when publica su estado, then el servidor guarda por cliente/juego texto de `StateDisplayMap` y la vista muestra versión sin `installPath`.
- Given una retirada durante una operación, when llega el resultado terminal, then se muestra `PackageWithdrawn`, se conserva la instalación y no se crea `ClientError`.
- Given varios clientes, when el organizador refresca la tabla, then obtiene estado, operaciones y errores existentes en un único GET sin consulta por equipo.

## Implementation Notes

- AD-18/22/29/30/32/33 actualizadas antes de cerrar los contratos. El protocolo actual pasa a 2; no hay migración porque las columnas ya existían.
- El cliente lee instalaciones y descargas locales, publica el estado completo al registrar/reconectar y en el inicio y fin de instalaciones; el progreso conserva la fase real. La retirada llega como resultado terminal separado del error.
- `ClientOperationsProjection` conserva la fase y los bytes reportados cuando recibe una instantánea posterior de la misma operación. `ListClients` entrega proyecciones y errores en un único GET.
- La revisión detectó y corrigió la carrera de progreso antes de la instantánea, la ausencia de descargas iniciadas después del registro, el cierre de operaciones terminales, el estado de la fase, la publicación duplicada de progreso, la fuga de operaciones canceladas, la falta de resultado en el detalle y el vaciado por instantánea local no disponible.
- Cuando falla la lectura local se omite la instantánea completa y se comunica conexión/desconexión mediante `ClientConnectionChanged`; solo cambia la conexión de la fila existente. El protocolo actual pasa a 3; el mínimo predeterminado también pasa a 3. `PackageWithdrawn` continúa siendo un resultado terminal, nunca `ClientError`.
- Una instantánea completa válida sincroniza instalaciones por equipo y quita las desinstaladas; el evento solo de conexión no modifica esas filas. Verificación posterior: `dotnet test LANPartyHub.sln --no-restore`: 1113 unitarias, 341 integraciones, 3 E2E correctas y 1 E2E omitida (LAN real); `check-encoding.ps1`, `check-epics.ps1` y `git diff --check` correctos. La historia sigue en curso hasta revisar el diff actualizado y cerrar o diferir expresamente los hallazgos restantes.

## Spec Change Log

- 2026-11-10, revisión 1: el usuario eligió mostrar la fase **literal**, no traducida; `StateDisplayMap` continúa siendo la única fuente de textos de estado y resultado. Evitar una traducción española duplicada o un porcentaje inventado. KEEP: payload `InstallationProgressUpdated` exacto y proyección de fase real.
- 2026-11-10, revisión 1: el usuario autorizó **evento cliente→servidor solo de conexión** cuando falle SQLite. Evitar el estado conocido como malo (instantánea vacía que borra operaciones) y el otro estado malo (no comunicar `Disconnected`); ajustar arquitectura, contrato, protocolo y consumidores antes de completar. KEEP: `ClientStateChanged` con listas reales completas; no autenticación ni REST por equipo. Conservar todos los cambios sin commit, no revertir trabajo ajeno.

## Review Triage Log

Revisión 2 sobre el diff actualizado: se corrigieron durante esta pasada la pérdida de una instalación ya persistida ante un estado desconocido, la resurrección de descargas cerradas, la reclasificación de un tipo desconocido, el orden de estados al reanudar/confirmar, el doble aviso local de `Failed` y la espera a la red antes de iniciar los bytes en `DownloadCoordinator.StartAsync`. Hay pruebas focalizadas para las regresiones de proyección. **Pendiente antes de cerrar:** cubrir la retención/expiración con reloj controlado, la rama `Failed` de la UI y las notificaciones inicio/fin de descarga; decidir y verificar el comportamiento de `lastSeenAt` para el evento solo de conexión. Los comentarios de la revisión 1 que aún dicen «pendiente» deben revalidarse frente al código, no contarse como fallos vigentes ni declararse cerrados sin comprobación. Después de esta revisión, `dotnet test LANPartyHub.sln --no-restore` pasa: 1116 unitarias, 341 integraciones y 3 E2E; 1 E2E requiere una LAN real.

Revisión del diff de la rama contra `8f98cb24d27b02fd7fd4fe2349520c4db0510cae` (incluidos los archivos nuevos). Los identificadores B, E y V corresponden a contenido, casos límite y verificación, respectivamente. «Pendiente» significa que el hallazgo requiere corrección o decisión antes de fusionar; esta revisión **no revierte el trabajo sin commit**.

| ID | Veredicto / ruta | Evidencia y decisión |
| --- | --- | --- |
| B01 | medium / intent_gap, resuelto por el usuario | `ActiveInstallations.PhaseTextOf` emite `InstallPhase.ToString()` en inglés; el usuario confirmó que ese identificador literal es la fase visible. |
| B02 | false / rechazar | Los separadores y etiquetas de `ClientsViewModel` son composición de UI, no textos de estado ni resultado; AD-25 no obliga a ponerlos en `StateDisplayMap`. |
| B03 | high / bad_spec, corregido | `CoordinatorProgressSink` encola las fases y `FlushAsync` las publica EN ORDEN; la prueba `InstallationPhasePublicationRaceTests` comprueba que `Extracting`, `AwaitingConfirmation`, `ApplyingManifest` y `Completed` llegan al servidor. |
| B04 | medium / patch, corregido | La publicación al servidor pasa por `PublishPhaseAsync`, que NO vuelve a emitir `ProgressChanged`; la prueba `The_view_receives_the_progress_once_per_phase` lo fija y `InstallationFailurePhasePublicationTests` cubre el camino de fallo. |
| B05 | false / rechazar | AD-33 exige persistir el resultado de una intención si existe, no exige rehidratar `recentOutcomes[]` tras reiniciar el servidor: esa lista es explícitamente memoria de proyección. |
| B06 | false / rechazar | `ListClients` lee errores ya registrados; la producción de nuevos `CLIENT_ERROR` pertenece a la 9.3, no a la 9.2. |
| B07 | false / rechazar | `OperationOutcome` es una enumeración compartida exhaustiva; la 9.2 solo exige emitir `PackageWithdrawn`, no producir aquí `Succeeded`/`Failed`. |
| B08 | medium / bad_spec, corregido | `_outcomes` tiene tope por cliente (`MaxOutcomesPerClient`), caducidad (`OutcomeExpiry`) y lectura por equipo; `ClientOperationsProjectionRetentionTests` lo fija con reloj controlado. |
| B09 | medium / bad_spec, corregido | `_operations` caduca (`OperationExpiry`) y los recuerdos de terminación también (`TerminationRetention`), con poda en cada publicación; lo fijan las pruebas de retención. |
| B10 | medium / bad_spec, corregido | La instantánea conserva el estado que declaró el PROGRESO (`HasProgress`) y solo lo sustituye cuando no hubo progreso; `A_resumed_download_uses_the_latest_real_progress_state` lo fija. |
| B11 | medium / bad_spec, corregido | El tipo de operación se compara por IDENTIDAD con `download`/`installation` y un tipo desconocido conserva el que ya tenía o se rechaza; no se reclasifica como descarga. |
| B12 | false / rechazar | Los parámetros opcionales de `ClientRegistrationView` construyen listas vacías explícitas en JSON; la vista REST conserva sus diez propiedades exactas, verificadas en integración. |
| B13 | medium / patch, corregido | `ClientRegistrationViewTests` fija las cuatro listas nuevas y sus valores por omisión. |
| B14 | medium / patch, corregido | La prueba de integración crea dos intenciones `DOWNLOAD_INTENT` reales, publica el resultado y comprueba en SQLite que solo la intención propia queda con `outcome`. |
| B15 | medium / patch, corregido | `ReportClientStateTests` publica un estado de instalación desconocido y comprueba que se rechaza SIN borrar la instalación ya proyectada. |
| B16 | high / intent_gap, corregido con la decisión del usuario | El evento `ClientConnectionChanged` existe en `Shared`, lo publica el cliente cuando falla la lectura local y el servidor solo actualiza `connectionState` de la fila registrada. |
| B17 | false / rechazar | Tras desconectar, `LastConnectionState` conserva el último valor real; no hay prueba de que publicar ese valor declare «Desconectado» mientras la conexión ya esté verificada: el registro la actualiza. |
| B18 | medium / bad_spec, corregido | Las celdas no envuelven, recortan con elipsis y llevan el texto COMPLETO como tooltip, así que el recorte no pierde el dato. |
| B19 | low / rechazar | El detalle repite código/mensaje además de causa/paso; es redundancia de presentación, no pérdida de información ni regresión relevante. |
| B20 | low / rechazar | `ListClients` hace dos consultas SQLite por cliente, pero cumple un GET HTTP y el requisito de no pedir por equipo; falta evidencia de un límite de escala que justifique nuevos puertos de lote. |
| B21 | medium / bad_spec, corregido | `OpenClientStateStore` devuelve un ALQUILER (`ClientStateStoreLease`) que dispone el contexto al final de la sesión. |
| B22 | medium / patch, corregido | El publicador de reserva que crea `OpenRegistration` lo dispone la propia composición (`ownedPublisher`) y el `finally` de `Program` dispone la del registro. |
| B23 | maybe-false / defer | `DisposeAsync().GetAwaiter().GetResult()` bloquea en salida; demostrar que la continuación captura el hilo UI antes de calificarlo de deadlock. Patrón PREEXISTENTE en el cierre del cliente, fuera del cambio de esta historia. |
| B24 | low / rechazar | `_stateNotifier` se asigna bajo lock y luego se lee como referencia atómica; no se ha demostrado una carrera observable en el flujo de inicialización único del `Program`. |
| B25 | false / rechazar | La clave mojibake de 9.2 ya existía antes de este diff y el seguimiento se actualizó por los segmentos numéricos; no es un cambio introducido en esta revisión. |
| B26 | false / rechazar | `.build-lite/plans/9-2.md` es un artefacto local ignorado deliberadamente; la spec y la arquitectura versionadas contienen las decisiones de implementación. |
| B27 | false / rechazar | El `Review Triage Log` estaba vacío porque aún no se había procesado la revisión; no constituye defecto de producción. |
| B28 | false / rechazar | El AC2 del épico fija la forma de `ClientStateChanged`; la ampliación de AD-18 es de `ClientRegistrationView` y no contradice esa forma. |
| B29 | medium / bad_spec, corregido | El protocolo sube a 3, el mínimo predeterminado también, y AD-18/AD-29 fijan la ventana de compatibilidad; la prueba HTTP comprueba que la versión anterior recibe 426. |
| B30 | low / patch, corregido | Las firmas editadas ya no dejan la llave pegada a la línea (formato verificado con `dotnet build` sin errores). |
| E01 | high / patch, corregido | `BeginTrackedOperationAsync` libera el tracker si la publicación se cancela antes de entrar al `try/finally`; `A_cancelled_declaration_leaves_no_operation_in_course` lo fija. |
| E02 | medium / bad_spec, corregido | El cierre se publica con un token VIVO, así que una operación cancelada también llega a soltar su vista en el servidor; `A_cancelled_operation_releases_the_operation_on_the_server` lo fija. |
| E03 | medium / patch, corregido | `ReportTransfer` rechaza un `DownloadState` numérico desconocido sin lanzar excepción y sin tocar las demás operaciones; la prueba de estado desconocido lo fija. |
| E04 | medium / patch, corregido | `ReportClientState` trata las listas `null` de una instantánea malformada como listas vacías y salta las filas nulas; `Null_lists_in_a_malformed_snapshot_do_not_fail_the_publication` lo fija. |
| E05 | low / rechazar | Un `operationId` vacío en progreso produce un error de invocación, pero procede de un cliente no autenticado y el servidor no guarda datos corruptos; no se ha demostrado reintento infinito. |
| E06 | low / rechazar | Un `operationId` vacío en resultado falla explícitamente; no se ha demostrado corrupción ni reintento del cliente legítimo. |
| E07 | medium / patch, corregido | `IsDownload`/`IsInstallation` toleran un `OperationType` nulo o vacío y la fila se compone sin romperse. |
| E08 | low / patch, corregido | `ActiveDownloadsText` muestra el total declarado aunque falten los bytes transferidos. |
| E09 | medium / bad_spec, corregido | Duplicado de B08: los resultados caducan y la lectura recorre solo los del equipo. |
| E10 | false / rechazar | El tracker de reparación no estaba incluido en la spec de proyección de operaciones de instalación de esta historia; ampliarlo a reparaciones requiere historia/decisión propia. |
| E11 | medium / bad_spec, corregido | Duplicado de B18: las celdas recortan con elipsis y llevan el texto completo como tooltip. |
| E12 | medium / bad_spec, corregido | Duplicado de B21: el contexto del store se dispone con el alquiler al final de la sesión. |
| E13 | medium / bad_spec, corregido | `ReportPhase` consulta el recuerdo de terminación, que sobrevive a la caducidad y al tope del resultado; `A_late_phase_does_not_resurrect_a_terminated_operation` y las pruebas de retención lo fijan. |
| E14 | medium / patch, corregido | La validación del estado ocurre ANTES de declarar la operación como reportada, así que una declaración inválida no conserva viva una operación antigua. |
| E15 | low / patch, corregido | El comentario que llamaba reservadas a las columnas ya pobladas se eliminó. |
| E16 | medium / bad_spec, corregido | Una instantánea completa válida quita las instalaciones ausentes SOLO de ese cliente; el test de integración comprueba que no afecta al otro cliente y que el evento solo de conexión no borra nada. |
| E17 | medium / intent_gap, resuelto por el usuario | Duplicado de B01: fase inglesa desde `InstallPhase.ToString()`; se mantiene literal por decisión expresa. |
| E18 | medium / patch, corregido | Duplicado de B13: `ClientRegistrationViewTests` fija las cuatro listas nuevas. |
| E19 | false / rechazar | Una lectura SQLite correcta que devuelve cero filas es un estado vacío válido, necesario para reflejar una desinstalación; la ruta de fallo captura la excepción y emite únicamente `ClientConnectionChanged`, sin instantánea vacía. |
| V01 | high / bad_spec, corregido | `The_table_shows_the_real_games_bytes_and_installation_phase` fija el texto real de las tres celdas principales. |
| V02 | medium / bad_spec, corregido | La prueba de integración inserta una fila real en `CLIENT_ERROR` y comprueba que el GET devuelve código, causa y siguiente paso del error de ese cliente. |
| V03 | medium / patch, corregido | Duplicado de B14: la intención propia queda con `outcome` y la ajena sigue vacía. |
| V04 | medium / patch, corregido | Duplicado de E03: el estado de descarga desconocido se rechaza sin afectar a las demás operaciones. |
| V05 | high / intent_gap, corregido con la decisión del usuario | Duplicado de B16: el evento de conexión comunica el estado sin borrar la proyección; lo fijan `ClientConnectionChangedTests` y la prueba de integración. |
| V06 | medium / bad_spec, corregido | `DownloadComposition` recibe el publicador COMPARTIDO y solo abre el suyo cuando no hay ninguno; `Program` le pasa el mismo publicador del registro. |
| V07 | low / patch, corregido | El vocabulario del tipo de operación vive en `Shared` (`OperationTypeNames`), así que cliente y servidor comparten el MISMO nombre y no hay una fuente de verdad duplicada (R-MVP-7). |
| V08 | false / rechazar | `The_organizer_reads_all_the_clients_with_one_get...` ejerce un GET y verifica ambos clientes; no hay cliente HTTP de consulta por equipo en esa ruta. |

## Verification

**Commands:**
- `uv run tools/build-lite.py --story 9.2` — MiniMax M3 implementa/repara, verifica, abre PR y fusiona solo con puertas verdes.
- `powershell -NoProfile -File tools/check-encoding.ps1` y `powershell -NoProfile -File tools/check-epics.ps1` — sin errores de codificación ni trazabilidad.

**Resultados hasta revisión:** `dotnet build LANPartyHub.sln` pasa; 1093 unitarias, 315 integraciones sin `ServerStartupTests`, 21 `ServerStartupTests` aisladas y 3 E2E (1 omitida por requerir LAN real) pasan. La suite conjunta de integración falla intermitentemente en dos pruebas de puertos de `ServerStartupTests` cuando se ejecuta en paralelo; ambas pasan aisladas. `check-encoding.ps1`, `check-epics.ps1` y `git diff --check` pasan. Aún no hay PR ni merge.

**Resolución de la carrera de integración:** `ServerStartupTests` pertenece ahora a una colección xUnit exclusiva (`DisableParallelization = true`), porque sus aserciones comparan sockets del proceso entero. No se alteraron las aserciones ni el paralelismo del resto de colecciones. Tras el cambio, `ServerStartupTests` pasa 21/21, la integración conjunta pasa 336/336 y `dotnet test LANPartyHub.sln --no-build` pasa 1093 unitarias, 336 integraciones y 3 E2E (1 escenario LAN real omitido). Guardas de codificación y backlog en verde. La revisión de la 9.2 continúa; este resultado no implica PR ni merge.

**Cierre de la revisión (revisión 3):** las tres capas de revisión se ejecutaron sobre el diff actualizado y sus hallazgos se corrigieron o se rechazaron con evidencia en el `Review Triage Log`. Cambios de esta pasada: publicación de la fase `Failed` en los rechazos tempranos de la instalación; poda de los recuerdos de terminación caducados; eliminación del resolvedor de estado muerto; guarda de `Enum.IsDefined` al leer el estado terminal del cliente; y unificación del vocabulario del tipo de operación en `Shared` (`OperationTypeNames`). Pruebas añadidas: retención y expiración con reloj controlado, fase `Failed` sin doble aviso, notificaciones de inicio y fin de descarga, límite inferior de protocolo (426) y conservación de `lastSeenAt` en el evento solo de conexión.

**Verificación final:** `dotnet build LANPartyHub.sln --no-restore` compila sin errores; `dotnet test LANPartyHub.sln --no-restore` pasa 1137 unitarias, 344 integraciones y 3 E2E, con 1 E2E omitida porque exige una LAN real; `powershell -NoProfile -File tools/check-encoding.ps1`, `powershell -NoProfile -File tools/check-epics.ps1` y `git diff --check` pasan. La 9.2 queda cerrada en cuanto a implementación, revisión y verificación; la apertura del PR y la fusión siguen siendo un paso aparte del flujo.
