# Revisión — Lente de veracidad tecnológica

- **Lente:** `finalize_reviewers[0]` — «Verify every committed decision was web-researched or reality-checked rather than asserted from training data…»
- **Objetivo:** `ARCHITECTURE-SPINE.md`
- **Fecha:** 2026-10-26
- **Modo:** headless, ejecución secuencial

## Veredicto

**CUMPLE CON UNA OBSERVACIÓN.** Todas las versiones fijadas se verificaron en la web el 2026-10-26 y son correctas. La espina acierta en las dos decisiones donde lo fácil era equivocarse: **Avalonia 12** (no 11) y **MonoTorrent sin fijar**. Una observación menor sobre `Microsoft.Data.Sqlite`.

---

## Verificación por tecnología

| Named tech en la espina | Versión fijada | Verificación web | Estado |
| --- | --- | --- | --- |
| .NET SDK / runtime | 10.0.13 LTS | Publicado 2025-11-11; soporte hasta 2028-11-14; 10.0.13 servicing release 2026-10-13 | ✅ Correcto |
| ASP.NET Core (API + SignalR) | 10.0.13 | `What's new in ASP.NET Core in .NET 10`; SignalR `Client.Core` 10.0.x; .NET 10 es el LTS actual | ✅ Correcto |
| EF Core | 10.0.x | EF Core 10 es la versión estable alineada con .NET 10; EF 11 en pre-release con breaking changes | ✅ Correcto |
| Microsoft.Data.Sqlite | 10.0.13 | NuGet muestra 10.0.13 como estable actual | ✅ Correcto |
| SQLite | 3.x | Motor embebido estándar | ✅ Correcto |
| Avalonia | 12.1.4 | **Avalonia 12.0.0 publicado 2026-04-07** con breaking changes vs 11.x; `12.0 Preview 1` (2026-02-18); NuGet actual 12.1.4 | ✅ Correcto, y la decisión correcta |
| xUnit | 3.x | xUnit v3 es la rama estable actual | ✅ Correcto |
| MonoTorrent | **sin fijar** | Estable 3.0.2 (2024-08); `release-v3.9.0-alpha-0000` en desarrollo (2026) | ✅ **Acierta**: fijar alpha sería incorrecto |

## Observaciones

### T-1 — `Avalonia 12` es la elección correcta y la espina lo justifica (BAJA, informativa)

La espina anota que «12.0.0 publicado 2026-04-07, con cambios incompatibles respecto a 11.x». Es exactamente la razón por la que un plan maestro que dice simplemente «Avalonia UI» podría carried a **11.x** y quedarse con una base de código de una major anterior, o peor, migrar a mitad de proyecto. La espina fija **12.1.4** y marca el salto de major. **Correcto, sin cambios.**

*Nota de rigor:* el salto de major de Avalonia significa que `Avalonia.ReactiveUI`, `Avalonia.Diagnostics` y los paquetes de terceros que lo consumen deben recompilarse contra 12.x. La espina no nombra paquetes de terceros de Avalonia — para el MVP no los necesita. **No es un hallazgo.**

### T-2 — `Microsoft.Data.Sqlite` tiene un issue abierto de versionado de vulnerabilidades del SQLite embebido (MEDIA, informativa)

El issue `dotnet/efcore#38257` («SQLite vulnerabilities») señala que `Microsoft.Data.Sqlite` arrastra dependencias con vulnerabilidades conocidas de SQLite y pide alinear las versiones de SQLite. Efecto práctico para esta arquitectura: **la versión del SQLite embebido que entra con `Microsoft.Data.Sqlite` no es necesariamente la última**, y la espina escribe `SQLite 3.x` sin fijar.

- **Impacto en LAN Party Hub:** bajo y aceptado. El SQLite embebido no está expuesto a la red en ningún momento; el único vector es un paquete malicioso que,*como ya se valida por firma antes de escribir en disco* (AD-2, AD-8), sería difícil de explotar. La consecuencia de un SQLite con CVE es un fallo al procesar una base de datos manipulada, no una ejecución remota en la LAN.
- **Corrección propuesta (no bloqueante):** anotar en la espina que `SQLite 3.x` significa «la versión embebida que trae `Microsoft.Data.Sqlite` en el runtime fijado», y que si el informe de dependencias de una fase posterior señala CVE con impacto en la LAN, se evalúa actualizar el runtime o fijar `SQLitePCLRaw` por separado. Esto evita que un constructor posterior asuma que puede elegir la versión de SQLite libremente.
- **No requiere decisión de usuario.** Es una aclaración de la semilla.

### T-3 — `MonoTorrent` sin fijar: la decisión de no fijar es la correcta, pero falta un criterio de fijaje (MEDIA)

La espina dice «se fija solo si el spike P2P valida la biblioteca». Correcto. Pero un constructor posterior que lea solo la espina puede interpretar «sin fijar» como «cualquiera», y empezar el MVP **añadiendo** `BitTorrentTransferEngine` porque la sección Stack menciona MonoTorrent.

- **Corrección propuesta:** hacer explícito en la fila de Stack que la **ausencia de versión fijada significa «no es dependencia del MVP»**, y que el proyecto del MVP **no referencia MonoTorrent en absoluto**. Esto refuerza AD-4 y evita el acoplamiento prematuro que la propia espina quiere impedir.

### T-4 — No hay ninguna afirmación de versionado sin verificar (cumple)

Repasadas todas las afirmaciones de la espina que podrían serTraining-data disfrazado:

| Afirmación | Verificación |
| --- | --- |
| «`.NET 10.0.13 LTS` (soporte hasta 2028-11-14)» | ✅ verificado |
| «`Avalonia 12.1.4`» | ✅ verificado (NuGet) |
| «`MonoTorrent 3.0.2` estable (2024-08); alphas 3.9.x» | ✅ verificado |
| «`.NET 10` es el LTS actual» (implícito en elegir 10 sobre 11) | ✅ verificado: .NET 11 es STS en pre-release hasta nov-2026 |
| «mDNS `RFC 6762/6763`» | ✅ son los RFC correctos (DNS-SD y mDNS) |
| «Ed25519» | ✅ existe y está soportado por `System.Security.Cryptography` en .NET 10 (`Ed25519` está en .NET 10; en .NET 8/9 no lo estaba de forma estable — **verificar la API concreta al implementar**) |
| «UUID v7» | ✅ soportado por `Guid.CreateVersion7()` en .NET 9+ |
| «`Microsoft.Extensions.Logging` JSON» | ✅ `AddJsonConsole` existe |
| «`WebApplicationFactory`» | ✅ `Microsoft.AspNetCore.Mvc.Testing` |

**Una reserva sobre Ed25519:** `System.Security.Cryptography` incorporó `Ed25519` en **.NET 10** como API estable (en .NET 9 estaba disponible tras una flag de vista previa). Dado que la espina fija .NET 10, **la elección es correcta y más segura** que la alternativa: `NSec`/`BouncyCastle` o `libsodium` no hacen falta. Conviene que el constructor lo sepa, porque es un punto donde una unidad puede asumir que Ed25519 «no existe en .NET» y añadir una dependencia criptográfica externa innecesaria.

- **Corrección propuesta (informativa):** añadir una nota en la espina de que Ed25519 está disponible en la BCL de .NET 10 sin dependencia criptográfica externa.

### T-5 — Riesgo residual: `Avalonia 12` y el modelo de componentes de la UX (BAJA, informativa)

La `EXPERIENCE.md` define un juego de componentes propio (`button-primary`, `card-game`, `panel-connection`, `status-badge`, `progress-bar`, `data-table-row`, `toast-error`, `dialog-surface`) y una escala de espaciado propia, sin depender de un kit. Esto es **acertado** para Avalonia: no exige Fluent, Material o cualquier theme propietario, y de hecho reduce el riesgo de que la UX quede arrastrada por un kit visual ajeno a `DESIGN.md`. **Sin acción.**

---

## Resumen

| Severidad | Cantidad | IDs | Acción |
| --- | --- | --- | --- |
| Crítica | 0 | — | — |
| Alta | 0 | — | — |
| Media | 2 | T-2, T-3 | Aclaraciones de la semilla Stack (no bloqueantes) |
| Baja | 3 | T-1, T-4, T-5 | Informativas; T-4 lleva una mejora útil |

Todas las versiones fijadas son correctas y están verificadas. **No hay ninguna afirmación de stack sin verificar.**

---

## Bloque de resolución — T-1..T-5 (cierre residual de arquitectura)

- **Fecha del cierre:** 2026-10-26
- **Método:** cada hallazgo se reverificó contra el **texto de `ARCHITECTURE-SPINE.md`** (tabla `## Stack` y AD que la referencian), no contra este informe.
- **Verificación mecánica:** `lint_spine.py` sobre la espina actual → `ok: true`, `total_findings: 0`. En concreto, el chequeo `version_pin` no reporta ninguna fila de `## Stack` sin versión: `MonoTorrent` queda como **«sin fijar»**, que es un valor explícito y no un hueco.

### Estado por hallazgo

| ID | Severidad | Estado | Cierre en la espina | Evidencia (línea de la espina) |
| --- | --- | --- | --- | --- |
| **T-1** | BAJA (informativa) | **CERRADO — sin cambio requerido** | La espina ya fijaba **Avalonia 12.1.4** y ya anotaba «12.0.0 publicado 2026-04-07, con cambios incompatibles respecto a 11.x». Este informe **confirmó** que esa era la decisión correcta; no hay nada que corregir. | `## Stack` L344 |
| **T-2** | MEDIA (informativa) | **CERRADO — diferido a `Deferred`** | La fila `SQLite` pasa de «3.x» a **«embebido 3.x, incluido por `Microsoft.Data.Sqlite` 10.0.13; no se fija ni se selecciona una versión SQLite independiente»**. El constructor ya no puede asumir que elige la versión de SQLite. La condición de revisiting por CVE que el informe pedía **queda registrada en la tabla `Deferred`**, no como pendiente abierto. | `## Stack` L342; tabla `Deferred` L731 |
| **T-3** | MEDIA | **CERRADO** | «Sin fijar» deja de ser ambiguo: `AD-4` declara que `BitTorrentTransferEngine` **no existe** en el MVP, que el proyecto del MVP **no referencia MonoTorrent** y que el dominio de configuración **no expone selector de motor**. La fila de Stack repite que la versión se fija solo si el spike P2P valida la biblioteca. | `AD-4` L103–106; `## Stack` L346; `AD-21` L254; convención `Configuración y red` L330 |
| **T-4** | BAJA (informativa) | **CERRADO** | Se añade la fila `Ed25519`: **«API estable de `System.Security.Cryptography` en .NET 10; sin dependencia criptográfica externa»**. Una unidad ya no puede asumir que Ed25519 «no existe en .NET» y añadir `NSec`/`BouncyCastle`/`libsodium` innecesariamente. | `## Stack` L343 |
| **T-5** | BAJA (informativa) | **CERRADO — sin cambio requerido** | Riesgo residual sobre el modelo de componentes propio de `EXPERIENCE.md` frente a un kit visual. El informe lo calificación como **acertado** y «sin acción»; la espina no depende de ningún kit externo. Sin cambio. | `EXPERIENCE.md` (fuera de alcance de esta revisión) |

### Pendientes de este informe

**Ninguno.** T-2 está **resuelto**: la condición de revisiting por **CVE con impacto en la LAN** en el SQLite embebido **queda registrada en la tabla `Deferred`** de la espina (fila L731), con su condición de revisiting explícita.

**Ninguno de T-1..T-5 queda abierto como hallazgo.** El único punto que sobrevivía —la condición de revisiting por CVE en el SQLite embebido— ya está **diferido de forma explícita** en `Deferred` (L731), no como pendiente abierto.

### Notas de la verificación

- **T-3** era el único hallazgo con consecuencia de construcción real, y su cierre no se limita a la fila de Stack: `AD-4` cierra la superficie de configuración del motor, `AD-13` cierra la existencia del módulo `Tracking`, y los tres diagramas llevan la marca de ausencia. La fila de Stack es la **tercera** capa, no la única.
- **T-2** se cerró en la dirección que pedía el informe (aclara, no fija): fijar una versión de SQLite independiente habría contradicho la decisión de no añadir una dependencia de terceros. La condición de revisiting por CVE quedó **diferida de forma explícita** en la tabla `Deferred` (L731), no abandonada.
- **T-4** se cerró con una fila nueva en `## Stack`, no con una nota al pie: el linter trata una fila con versión no vacía como fijada, y `Ed25519` es una elección de stack que un constructor debe ver en la tabla, no en un párrafo.
- La verificación de versiones fijadas **no se ha re-ejecutado contra la web** en este cierre: la fecha de la espina y la de esta revisión son la misma (2026-10-26) y las versiones no han cambiado. Es un límite explícito de este cierre, no un supuesto silencioso.

### Referencias cruzadas

- Los cierres del checklist **H-1..H-6** están en `reviews/review-rubric-walker.md`.
- Los cierres adversariales **A-1..A-12** están en `reviews/review-adversarial-divergence.md`.
- La aclaración de la semilla Stack y los refinamientos de `AD-4` quedaron registrados en `.memlog.md`.

---

## Bloque de resolución — D-1 / D-2 / D-3 (cierre de la ronda de correcciones de Fase D)

- **Fecha del cierre:** 2026-10-26
- **Método:** cada hallazgo se reverificó contra el **texto de `ARCHITECTURE-SPINE.md`**, no contra este informe. Una línea citada significa que la regla existe en la espina; la cita es la evidencia del cierre.
- **Verificación mecánica:** `lint_spine.py` sobre la espina actual → `ok: true`, `total_findings: 0`, sin hallazgos por categoría ni severidad.

### Estado por hallazgo

| ID | Severidad | Estado | Cierre en la espina | AD que lo cierra | Evidencia (línea de la espina) |
| --- | --- | --- | --- | --- | --- |
| **D-1** | CRÍTICA | **RESUELTO** | El ancla de confianza **se fija out-of-band** (fingerprint por QR del organizador o por contraste visual en su pantalla) y **no** se obtiene del canal que debe autenticar (bootstrap circular). El servidor publica su fingerprint **solo para contrastarlo**, no para aceptarlo; sin fingerprint out-of-band el cliente no fija el ancla y no opera (tercer estado de conexión, ancla sin verificar). El **rechazo precede a persistir** en tres niveles distinguibles por `code` de `ErrorCatalog` (sustitución de ancla / firma inválida / `sha256` divergente), y un ancla ya fijada es **inmutable**, con la rotación en `Deferred`. | **AD-27** (refinamiento de **AD-2**, **AD-8**, **AD-12**) | AD-27 L342–367; AD-2 L86–98 (L91 bootstrap circular, L92–95 vías out-of-band, L96 sin fingerprint no se opera, L97 rechazo anterior a persistir, L98 ancla inmutable); AD-12 L182; convención `Data & formats` L396; diagrama de secuencia L520–524; tabla `Deferred` L814; tabla `Trazabilidad` L851; tabla «R5 y P2P» L874–888 |
| **D-2** | ALTA | **RESUELTO** | MVP-HTTP y PoC-P2P quedan **separados**: el MVP tiene **exactamente un** camino de descarga (`HttpRangeTransferEngine`); `BitTorrentTransferEngine`, el módulo `Tracking` y su endpoint `announce`, el registro de swarm, el seeding entre clientes, el mapa de fragmentos `chunkMap` (columna incluida) y el `chunkSizeBytes` obligatorio **no existen en el MVP**, y esa ausencia es el estado correcto. Ninguna regla del MVP remite a P2P: ni la tabla REST, ni la configuración, ni la topología de despliegue, ni el modelo de dominio. El carácter **bloqueante / no bloqueante** de cada spike es **normativo**: **S-2 es BLOQUEANTE**, **S-3 es NO BLOQUEANTE** (si S-3 no se ejecuta o fracasa, el MVP **no cambia**). | **AD-28** (refinamiento de **AD-21**, **AD-4**, **AD-13**, **AD-24**) | AD-28 L368–379; AD-21 L263–276 (L268 carácter normativo, L269–272 derivación por dependencia, L275 no se propaga por edición); AD-4 L109; AD-13; AD-24 L313–321; diagrama de contexto L444; diagrama de módulos L477; topología L760; tabla `Spikes` L795–796; convención `Data & formats` L396; tabla `Deferred` L814; tabla `Trazabilidad` L852; tabla «R5 y P2P» L874–888 |
| **D-3** | ALTA | **RESUELTO** | El evento **`ClientsSnapshotChanged` no existe** y se elimina del contrato: la vista de organización de clientes (FR-10) se obtiene **exclusivamente** por REST, `GET /api/v1/clients`, que devuelve `ClientRegistrationView[]` con la forma exacta de `Shared`. El consumidor es la **UI del propio servidor** (no hay receptor de hub declarado) y el modelo no admite autenticación de suscriptores, luego no existe una forma de proteger el push que no sea inventar autenticación. La actualización de la vista no se pierde: se refresca por REST cuando la UI lo pide y cuando el servidor detecta un cambio de conexión (evento **interno** del servidor, no de hub). | **AD-29** (refinamiento de **AD-18**, **AD-22**, **AD-5**) | AD-29 L380–390; AD-18 L243 (`ClientRegistrationView` como contrato único de `Shared`); AD-22 L301 (forma exacta de los payloads SignalR, sin `ClientsSnapshotChanged`); tabla REST L550; tabla de eventos SignalR L563; diagrama de secuencia L520–535; convención `Contratos API` L397; tabla `Capability → Architecture` L775; tabla `Trazabilidad` L853 |

### Pendientes de este informe

**Ninguno.** D-1, D-2 y D-3 quedan **resueltos**: cada uno tiene su AD propia (AD-27, AD-28, AD-29), su regla exigible, sus correcciones en cascada (tablas, diagramas, convenciones, `Deferred` y `Trazabilidad`) y su verificación mecánica con `lint_spine.py` en `ok: true` / `total_findings: 0`.

### Notas de la verificación

- **D-1** era el hallazgo con más superficie: no bastaba con decir «el ancla es la clave del servidor», sino fijar **cómo** llega, **quién** la compara, **qué** ocurre cuando no coincide y **qué se escribe antes** del rechazo. Las cuatro cosas están: el fingerprint es el dato transportado, la comparación la hace la persona, el rechazo es anterior a persistir en tres niveles con `code` distinto, y el ancla fijada es inmutable.
- **D-2** se cerró por **eliminación**, no por declaración: la forma más robusta de separar la PoC del MVP es que los artefactos P2P **no existan** en el MVP (módulo, endpoint, puerto, columna `chunkMap`, dependencia de `MonoTorrent`, vocabulario de peer). La ausencia del diagrama de módulos es ahora la prueba, y la nota al pie lo dice en texto para que no se lea como un hueco pendiente.
- **D-3** se cerró fijando **un único camino** del dato: `ClientRegistrationView` es un contrato de `Shared` **único**, consumido por la tabla REST y por la proyección del servidor. Un constructor ya no puede declarar un segundo tipo para el mismo concepto ni empujar el registro de clientes a cada equipo de la LAN.
- La **capa de verificación** no encontró ningún hallazgo de esta lente que quedara abierto tras el cierre; los cierres de D-1 y D-2 se reforzaron mutuamente (AD-27 fija la cadena de confianza y AD-28 le quita la condición de cierre al P2P: son las dos caras de «la seguridad del MVP no depende de la PoC»).

### Referencias cruzadas

- Los cierres del checklist **H-1..H-6** están en `reviews/review-rubric-walker.md`.
- Los cierres adversariales **A-1..A-12** están en `reviews/review-adversarial-divergence.md`.
- Las decisiones **AD-27, AD-28, AD-29** y el refinamiento de **AD-21** quedaron registrados en `.memlog.md`.
