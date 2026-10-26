# Revisión adversarial — Divergencia entre unidades

- **Lente:** `finalize_reviewers[1]` — «Attack the spine as an adversary: construct two units one level down that each obey every AD to the letter yet still build incompatibly — clashing shared-data shapes, two owners of one entity, conflicting state-mutation paths. Every pair you find is a hole to close with a new or tightened AD.»
- **Objetivo:** `ARCHITECTURE-SPINE.md`
- **Fecha:** 2026-10-26
- **Modo:** headless, ejecución secuencial (sin subagentes disponibles)

## Veredicto

**INADECUADO CON FALLOS REALES.** La espina cierra bien las fronteras de módulo, la raíz de confianza y el camino HTTP del MVP, pero deja **doce divergencias construibles** entre el servidor y el cliente: doce unidades que obedecen cada AD a la letra y aun así producen contratos incompatibles. Ninguna es un fork de producto — todas se cierran con una regla adicional o un AD nuevo, sin decisión del usuario. Ninguna invalida la espina; todas la endurecen.

Se evaluaron **12 pares** de unidades. **Los doce divergen** (A-1…A-12), cada uno cerrado con un AD o una aclaración de semilla.

---

## Pares evaluados

| # | Par de unidades | ¿Divergen? | Hallazgo |
| --- | --- | --- | --- |
| 1 | Cliente ↔ Cliente (dos implementaciones del mismo cliente) | **Sí** | A-8 |
| 2 | Servidor ↔ Servidor (dos.catálogos) | **Sí** | A-2 |
| 3 | Servidor ↔ Cliente (forma del error) | **Sí** | H-1 (ya reportado) |
| 4 | Servidor ↔ Cliente (vocabulario de estados) | **Sí** | H-2 (ya reportado) |
| 5 | Cliente ↔ Cliente (ruta del `.part`) | **Sí** | H-4 (ya reportado) |
| 6 | Servidor ↔ Cliente (presencia de `Tracking`) | **Sí** | H-3 (ya reportado) |
| 7 | Servidor ↔ Cliente (catálogo que retira un paquete en curso) | **Sí** | H-5 (ya reportado) |
| 8 | Servidor ↔ Cliente (dueño de `CLIENT_INSTALLATION`) | **Sí** | **A-1** |
| 9 | Servidor ↔ Cliente (firma: qué se firma exactamente) | **Sí** | **A-2** |
| 10 | Servidor ↔ Cliente (`chunkSizeBytes` vs troceado HTTP) | **Sí** | **A-3** |
| 11 | Servidor ↔ Cliente (identidad del `clientId`) | **Sí** | **A-4** |
| 12 | Servidor ↔ Cliente (orden de escritura del manifiesto vs contenido) | **Sí** | **A-5** |

Los hallazgos **H-1…H-6** (corte `review-rubric-walker.md`) y **T-2…T-4** (corte `review-technology-currency.md`) se re-evalúan aquí desde el ángulo adversarial: se confirman como construibles y se cierran con los mismos AD nuevos que este informe propone, para no duplicar reglas.

---

## Hallazgos nuevos (adversariales)

### A-1 — `CLIENT_INSTALLATION` tiene dos dueños posibles (CRÍTICA)

- **Ubicación:** `AD-10`, `AD-11`, diagrama ER (entidades `CLIENT_INSTALLATION` y `CLIENT`).
- **Construcción del par:**
  - **Unidad A (servidor)** lee `AD-10` — «el cliente es la única fuente de verdad del estado local de una operación» — y concluye que `CLIENT_INSTALLATION` es una **tabla del servidor** que refleja lo que el cliente le reporta por SignalR (`ClientStateChanged` lleva `installedGames`). La implementa en la base de datos del servidor, con su propio migrador EF Core.
  - **Unidad B (cliente)** lee `AD-11` — «un archivo por rol: el servidor, uno; el cliente, uno» — y concluye que `CLIENT_INSTALLATION` es una **tabla del cliente** (`clientId` + `gameId` + `installedVersion` + `validationState` + `installPath`), porque el cliente es quien la ejecuta y quien valida (`AD-19`: «valida la instalación antes de lanzar»).
- **Por qué cada una obedece la espina:** `AD-11` prohíbe explícitamente que «el esquema de cada archivo es propiedad de su rol», pero **no dice qué entidades viven en qué lado**. El ERD no indica el propietario. `AD-10` habla de "estado local de una operación", no de "estado de una instalación", así que la lectura A también cabe.
- **Divergencia resultante:**
  - El servidor persiste `installedGames` de forma derivada y la llama `CLIENT_INSTALLATION`; el cliente la persiste como estado propio. Dos definiciones del mismo nombre, dos formas de datos, dos migrating.
  - Peor: `installPath` es una ruta **del cliente**. En el servidor es la ruta del equipo remoto — inútil o engañoso. Y `validationState` (`Installed`, `Repairable`, `Failed`) es un **estado de máquina de `InstallationState`**, que `AD-18` declara **único** y compartido. Si el servidor lo persiste como cadena propia, violaría `AD-18`; si lo persiste como el enum compartido, entonces el servidor **es dueño de un estado de máquina del cliente**, contradiciendo `AD-10`.
  - El `ClientStateChanged` de la tabla SignalR lleva `installedGames` como contenido **mínimo**, sin forma declarada. Un servidor podría esperar `gameId` + `version`; otro, `gameId` + `version` + `validationState` + `path`. No hay contrato.
- **Corrección propuesta:**
  1. Declarar el **dueño de cada entidad**: `CLIENT_INSTALLATION` vive **en el cliente** (es estado local, como la regla de AD-10 para operaciones). El servidor guarda **una proyección de solo lectura**, con otro nombre, en su propio archivo (p. ej. `CLIENT_INSTALL_VIEW`), alimentada por SignalR.
  2. Fijar la **forma mínima** del evento `ClientStateChanged` (campos exactos, no "contenido mínimo"), o declarar explícitamente que es un DTO de `Shared` con esos campos.
  3. Regla de no-colisión: el servidor **no** persiste `InstallationState` de otro cliente; si lo guarda, es como **cadena de texto de presentación**, no como el enum de `Shared`.

### A-2 — AD-2 y AD-7 juntos no dicen **qué** se firma exactamente (CRÍTICA)

- **Ubicación:** `AD-2` («firma Ed25519 sobre el manifiesto canónico del paquete»), `AD-7` (`PackageDescriptor` con `manifestRef`, `signature`, `keyId`; `InstallationManifest` con `schemaVersion`, `signature`, `keyId`), `AD-8` («firmado por la misma raíz de confianza»).
- **Construcción del par:**
  - **Unidad A (servidor)** firma el **manifiesto de instalación** (`InstallationManifest`) — coherente con AD-8 — y guarda la firma en `PackageDescriptor.signature` porque `PackageDescriptor` tiene campo `signature`. Publica `GET /api/v1/packages/{id}` con la firma dentro del descriptor, y sirve el manifiesto **sin** firma propia.
  - **Unidad B (cliente)** firma el **descriptor del paquete** — el "manifiesto canónico del paquete" — y sirve el manifiesto de instalación **también** firmado, como artefacto aparte (AD-20 dice: «sirve el manifiesto de instalación como artefacto aparte, firmado»).
- **Por qué cada una obedece la espina:** AD-2 usa la expresión «manifiesto canónico del **paquete**», que se lee como descriptor; AD-7 y AD-8 usan «manifiesto» para `InstallationManifest`. La espina usa **la misma palabra para dos objetos** y nunca dice cuál recibe la firma de AD-2.
- **Divergencia resultante:** el cliente de la Unidad B **rechaza** el paquete del servidor A («sin firma» → no confiable → no instala, por AD-8). El cliente de la Unidad A **rechaza** el manifiesto de B (firma sobre un objeto que AD-2 no define). **Los dos sistemas son mutuamente incompatibles** y ambos cumplen la espina. Además el ERD muestra `signature` **tanto** en `PACKAGE_DESCRIPTOR` **como** en `INSTALLATION_MANIFEST`, con `keyId` duplicado — la espina no dice cuál es la firma autoritativa.
- **Corrección propuesta:** un AD que fije la **cadena de confianza exacta**:
  1. Se firma **un** objeto: el manifiesto de instalación (`InstallationManifest`) en su forma canónica.
  2. El `PackageDescriptor` **no** lleva `signature`; lleva `manifestRef` (la URL/identificador del manifiesto firmado) y `sha256` (integridad del contenido del paquete).
  3. La firma cubre el manifiesto canónico; el manifiesto canónico incluye `packageId`, `packageVersion`, `sizeBytes` y `sha256` del paquete. Así, la firma **autentica** los metadatos y el `sha256` **verifica** los bytes, y la correspondencia entre manifiesto y paquete queda firmada, no supuesta.
  4. `keyId` vive **solo** en el sobre firmado.
  5. Regla de simetría explícita: la clave pública del ancla verifica **la misma** firma en servidor y cliente.

### A-3 — `chunkSizeBytes` en `PackageDescriptor` no tiene dueño ni semántica (ALTA)

- **Ubicación:** `AD-7` (`PackageDescriptor.chunkSizeBytes`), `AD-11` («registro de fragmentos/offset»), `AD-20` («el troceado interno en trozos no es necesario para HTTP porque Range basta; el troceado **sí** es requisito del camino BitTorrent»), ERD (`DOWNLOAD_PROGRESS.chunkMap`).
- **Construcción del par:**
  - **Unidad A (servidor)** lee `AD-20` — el troceado no es necesario para HTTP — y deja `chunkSizeBytes` en **0** o sin valor, y sirve el contenido con `Range` libre. Persiste `chunkSizeBytes = 0`.
  - **Unidad B (cliente)** lee `AD-11` — «el progreso de descarga se persiste como **registro de fragmentos/offset**» — y persiste una tabla de fragmentos con tamaño de fragmento fijo, leyendo `chunkSizeBytes` del descriptor para calcular los rangos. Si recibe `0`, divide por cero o cae a un valor por defecto inventado.
- **Por qué cada una obedece la espina:** `AD-20` dice explícitamente que el troceado no es necesario para HTTP, y `AD-11` exige un registro de fragmentos. La espina nunca dice que `chunkSizeBytes` es **opcional en el camino HTTP**, ni que el registro de fragmentos del cliente es un **mapa de fragmentos del protocolo BitTorrent** o un **offset acumulado**.
- **Divergencia resultante:** un cliente lee `chunkSizeBytes = 0` y rompe; o un cliente persiste un mapa de fragmentos que el servidor no puede leer para nada; o dos clientes persisten mapas con granularidades distintas y `DOWNLOAD_PROGRESS.chunkMap` deja de ser interpretable. Además `chunkMap` (columna del ERD) no está en `AD-7` ni en `AD-11` — es una decisión de forma que nadie posee.
- **Corrección propuesta:** un AD de **granularidad de progreso**:
  1. En el camino HTTP del MVP, el progreso persistido es un **offset acumulado** (`bytesPersisted`), no un mapa de fragmentos; el `chunkSizeBytes` del descriptor es **informativo y opcional** en el camino HTTP (cero o ausente).
  2. El mapa de fragmentos es el formato **exclusivo** del camino BitTorrent y no existe hasta que S-3 cierre.
  3. `chunkSizeBytes` es un campo **obligatorio y > 0** cuando el motor de transferencia activo es BitTorrent.
  4. La columna `chunkMap` del ERD se sustituye por `bytesPersisted` (offset) más, opcionalmente, `chunkMap` solo en el camino BitTorrent; y el formato del mapa de fragmentos se fija al cerrar S-3, no antes.

### A-4 — `clientId` no tiene regla de emisión ni de persistencia (ALTA)

- **Ubicación:** ERD (`CLIENT.clientId PK`, `CLIENT.machineName`), `AD-15` (correlación por `clientId` propagada a través de la frontera servidor-cliente), `AD-17` (el cliente guarda su estado en `%LOCALAPPDATA%\LanPartyHub`), convención de datos («Identificadores opacos tipo UUID v7»).
- **Construcción del par:**
  - **Unidad A (cliente)** genera un `clientId` **nuevo** en cada arranque (UUID v7) y lo reporta. El servidor ve un cliente nuevo cada vez que el equipo reinicia.
  - **Unidad B (cliente)** genera un `clientId` **estable**, persistido en su base de datos local y reutilizado en cada arranque. El servidor ve el mismo equipo a lo largo del tiempo.
- **Por qué cada una obedece la espina:** la convención dice «identificadores opacos tipo UUID v7», lo que admite ambos comportamientos. `AD-15` habla de propagar `clientId`, no de su ciclo de vida. La columna `machineName` sugiere que el servidor podría deduplicar por ella, pero ninguna AD lo dice.
- **Divergencia resultante:** en la vista de organización (`GET /api/v1/clients`, panel de estado de clientes, FR-10), el servidor de la Unidad A muestra **equipos duplicados** — «EQUIPO-01» tres veces, con tres `clientId` distintos y cero descargas activas cada uno. El de la Unidad B muestra uno. Dos unidades que construyen la tabla de clientes **no son la misma tabla**. Con `machineName` presente y sin regla, un constructor puede deducir por nombre en un caso y por `clientId` en otro.
- **Corrección propuesta:** un AD breve de **identidad del cliente**:
  1. El `clientId` se genera **una vez** y se persiste en la base de datos local del cliente; sobrevive a reinicios; no se regenera.
  2. El servidor **no** deduplica ni agrupa por `machineName`; `machineName` es **informativo** (etiqueta visible), nunca clave.
  3. El servidor trata un `clientId` desconocido como un cliente nuevo con ese `clientId`.

### A-5 — El manifiesto se firma y se sirve **por separado**, pero el cliente no sabe si debe verificar antes o después de la descarga (ALTA)

- **Ubicación:** `AD-2` («la firma es Ed25519 sobre el manifiesto canónico»; el manifiesto firmado es la raíz de verificación), `AD-8` («la comprobación de firma precede a cualquier escritura en disco»), `AD-20` («sirve el manifiesto de instalación como artefacto aparte, firmado»), diagrama de secuencia (el manifiesto se pide **después** de `POST /api/v1/downloads` y **antes** de `GET /content`).
- **Construcción del par:**
  - **Unidad A (cliente)** sigue la tabla REST y el diagrama de secuencia al pie de la letra: `POST /downloads` → `GET /manifest` → `GET /content`. Escribe el `.part` en disco **mientras** descarga, y verifica la firma del manifiesto **al terminar**.
  - **Unidad B (cliente)** aplica `AD-8` al pie de la letra: verifica la firma **antes de cualquier escritura en disco**, con lo que necesita el manifiesto **antes** de crear el archivo parcial.
- **Por qué cada una obedece la espina:** el diagrama de secuencia es **una** ilustración y la espina no lo declara normativo; `AD-8` y `AD-20` son ambos normativos y no se reconcilian. AD-20 pone el manifiesto «aparte», lo que sugiere pre-descarga; el diagrama lo pone en medio del flujo.
- **Divergencia resultante:** A y B divergen en **cuándo se crea el archivo parcial** y, sobre todo, en el **tamaño del hueco de confianza**: A puede tener un `.part` de un paquete no confiable en disco (el archivo existe aunque no se instale); B no tiene nada. Además A necesita el manifiesto para validar el `sha256`, así que sin manifiesto no puede validar, pero sí ha escrito.
- **Corrección propuesta:** un AD de **orden de operaciones** que fije la secuencia normativa:
  1. `GET /packages/{id}/manifest` → verificar firma y `schemaVersion` → **solo entonces** crear el archivo parcial y descargar.
  2. Descargar con `Range` → calcular `sha256` → comparar con el del manifiesto firmado → si no coincide, **descartar** el `.part` y marcar la operación como fallida (no reparable por reanudación).
  3. Regla derivada: `POST /api/v1/downloads` **no** inicia la escritura en disco; la escritura empieza tras la verificación del manifiesto.
  4. El diagrama de secuencia se marca como ilustrativo o se reordena para que no contradiga la norma.

---

## Re-evaluación adversarial de los hallazgos ya reportados

| ID | Origen | ¿Construible como divergencia? | Veredicto |
| --- | --- | --- | --- |
| H-1 | Rubric walker | **Sí** — dos unidades declaran la forma del error por separado; el cliente deserializa mal `cause`/`nextStep`. | **Real.** Se cierra con A-6 (ver abajo). |
| H-2 | Rubric walker | **Sí** — dos tablas estado→texto, una por unidad; y tres badges de UX sin estado de dominio. | **Real.** Se cierra con A-7. |
| H-3 | Rubric walker | **Sí** — dos dominios de configuración del puerto `ITransferEngine`. | **Real.** Se cierra con A-8. |
| H-4 | Rubric walker | **Sí** — el `.part` dentro de la raíz de instalación o en `%TEMP%`. | **Real.** Se cierra con A-9. |
| H-5 | Rubric walker | **Sí** — un cliente cancela la operación en curso y otro la deja terminar. | **Real.** Se cierra con A-10. |
| H-6 | Rubric walker | No es divergencia; es precisión de coste de la lista de pruebas. | **Real pero trivial.** Se cierra con A-11. |
| T-2 | Tecnología | No es divergencia entre unidades; es aclaración de la semilla del stack. | **Real como aclaración**, no bloqueante. Se cierra con A-12. |
| T-3 | Tecnología | **Sí**, en su forma: un constructor lee «MonoTorrent» en el Stack y añade `BitTorrentTransferEngine` al MVP, rompiendo H-3. | **Real.** Se cierra con A-8. |
| T-4 | Tecnología | No es divergencia; es nota informativa sobre Ed25519 en la BCL de .NET 10. | **Real como nota.** Se cierra con A-12. |

### A-6 — El tipo de error de API no es un tipo compartido (cierra H-1)

- **Ubicación:** convención `Data & formats`; `AD-15`; `AD-18`.
- **Par construido:** el servidor declara `ApiError` en `Server/Contracts`; el cliente declara `ErrorResponse` en `Client/Contracts` con `{ code, message }` y **sin** `cause`/`nextStep`. Ambos cumplen la convención (que está en prosa), pero el cliente pierde el siguiente paso. `EXPERIENCE.md` exige las tres partes («qué falló, por qué, qué hacer ahora») → el cliente **no puede** cumplir la UX.
- **Corrección propuesta:** `AD-18` enumera el contenido de `Shared` **de forma explícita** e incluye el DTO de error; `AD-15` declara que ese DTO es **el único** tipo de error de API del sistema, con `cause` y `nextStep` **obligatorios** (no opcionales), y que `code` es un identificador estable del catálogo de errores, nunca el texto principal.

### A-7 — El vocabulario de estados de la UI no está enlazado al dominio (cierra H-2)

- **Ubicación:** `AD-10`; `EXPERIENCE.md` `status-badge` (sincronizando, disponible, descargando, pausado, validado, instalado, instalado con versión desactualizada, en ejecución, error).
- **Par construido:** el servidor tiene su propia tabla estado→texto y el cliente la suya; divergen en el tercer estado. Y tres insignias de UX —«sincronizando», «disponible», «instalado con versión desactualizada»— y una cuarta —«error»— no son estados de máquina: ninguna `AD` las gobierna, así que cada unidad decide su regla de derivación.
- **Corrección propuesta:** un AD de **mapeo de estados**:
  1. El texto de UI de cada estado vive en **una sola tabla compartida**.
  2. «sincronizando», «disponible» e «instalado con versión desactualizada» son **estados de vista derivados**, no estados de máquina, con su regla de derivación declarada.
  3. «error» no es un estado propio: es la proyección de los estados terminales de fallo.

### A-8 — La condición de `Tracking` y de la segunda implementación del puerto es inequívoca (cierra H-3 y T-3)

- **Par construido:** una unidad construye `ITransferEngine` con dos implementaciones y un selector de configuración; otra con una sola y un `NotSupportedException`. Los dominios de configuración divergen.
- **Corrección propuesta:** `AD-4` — el puerto se entrega en el MVP con **una** implementación (`HttpRangeTransferEngine`); `AD-13` — el módulo `Tracking` está **ausente** hasta que S-3 cierre; y la fila de Stack deja explícito que «sin fijar» significa **«no es dependencia del MVP»** y que el proyecto del MVP **no referencia MonoTorrent en absoluto**.

### A-9 — Los directorios con nombre y propósito (cierra H-4)

- **Par construido:** el `.part` en la raíz de instalación (contamina la instalación validada) o en `%TEMP%` (se pierde al reiniciar → SM-2 roto).
- **Corrección propuesta:** un AD de **layout del cliente en disco** que nombre: raíz de instalación (confinada por AD-9), directorio de descargas parciales (**fuera** de la raíz de instalación, ruta fija y persistente), caché de manifiestos, base de datos del cliente.

### A-10 — Consistencia del catálogo frente a operaciones en curso (cierra H-5)

- **Par construido:** un cliente cancela la descarga de un paquete que el catálogo retiró; otro la deja terminar. Divergen.
- **Corrección propuesta:** un AD de **consistencia del catálogo**: el cliente aplica un snapshot nuevo **sin cancelar operaciones en curso**; una operación en curso que referencia un paquete retirado **termina** y se marca con un resultado explícito de «paquete retirado del catálogo»; la instalación existente de un juego retirado **no se borra**.

### A-11 — Precisión de coste de la lista de pruebas (cierra H-6)

- **Par construido:** ninguno; es clasificación, no divergencia.
- **Corrección propuesta:** mover «extracción fuera del directorio autorizado» de la lista E2E a la de unitarias/integración de `AD-16`; dejar en E2E solo lo que necesita LAN real.

### A-12 — Aclaraciones de semilla del stack (cierra T-2 y T-4)

- **Par construido:** ninguno directo; sí habilitan el de H-3/T-3 (A-8).
- **Corrección propuesta:** en la tabla de Stack, aclarar que `SQLite 3.x` es «la versión embebida que trae `Microsoft.Data.Sqlite` en el runtime fijado» (con la condición de revisiting si un informe de dependencias señala CVE con impacto en la LAN) y que Ed25519 está disponible en la BCL de .NET 10 sin dependencia criptográfica externa.

---

## Cobertura de la lente adversarial

Cada dimensión donde dos unidades podrían divergir se evaluó:

| Dimensión | ¿Cubierta por la espina? | Nota |
| --- | --- | --- |
| Forma de los DTOs de API | **No** | Solo prosa en convención → A-6 |
| Forma de los eventos de SignalR | **No** | «contenido mínimo» → A-1 |
| Forma del error | **No** | → A-6 |
| Vocabulario de estados y su texto | **No** | → A-7 |
| Forma del manifiesto firmado | **Parcial** | AD-2 y AD-7 usan «manifiesto» para dos objetos → A-2 |
| Forma de la granularidad de progreso | **No** | → A-3 |
| Identidad del cliente | **No** | → A-4 |
| Dueño de cada entidad persistida | **No** | → A-1 |
| Ubicación en disco del cliente | **Parcial** | AD-9 solo confina la instalación → A-9 |
| Orden de verificación vs escritura | **No** | AD-8 vs AD-20 vs diagrama → A-5 |
| Consistencia del catálogo | **No** | → A-10 |
| Superficie de configuración del motor | **Parcial** | → A-8 |
| Versión de protocolo | **Sí** | AD-6 fija el mecanismo; la tabla de valores queda en Deferred correctamente |
| Rutas de API | **Sí** | Tabla REST es explícita |
| Eventos de dominio | **Sí** | Convenciones de nombres |
| Puertos entre capas | **Sí** | AD-1, AD-4, AD-18 |
| Confines de escritura | **Sí** | AD-9 (una vez fijado el layout, A-9) |

---

## Bloque de resolución — A-1..A-12 (cierre residual de arquitectura)

- **Fecha del cierre:** 2026-10-26
- **Método:** cada hallazgo se reverificó contra el **texto de `ARCHITECTURE-SPINE.md`** (AD que lo cierran, tabla `## Stack` y tabla `Deferred`), no contra este informe. Una línea citada significa que la regla existe en la espina; la cita es la evidencia del cierre.
- **Verificación mecánica:** `lint_spine.py` sobre la espina actual → `ok: true`, `total_findings: 0`, sin hallazgos por categoría ni severidad.

### Estado por hallazgo

| ID | Severidad | Cierre en la espina | AD que lo cierra | Evidencia (línea de la espina) |
| --- | --- | --- | --- | --- |
| **A-1** | CRÍTICA | **CERRADO** | **AD-22** | Cada entidad persistida tiene **un** dueño; la proyección del servidor es una entidad **distinta** y **no comparte nombre** (`ClientInstallationView` ≠ `ClientInstallation`). El servidor no persiste el enum `InstallationState` del cliente, sino una cadena de presentación (`validationStateText`); `installPath` no sale del cliente; la forma de `ClientStateChanged` es **exacta** y vive en `Shared`. | AD-22 L256–279; convención `State & cross-cutting` L327 |
| **A-2** | CRÍTICA | **CERRADO** | **AD-2** | Se firma **un** objeto: el **manifiesto de instalación canónico** (`InstallationManifest`). `PackageDescriptor` **no** transporta firma: transporta `manifestRef` y `sha256`; `keyId` vive **solo** en el sobre firmado. El manifiesto firmado incluye `packageId`, `packageVersion`, `sizeBytes` y `sha256`, de modo que la firma autentica los metadatos y el `sha256` verifica los bytes. | AD-2 L84–90; AD-7 L125, L126; AD-8 L135; AD-18 L232, L233; convención `Data & formats` L325 |
| **A-3** | ALTA | **CERRADO** | **AD-24** | **Un motor, un formato.** En el camino HTTP (único motor del MVP) el progreso persistido es un **offset acumulado** (`bytesPersisted`) y **no hay mapa de fragmentos**; `chunkSizeBytes` es **informativo y opcional**. `chunkMap` solo existe en el camino BitTorrent, y su formato lo fija S-3. | AD-24 L291–298; AD-7 L128; AD-11 L167; convención `Data & formats` L325; ERD cliente L601–606, L610 |
| **A-4** | ALTA | **CERRADO** | **AD-23** | El `clientId` lo emite el cliente **una sola vez** y lo **persiste** (`ClientIdentity`); no se regenera al arrancar ni al reinstalar. El servidor **no** deduplica ni agrupa por `machineName` (es **informativo**, nunca clave) y trata un `clientId` desconocido como un cliente nuevo con ese `clientId`. | AD-23 L281–289; AD-22 L268, L274; ERD cliente L585–587, L576 |
| **A-5** | ALTA | **CERRADO** | **AD-8** | **Orden normativo de una instalación:** (1) obtener el manifiesto firmado; (2) verificar firma y `schemaVersion`; (3) **solo entonces** crear el directorio y el archivo parcial; (4) descargar; (5) comparar el `sha256`; (6) si no coincide, **descartar** el parcial y marcar la operación fallida, no reparable por reanudación. Ningún byte se escribe antes del paso 2 y `POST /api/v1/downloads` **no** inicia la escritura en disco. | AD-8 L135–136; AD-20 L248; AD-2 L88 |
| **A-6** | MEDIA (cierra H-1) | **CERRADO** | **AD-15, AD-18** | Existe **UN SOLO** tipo de error de API en todo el sistema: `ApiError`, declarado en `Shared`, con `cause` y `nextStep` **obligatorios** (no opcionales) y `code` estable del `ErrorCatalog`. La enumeración del contenido de `Shared` es exhaustiva y aplica «un concepto, un tipo, un lugar». | AD-15 L194–197; AD-18 L216–236, L220, L221, L229; convención `Data & formats` L325 |
| **A-7** | MEDIA (cierra H-2) | **CERRADO** | **AD-10, AD-25** | `StateDisplayMap` es la **única** tabla de microcopia, en `Shared`; «sincronizando», «disponible» y «desactualizado» quedan como **estados de vista derivados** con regla de derivación fija; «error» es la **proyección de los estados terminales de fallo**, no un estado de máquina. | AD-10 L154–161; AD-25 L300–307; AD-18 L224, L225; convención `State & cross-cutting` L327 |
| **A-8** | MEDIA (cierra H-3 y T-3) | **CERRADO** | **AD-4, AD-13** | El puerto `ITransferEngine` se entrega en el MVP con **una sola implementación** (`HttpRangeTransferEngine`); `BitTorrentTransferEngine` **no existe** en el MVP, el proyecto **no referencia MonoTorrent** y la configuración **no expone** selector de motor. El módulo `Tracking` queda **ausente hasta que S-3 cierre**. La fila de Stack deja explícito que «sin fijar» significa «no es dependencia del MVP». | AD-4 L103–106; AD-13 L179–181; AD-21 L254; `## Stack` L346; convención `Configuración y red` L330; diagramas L387, L401, L407 y topología L683 |
| **A-9** | MEDIA (cierra H-4) | **CERRADO** | **AD-9** | **Layout en disco del cliente con directorio y propósito nombrados:** raíz de instalación confinada, `downloads/` de ruta fija y persistente **fuera** de la raíz, `manifests/` de caché y `state.sqlite`. Los `.part` viven **solo** en `downloads/`, nunca en `%TEMP%` ni en la raíz de instalación. | AD-9 L143–148; AD-11 L167 |
| **A-10** | MEDIA (cierra H-5) | **CERRADO** | **AD-26** | Un snapshot nuevo **no cancela** operaciones en curso; una operación cuyo paquete fue retirado **termina** con resultado explícito `Paquete retirado del catálogo`, **distinto del fallo**; la instalación existente **no se borra**; el `catalogVersion` del servidor es **monótono creciente**. | AD-26 L309–318; tabla `Deferred` (delta) L740 |
| **A-11** | BAJA (cierra H-6) | **CERRADO** | **AD-16** | La clasificación de `AD-16` mueve «extracción fuera del directorio autorizado» a **unitarias/integración** y deja en E2E en LAN real solo lo que necesita LAN. La clasificación no elimina ningún caso de la checklist: fija el **nivel mínimo** de detección. | AD-16 L203 |
| **A-12** | BAJA (cierra T-2 y T-4) | **CERRADO** | **`## Stack`, `Deferred`** | La fila `SQLite` pasa a **«embebido 3.x, incluido por `Microsoft.Data.Sqlite` 10.0.13; no se fija ni se selecciona una versión SQLite independiente»**, con la condición de revisiting por CVE **diferida de forma explícita** en la tabla `Deferred`. Se añade la fila `Ed25519`: **«API estable de `System.Security.Cryptography` en .NET 10; sin dependencia criptográfica externa»**. | `## Stack` L342, L343; tabla `Deferred` L731 |

### Pendientes de este informe

**Ninguno.** Los doce hallazgos A-1..A-12 están cerrados y verificados contra la espina.

### Notas de la verificación

- **A-1** y **A-2** eran los hallazgos con más superficie: cada uno exigía **dos** reglas a la vez (dueño + forma del evento; objeto firmado + forma del descriptor). Ambas están, y `AD-22` cierra además la puerta a la duplicación con la regla «un concepto, un tipo, un lugar».
- **A-8** era el hallazgo con más riesgo de cierre parcial: `AD-4` y `AD-13` ya declaraban la condición, pero los **diagramas** la contradecían (listaban `Tracking` sin marca de condicional). Los tres diagramas (módulos del servidor, dependencias, topología de despliegue) llevan ahora la marca de ausencia, y la nota al pie del diagrama de módulos lo dice explícitamente: `Tracking` aparece **solo** para dejar constancia de que no forma parte del MVP.
- **A-10** interactúa con `Deferred`: la sincronización por delta sigue aplazada, pero el mecanismo de aplicación de un snapshot completo ya no admite dos lecturas, así que el aplazamiento no deja dos unidades divergir.
- **A-12** se cerró en la dirección que pedía el informe de tecnología (aclara, no fija): fijar una versión de SQLite independiente habría contradicho la decisión de no añadir una dependencia de terceros, y la condición de revisiting quedó **diferida**, no abandonada.
- La **capa de verificación** no encontró ningún hallazgo de esta lente que quedara abierto tras el cierre; los cierres de `A-1` y `A-2` se reforzaron mutuamente (la proyección del servidor y la entidad de origen no comparten nombre, y el manifiesto firmado es el **único** objeto firmado).

### Referencias cruzadas

- Los cierres del checklist **H-1..H-6** están en `reviews/review-rubric-walker.md`.
- Las aclaraciones de semilla **T-2..T-4** están en `reviews/review-technology-currency.md`.
- Las decisiones **AD-22..AD-26** y los refinamientos de **AD-2, AD-4, AD-7, AD-8, AD-9, AD-10, AD-13, AD-15, AD-16, AD-18, AD-20** quedaron registrados en `.memlog.md`.

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
- Las aclaraciones de semilla **T-1..T-5** están en `reviews/review-technology-currency.md`.
- Las decisiones **AD-27, AD-28, AD-29** y el refinamiento de **AD-21** quedaron registrados en `.memlog.md`.

---

## Resumen

| Severidad | Cantidad | IDs |
| --- | --- | --- |
| Crítica | 2 | A-1, A-2 |
| Alta | 3 | A-3, A-4, A-5 |
| Media | 5 | A-6 (H-1), A-7 (H-2), A-8 (H-3+T-3), A-9 (H-4), A-10 (H-5) |
| Baja | 2 | A-11 (H-6), A-12 (T-2+T-4) |

**Todos los hallazgos son aplicables sin decisión de usuario**: son cierres de contrato, no forks de producto. Ninguno exige información que la espina no pueda derivar de sus propias fuentes (Brief, PRD, UX y `docs/plan.md`).

**Ningún hallazgo invalida el camino principal del MVP** (servidor → cliente por HTTP). Los hallazgos A-1, A-2, A-4 y A-5 sí habrían producido **incompatibilidad real** entre un servidor y un cliente construidos por unidades independientes; son de severidad alta o crítica pese a no bloquear la ruta HTTP, porque el producto ES un servidor y un cliente que deben hablar.
