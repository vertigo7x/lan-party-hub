# Revisión adversarial — Divergencia construible en el backlog de épicas e historias

- **Lente:** adversarial (constructibilidad de divergencia). Dos unidades que obedecen **cada AC a la
  letra** y aun así construyen sistemas incompatibles. Cada par divergente es un hueco a cerrar con
  un AC adicional o una regla exigible.
- **Objetivo:** `epics.md`
- **Fecha:** 2026-10-26
- **Modo:** headless, ejecución secuencial (sin subagentes disponibles)
- **Base:** `bde708d97766dfc607528abb9d3a28bc82487f0e` (arquitectura con Checkpoint D aprobado)
- **Estado:** revisión re-ejecutada sobre el backlog de cierre de Fase E (13 épicas / 47 historias /
  261 AC). Los hallazgos se mantienen como registro del análisis y quedan **cerrados** en
  `epics.md`; el bloque de resolución está al final de este documento. El hallazgo **D-5** se
  **reabre** con la incorporación de **AD-30** y vuelve a quedar **resuelto** con la regla exigible
  correspondiente (ver «Reapertura y resolución de D-5»). La propagación de **AD-31** (derivación
  determinista del nombre efectivo) y **AD-32** (contrato único del registro) añade los criterios AC11..AC14
  de la Story 9.1 y la restricción heredada **R-MVP-10** (ver «Propagación de AD-31 y AD-32»).

## Veredicto

**ADECUADO CON FALLOS REALES, YA CERRADOS.** El backlog respeta las diez restricciones heredadas
(R-MVP-1..10) y no introduce ninguna capacidad fuera del MVP. En su primera redacción dejaba
**cinco divergencias construibles** y **dos huecos de cobertura** que un implementador podía
materializar sin salirse de ninguna AC.

Ninguna de las divergencias es un fork de producto: todas se cierran con AC o reglas exigibles
dentro del contrato ya aprobado por el PRD, la UX y la arquitectura. Ninguna invalida el backlog;
todas lo endurecen.

Se evaluaron **doce pares** de unidades. **Cinco divergen** (D-1..D-5), cada uno cerrado con un AC
o una regla exigible. Se detectaron además **dos huecos de cobertura** (H-1, H-2).

> **Conteo arquitectónico final (cierre documental de Fase E + cierre por AD-30/AD-31/AD-32):** 13
> épicas, 47 historias, 261 criterios de aceptación. Conteo confirmado mecánicamente sobre
> `epics.md`: 13 encabezados `## Epic N`, 47 encabezados `### Story N.M` y 261 ocurrencias de
> `**Given**` / `**When**` / `**Then**` (261/261/261). El verificador `tools/check-epics.ps1` devuelve
> `stories: 47`, `acceptance_criteria: 261`, `ar_definitions: 25`, `ar_table_rows: 25`,
> `ok: true`, `total_findings: 0`.

---

## Pares evaluados

| # | Par de unidades | ¿Divergen? | Hallazgo |
| --- | --- | --- | --- |
| 1 | Cliente A ↔ Cliente B —Cancellation del `DownloadState` | **Sí** | **D-1** |
| 2 | Cliente A ↔ Cliente B — qué se borra al cancelar | **Sí** | **D-1** |
| 3 | Servidor ↔ Cliente — `DownloadOperation` como intención vs. estado | **Sí** | (cubierto: Story 5.2 AC1) |
| 4 | Servidor ↔ Cliente — vista de clientes por evento vs. REST | **Sí** | (cubierto: Story 9.1 AC4, R-MVP-9) |
| 5 | Servidor ↔ Cliente — `validationState` enum vs. cadena de presentación | **Sí** | (cubierto: Story 9.2 AC3) |
| 6 | Servidor ↔ Cliente — `chunkSizeBytes` obligatorio vs. opcional | **Sí** | (cubierto: Story 2.2 AC2, Story 5.3 AC1) |
| 7 | Servidor ↔ Cliente — orden de verificación del manifiesto | **Sí** | (cubierto: Story 7.1 AC1/AC3) |
| 8 | Servidor ↔ Cliente — anclaje del ancla (canal del servidor vs. out-of-band) | **Sí** | (cubierto: Story 4.4 AC3/AC4) |
| 9 | Cliente A ↔ Cliente B — `clientId` regenerado vs. persistido | **Sí** | (cubierto: Story 1.3 AC2) |
| 10 | Servidor ↔ Cliente — formas de progreso (`bytesPersisted` vs. mapa) | **Sí** | (cubierto: Story 5.3 AC1) |
| 11 | Servidor ↔ Cliente — `ApiError` con `nextStep` opcional | **Sí** | (cubierto: AR-4, Story 6.2 AC3) |
| 12 | Cliente A ↔ Cliente B — vocabulario de estados (`error` como estado) | **Sí** | (cubierto: Story 3.3 AC4, AR-5) |

Los hallazgos **M-1..M-3, B-1 y B-2** del corte de cobertura (`review-coverage-traceability.md`) se
re-evalúan aquí desde el ángulo adversarial: se confirman como construibles y se cierran en el mismo
lote, sin duplicar reglas.

---

## Hallazgos nuevos (adversariales)

### D-1 — `Cancelled` es un estado terminal sin ninguna historia que lo produzca — **ALTA**

- **Ubicación:** `### Story 5.2` (única historia con cancelación); `### Story 5.3` (persistencia);
  `### Story 7.1` (máquina de estados de instalación).
- **Construcción del par:**
  - **Unidad A (cliente)** lee la Story 5.2 y ve que la cancelación se pide con confirmación
    «Se borrarán los 3,2 GB descargados». Concluye que cancelar **borra el parcial** y **libera el
    espacio**, y que la operación queda en un estado terminal `Cancelled` de `DownloadState`.
  - **Unidad B (cliente)** lee la misma Story 5.2 y la Story 5.3. Concluye que cancelar **solo
    abandona la transferencia en curso** y deja el parcial y el registro de progreso en disco, para
    poder retomarlo más tarde; la operación queda en `Cancelled` igualmente.
- **Por qué cada una obedece el backlog:** el backlog **nombra `Cancelled` en ninguna AC**. La
  arquitectura (sección «Estados de descarga e instalación» de `ARCHITECTURE-SPINE.md`, fijada por
  **AD-10**) sí lo declara como estado terminal, y AD-10 exige que las transiciones sean explícitas y con estado terminal por operación
  (AR-15), pero ninguna AC del backlog dice qué produce `Cancelled`, qué ocurre con el archivo
  parcial, ni si el registro de progreso persistido se borra. El texto de la confirmación —«Se
  borrarán los 3,2 GB descargados»— es **microcopia de UX** (UX-DR-22), no una regla de dominio:
  una unidad puede honourarla y otra puede considerar que el texto nombra lo que *se perdería* si se
  cancelara, sin que la pérdida sea efectiva.
- **Divergencia resultante:** dos clientes con la **misma** microcopia que se comportan distinto.
  El de la Unidad A borra el parcial; el de la Unidad B lo conserva y `bytesPersisted` sigue
  apuntando a 3,2 GB. Cuando el usuario vuelve a «Descargar», la Unidad B **reutiliza** un offset
  obsoleto (3,2 GB) que **no** corresponde al archivo, y produce un `.part` corrupto o un `sha256`
  divergente en una descarga que «nunca se había interrumpido». El usuario ve «Se borrarán los 3,2
  GB» y luego un «Paquete dañado: el hash no coincide»: el texto miente sobre el estado real.
  Peor: la Unidad B ha borrado el progreso persistido pero no el parcial, dejando la
  reconciliación de AR-11 (progresso efectivo = intersección de lo persistido y lo que el archivo
  contiene) en un estado imposible de explicar.
- **Corrección propuesta:** añadir a Story 5.2 un AC que fije la semántica de cancelación en el
  dominio: `Cancelled` es un estado terminal de `DownloadState`; al cancelar se **borra** el
  archivo parcial de `downloads/` y **se reinicia** el registro de progreso persistido
  (`bytesPersisted` vuelve a cero o el registro se elimina); la operación **no** es reintentable por
  reanudación, a diferencia de `Failed` (AD-10, AR-15, AR-11).

### D-2 — `Paquete retirado del catálogo` no está en `StateDisplayMap` ni en `ErrorCatalog`: resultado sin fuente de verdad — **ALTA**

- **Ubicación:** `### Story 3.1` AC4; `### Story 5.2`; `### Story 2.3` AC2.
- **Construcción del par:**
  - **Unidad A (servidor)** retira un paquete del catálogo mientras un cliente lo descarga. Marca la
    operación del cliente con el resultado `Paquete retirado del catálogo` y **lo escribe en el
    `ClientError`** del servidor, con un `code` nuevo del `ErrorCatalog` para ese caso.
  - **Unidad B (servidor)** hace lo mismo pero **no** lo registra como error: lo trata como un
    resultado de operación normal (no es un fallo) y lo emite solo en `ClientStateChanged` dentro de
    `activeOperations[].state`.
- **Por qué cada una obedece el backlog:** AD-26 (y AR-16) fijan que el resultado es
  «explícitamente `Paquete retirado del catálogo`, **distinto del fallo**». El backlog lo dice en
  Story 3.1 AC4. Pero **R-MVP-7 y AR-8** exigen que «un texto que no está en la tabla no existe»:
  la microcopia sale **solo** de `StateDisplayMap` y `ErrorCatalog`, y **AR-5** declara que
  `StateDisplayMap` traduce «cada valor de `DownloadState` e `InstallationState`» — y
  `Paquete retirado del catálogo` **no es un valor de ninguno de los dos**: es un *resultado* de
  operación, no un estado de máquina. Ninguna AC exige añadir su entrada a la tabla correspondiente,
  ni dice en cuál de las dos vive.
- **Divergencia resultante:** dos servidores, con el mismo `ErrorCatalog` y el mismo
  `StateDisplayMap`, muestran textos **distintos** para el mismo caso, o el mismo texto desde
  tablas **distintas**. El cliente recibe un `state` que no encuentra en `StateDisplayMap` y cae al
  «texto genérico del catálogo» (Story 6.2 AC4): el organizador ve un error genérico y el jugador
  ve un estado genérico, cuando el caso tiene un texto propio y definido por el PRD. La
  consecuencia práctica es grave: la retirada de un paquete —una acción normal del organizador—
  se presenta al jugador como **fallo**, contradiciendo «distinto del fallo».
- **Corrección propuesta:** añadir a Story 3.1 un AC que fije **dónde** vive la microcopia de
  `Paquete retirado del catálogo` (en `StateDisplayMap` como resultado de operación, o en
  `ErrorCatalog` con `code` propio) y que fije que **no** se presenta como error, y a Story 9.2 un
  AC que exija que el servidor no lo persista como `ClientError` por ser un resultado distinto del
  fallo.

### D-3 — La certificación de `Installed`/`Repairable` no tiene criterio verificable: dos clientes certifican distinto — **MEDIA**

- **Ubicación:** `### Story 7.1` AC4; `### Story 7.3` AC1; `### Story 8.2` AC1/AC2; `### Story 10.3` AC1.
- **Construcción del par:**
  - **Unidad A (cliente)** valida la instalación comprobando que **todos** los archivos del
    manifiesto están presentes y que el `verify` del manifiesto pasa. Marca `Installed`.
  - **Unidad B (cliente)** valida la instalación comprobando solo que el **ejecutable** existe y que
    el directorio de instalación no está vacío. Marca `Installed`.
- **Por qué cada una obedece el backlog:** la arquitectura fija las **operaciones** del manifiesto
  (`verify` entre ellas, AD-7) y exige que la instalación se valide antes de lanzar (AR-22), pero
  **no declara qué condición decide el estado `Installed`**. El backlog tampoco: Story 7.1 AC4 dice
  solo «la operación pasa por `ValidatingInstallation` y termina en `Installed` o en `Repairable`»,
  y Story 7.3 AC1 dice que el cliente «detecta una instalación existente y evita repetir operaciones
  ya completadas **cuando la instalación es válida**». El término «instalación válida» no tiene
  definición en ninguna AC, y `Repairable` tampoco: no hay regla que diga cuándo una instalación
  inválida es reparable y cuándo no lo es, pese a que Story 8.2 AC1/AC2 y Story 10.3 AC1 bifurcan
  el comportamiento según esa distinción.
- **Divergencia resultante:** el mismo directorio en el mismo equipo se marca `Installed` en un
  cliente y `Repairable` en otro. Consecuencias divergentes y **visibles**: «Jugar» habilitada en un
  caso y bloqueada en el otro; «Reparar» como primaria en el segundo; y el servidor recibe una
  `validationStateText` distinta para el mismo equipo. El caso «instalación incompleta» del PRD §10
  se clasifica como detectable en unitarias (AD-16), pero **no hay ninguna prueba unitaria posible
  sin un criterio de certificación**: la divergencia se vuelve un bug de producción, no un fallo de
  pruebas.
- **Corrección propuesta:** añadir a Story 7.1 un AC que fije la **condición de certificación**
  (`Installed` = todas las operaciones `verify` del manifiesto pasan sobre el árbol instalado) y la
  **condición de reparabilidad** (`Repairable` = la instalación es reparable desde el paquete ya
  validado; no reparable = el paquete ya no está disponible o el `sha256` no valida), de modo que
  Story 8.2, Story 10.3 y el servidor puedan ramificar sobre el mismo criterio.

### D-4 — El motor de transferencia activo no se propaga al servidor: el reparto equitativo es indecible — **MEDIA**

- **Ubicación:** `### Story 5.2` AC6 (reparto equitativo); `### Story 1.2` AC2 (sin selector de
  motor); `### Story 5.1` AC2 (una sola implementación).
- **Construcción del par:**
  - **Unidad A (servidor)** aplica el reparto equitativo por **cliente** porque en el MVP hay un solo
    motor (AD-14: «sus límites por cliente se aplican igual en el MVP HTTP»).
  - **Unidad B (servidor)** implementa el reparto por **motor de transferencia**, porque
    `DownloadOperation` declara su `transferEngine` (AD-24) y AD-14 dice «la prioridad de LAN se
    implementa limitando el ancho de banda **por peer**».
- **Por qué cada una obedece el backlog:** el backlog dice «el reparto del enlace es equitativo
  entre los clientes que descargan a la vez» (Story 5.2 AC6) pero **no dice la unidad de reparto**,
  y el término «cliente» aparece en el mismo documento que «*peer*» es vocabulario prohibido
  (R-MVP-6, Story 5.1 AC4). Además AR-24 dice «el reparto del enlace en el MVP HTTP es equitativo
  entre los clientes que descargan a la vez», pero **no dice cómo** se reparte (fair queuing por
  operación, por cliente, round-robin de conexiones, límite de ancho por cliente).
- **Divergencia resultante:** con 3 clientes y un paquete cada uno, la Unidad A reparte 1/3 del
  enlace a cada uno; la Unidad B reparte por motor y, con un solo motor activo, reparte por
  operación — que es lo mismo, salvo cuando un cliente abre **dos** descargas simultáneas (límite
  configurable, AD-14): entonces la Unidad B da la mitad del enlace al cliente con dos operaciones y
  la Unidad A le da un tercio. El resultado observable es un cliente con **más ancho por byte
  descargado** que otro, es decir, un reparto no equitativo entre clientes: exactamente lo que NFR-2
  y el AC de Story 5.2 AC6 piden.
- **Corrección propuesta:** precisar en Story 5.2 AC6 que el reparto equitativo es **por cliente**
  (no por operación ni por conexión), y que con un cliente que abre varias operaciones el ancho se
  reparte entre los clientes, no entre las operaciones.

### D-5 — `ClientRegistration` y `ClientIdentity` son dos nombres para identidades de rol distinto, pero solo uno se declara — **MEDIA — REABIERTO Y RESUELTO (AD-30)**

> **Estado:** **RESUELTO**. El hallazgo se reabre con la incorporación de la decisión de arquitectura
> **AD-30** (`machineName` es la etiqueta humana del equipo, única y validada por el servidor;
> `clientId` sigue siendo la clave) y se cierra con los criterios AC5..AC10 de la Story 9.1. La
> evidencia de cierre está en «Reapertura y resolución de D-5» al final de este documento.

- **Ubicación:** `### Story 9.1` AC5; `### Story 1.3` AC2; `### Story 9.2` AC3.
- **Construcción del par:**
  - **Unidad A (servidor)** persiste `ClientRegistration` con la clave primaria `clientId` y un
    `machineName` único por equipo (índice único), porque «el servidor no deduplica ni agrupa por
    `machineName`» (AR-14) se lee como «`machineName` es único por equipo».
  - **Unidad B (servidor)** persiste `ClientRegistration` **sin** unicidad alguna en `machineName`,
    porque AD-23 dice que `machineName` es «informativo, una etiqueta visible para el organizador,
    **nunca** una clave».
- **Por qué cada una obedece el backlog:** el backlog dice «el servidor **no** deduplica ni agrupa
  por `machineName`» (Story 9.1 AC5), pero **no prohíbe** un índice único sobre `machineName`, que
  no es deduplicación sino una restricción de unicidad — y su efecto es el de un dedupe: el segundo
  cliente con el mismo nombre de equipo **falla al registrarse**. En una LAN con dos equipos
  llamados `DESKTOP-ABC` (nombres por defecto de Windows en redes con el mismo prefijo de dominio),
  la Unidad A **rechaza** al segundo cliente, que queda invisible para el organizador: un fallo
  silencioso exactamente del tipo que FR-10 pretende evitar.
- **Divergencia resultante:** dos servidores con la misma arquitectura aceptan o rechazan al
  segundo cliente homónimo. En la Unidad A, ese cliente no aparece en la tabla, no descarga y no
  reporta estado, y **ningún AC del backlog lo detecta**: el cliente ve «conectado» y el organizador
  no lo ve.
- **Corrección propuesta:** añadir a Story 9.1 AC5 que `machineName` **no** tiene restricción de
  unicidad ni índice único, y que dos clientes con el mismo `machineName` pero distinto `clientId`
  coexisten en la tabla.

---

## Huecos de cobertura (no son divergencias, pero dejan superficie sin criterio)

### H-1 — AR-21 (estrategia de pruebas) y AR-25 (carácter normativo de los spikes) no los cita ninguna AC — **MEDIA**

- **Ubicación:** `### Additional Requirements` → AR-21, AR-25.
- **Evidencia:** la verificación mecánica da **AR-21: 1** mención y **AR-25: 1** mención — la
  definición. **Ninguna AC** las cita. AR-21 fija la estrategia de pruebas en tres niveles y
  declara que el caso «cliente compartiendo fragmentos con otros» **pertenece a la PoC P2P y sale
  del conjunto obligatorio del MVP**; AR-25 fija el carácter bloqueante/no bloqueante de cada spike
  como **normativo**.
- **Divergencia que previene:** AR-21 y AR-25 son requisitos exigibles sin ninguna historia que los
  verifique. Un implementador puede construir el MVP sin ninguna prueba E2E en LAN real (el nivel
  que AD-16 declara «escenario de aceptación del MVP») y sin Respectar la separación del caso P2P,
  y ninguna AC lo impide: la separación P2P está en AR-21 y en R-MVP-1/Story 5.1, pero la
  *clasificación de niveles de prueba* —que es lo que AR-21 añade— no está en ninguna AC.
- **Corrección propuesta:** añadir a Story 1.1 un AC con la clasificación de niveles de prueba
  (unitarias / integración / E2E en LAN real) y la exclusión explícita del caso P2P, y a la
  Story 12.1 un AC con el carácter normativo de S-2 (**BLOQUEANTE para el cierre**) como regla
  exigible, no descriptiva.

### H-2 — El `transferEngine` se persiste pero no hay AC que fije su **valor** en el MVP — **MEDIA**

- **Ubicación:** `### Story 1.3` AC3; `### Story 5.1` AC2; `### Story 5.3` AC1.
- **Evidencia:** AR-11 declara que «un registro cuyo `transferEngine` no exista en la versión activa se
  rechaza al arrancar», y Story 1.3 AC3 lo cita. Story 5.1 AC2 dice que el puerto se entrega con
  **una sola** implementación, `HttpRangeTransferEngine`. **Pero ninguna AC dice qué valor
  literal** se persiste en la columna `transferEngine` del cliente.
- **Divergencia que previene:** la Unidad A persiste `"Http"`; la Unidad B persiste
  `"HttpRangeTransferEngine"`; la Unidad C persiste el enum `TransferEngine.HttpRange`. El valor
  es una **cadena libre** salvo que se fije, y las tres construyen un cliente que **rechaza sus
  propios registros al arrancar** (Story 1.3 AC3) si el valor persistido no coincide con la
  enumeración de la versión activa. Es el peor modo de fallo posible para esta regla: el cliente
  borra (o rechaza) la descarga en curso de un usuario sin motivo visible.
- **Corrección propuesta:** fijar en Story 5.1 el **valor único** del identificador de motor
  persistido en el MVP, y en Story 5.3 un AC que exija que el `transferEngine` registrado en cada
  operación sea **exactamente** ese valor.

---

## Re-evaluación adversarial de los hallazgos ya reportados

| ID | Origen | ¿Construible como divergencia? | Veredicto |
| --- | --- | --- | --- |
| M-1 | Cobertura | **Sí** — un implementador construye «Jugar» con `IsEnabled = false`; en Avalonia eso **excluye el control del recorrido de foco** y el usuario de teclado no recibe ninguna señal de que la acción existe. La divergencia es entre la vista con teclado y la vista con ratón. | **Real.** Se cierra con AC de teclado en Story 8.2. |
| M-2 | Cobertura | **Sí** — dos unidades «cubren» NFR-2 de forma distinta: una Structural (límites configurables, reparto equitativo verificable) y otra por medición (cifras de S-4, spike **no bloqueante** que puede no ejecutarse nunca). La segunda admite un backlog que cierra sin medir nada. | **Real.** Se cierra separando verificabilidad de medición. |
| M-3 | Cobertura | **Sí** — un constructor lee «decide el formato del mapa de fragmentos» (Story 12.5 AC5) como requisito del MVP y añade la columna `chunkMap` al esquema del cliente, que es exactamente lo que AD-24 y AD-28 excluyen. | **Real.** Se cierra marcando el AC como evidencia de spike, no como requisito. |
| B-1 | Cobertura | **Sí** — el MVP puede cerrarse con todas las historias «verdes» y sin que nadie haya corrido el flujo completo en una LAN real: SM-1 y SM-5 son los criterios de éxito **primario** y **secundario** del PRD y ninguna historia los verifica. | **Real.** Se cierra con una historia de cierre E2E. |
| B-2 | Cobertura | **No** — Story 11.2 AC2 cubre la parte de «dato literal en forma exacta» de UX-DR-6 y Story 11.4 AC1 cubre la microcopia. Es una **nota de trazabilidad**, no un hueco. | **No es un defecto.** Se cierra con la cita explícita de UX-DR-6. |

---

## Cobertura de la lente adversarial

| Criterio de la lente | Estado |
| --- | --- |
| Pares cliente ↔ cliente divergentes | 5 evaluados, **3 divergentes** (D-1, D-3, D-5) |
| Pares servidor ↔ cliente divergentes | 5 evaluados, **2 divergentes** (D-2, D-4) |
| Pares servidor ↔ servidor divergentes | 2 evaluados, **0 divergentes** |
| Restricciones heredadas R-MVP-1..10 violadas por el backlog | **0** |
| Capacidades fuera del MVP introducidas por una AC | **0** |
| Alcance de Fase F (sprint planning, scaffold, código) en el backlog | **0** |

### Restricciones heredadas: verificación explícita

| Restricción | ¿Se respeta en el backlog? | Evidencia |
| --- | --- | --- |
| R-MVP-1 (un solo camino HTTP) | **Sí** | Story 5.1 AC1/AC2/AC3/AC4, Story 5.3 AC1 |
| R-MVP-2 (sin autenticación) | **Sí** | Story 4.3 AC1/AC2/AC3/AC4, R-MVP-2 citada en Story 4.3 |
| R-MVP-3 (ancla out-of-band, rechazo antes de persistir) | **Sí** | Story 4.4 AC2/AC3/AC4, Story 6.1 AC4, Story 7.1 AC3 |
| R-MVP-4 (solo modo portable) | **Sí** | Story 7.3 AC6, Story 10.2 AC3 |
| R-MVP-5 (spikes acotados, S-2 bloqueante) | **Sí** | Story 12.1..12.5, con el carácter declarado en cada historia |
| R-MVP-6 (vocabulario servidor/cliente) | **Sí** | Story 5.1 AC4 |
| R-MVP-7 (`Shared` solo contratos, tablas únicas) | **Sí** | Story 1.1 AC4/AC5, Story 3.3 AC4 |
| R-MVP-8 (microcopia solo de las tablas) | **Parcial** | **D-2**: `Paquete retirado del catálogo` no tiene tabla asignada |
| R-MVP-9 (vista de clientes solo por REST) | **Sí** | Story 9.1 AC4 |
| R-MVP-10 (registro en un único punto de entrada, derivación determinista del nombre efectivo, colisión no es error) | **Sí** | Story 9.1 AC11/AC12/AC13/AC14 |

> **Nota de recuento.** La verificación explícita de las restricciones heredadas se hizo
> originariamente sobre **9** restricciones (`R-MVP-1..R-MVP-9`). La propagación de **AD-31** y
> **AD-32** añadió **R-MVP-10**, de modo que la tabla vigente cubre **10** restricciones
> (`R-MVP-1..R-MVP-10`); la cifra de nueve es **histórica** y se conserva aquí solo como
> referencia del corte de análisis.

---

## Resumen

| Severidad | Cantidad | IDs |
| --- | --- | --- |
| Crítica | 0 | — |
| Alta | 2 | D-1, D-2 |
| Media | 5 | D-3, D-4, D-5, H-1, H-2 |
| Baja | 0 | — |

Todos los hallazgos son aplicables sin decisión de producto: son cierres de divergencia y de
cobertura dentro del contrato ya aprobado por el PRD, la UX y la arquitectura.

### Verificaciones mecánicas ejecutadas sobre este documento

| Verificación | Resultado |
| --- | --- |
| Mención de cada FR-1..FR-11 | PASS — mínimo 13 menciones |
| Mención de cada NFR-1..NFR-10 | PASS — mínimo 4 menciones |
| Mención de cada UX-DR-1..UX-DR-45 | PASS — mínimo 3 menciones |
| Mención de cada AR-1..AR-25 | PASS — mínimo 1 mención (**AR-21 y AR-25 solo en la definición → H-1**) |
| Mención de cada AD-1..AD-30 | PASS — mínimo 1 mención |
| Secuencia de IDs de historia 1.1..13.1 | PASS — 47 historias, numeración continua sin huecos |
| Presencia de `**Given**`/`**When**`/`**Then**` | PASS — 261/261 en las tres |
| Placeholders `{{...}}` residuales | PASS — 0 |
| Caracteres CJK / U+FFFD | PASS — 0 / 0 |
| Estados de `DownloadState` de la arquitectura citados en AC | **FALLA** — `Cancelled` no aparece en ninguna AC (**D-1**) |

---

## Reapertura y resolución de D-5 (AD-30)

**Por qué se reabre.** La resolución original de D-5.contenía una contradicción interna que el
propio hallazgo señalaba: la corrección propuesta exigía que «`machineName` **no** tenga
restricción de unicidad ni índice único, y que dos clientes con el mismo `machineName` pero
distinto `clientId` **coexistan** en la tabla». Esa regla resolvía el par del hallazgo (el
servidor no rechazaba ni dejaba invisible al segundo cliente homónimo), pero **dejaba abierta la
ambigüedad de la vista** que D-5 describía: el organizador vería dos filas con la misma etiqueta
de equipo y no podría saber a quién corresponde cada una. Además, la decisión de producto tomada
por el usuario el 2026-10-26 va en sentido contrario: el **`machineName` es la etiqueta humana del
equipo y el servidor lo valida y lo hace único**. La arquitectura registra esa decisión como
**AD-30**.

**Qué decide AD-30.** El `clientId` (UUID v7 opaco, emitido una vez y persistido) es la **clave
técnica** de la fila de `ClientRegistration`, la deduplica y correlaciona los logs. El
`machineName` es la **etiqueta humana** del equipo: el cliente la **propone** y el **servidor la
valida y la hace única** en el momento del registro. La unicidad del nombre es una **restricción
de una columna**, **no** un mecanismo de deduplicación: el servidor no agrupa, no fusiona ni
reasigna filas por nombre. Ante una colisión con otro equipo ya registrado, el servidor **no
rechaza ni oculta** al segundo cliente: le asigna un **nombre único derivado** del propuesto
(el `stem` recortado más el guion y los **32 dígitos hexadecimales** del `clientId`), lo
**persiste** y se lo **notifica**, y el cliente lo **adopta**. Un
`machineName` que no cumple las reglas de forma se **rechaza en el registro** y el cliente queda
sin registrar y no opera. La regla **no introduce credenciales, autenticación ni autorización**:
el único control de acceso al protocolo sigue siendo la compatibilidad de versión.

**Cómo lo cierra el backlog.** Story 9.1 incorpora seis criterios exigibles (AC5..AC10) que
tienen la Divergencia del par original como caso covered:

| AC | Qué fija | Cita |
| --- | --- | --- |
| 9.1 AC5 | `ClientRegistration` es del servidor y su **clave** es el `clientId`: no deduplica, no agrupa, no fusiona ni reasigna filas por `machineName` | *(AR-13, AR-14, AD-23, AD-30)* |
| 9.1 AC6 | Dos clientes con distinto `clientId` y **mismo** `machineName` propuesto coexisten: el servidor asigna un nombre único **derivado**, lo persiste y lo notifica; ningún `clientId` cambia | *(FR-10, AR-14, AD-30)* |
| 9.1 AC7 | Un `machineName` mal formado se **rechaza en el registro** con `ApiError` de `code` propio, `cause` y `nextStep` obligatorios; el cliente queda sin registrar y no opera | *(FR-10, AR-4, AR-14, AD-30)* |
| 9.1 AC8 | El rechazo por nombre inválido y la colisión se registran en logging JSON con `code` **distintos**, de modo que son diagnosticables y distinguibles entre sí | *(AR-14, AR-20, AD-15, AD-25, AD-30)* |
| 9.1 AC9 | El renombrado del equipo en el cliente propone un nombre nuevo y aplica la misma regla; el `clientId` no cambia y el servidor no ofrece renombrado de otro cliente | *(FR-10, AR-14, AD-30)* |
| 9.1 AC10 | La vista expone **siempre el nombre efectivo**, nunca el propuesto que colisionó; el `machineName` no es credencial y no confiere permisos | *(FR-10, AR-14, AD-6, AD-12, AD-30)* |

**Trazabilidad.** La fila de la tabla de trazabilidad de las AR pasa a
`| AR-14 | 1.3 AC2, 1.4 AC1, 5.2 AC8, 9.1 AC5..AC10 | Estructural (identidad del cliente) |`, y las
filas de **AR-4** (`9.1 AC7`) y **AR-20** (`9.1 AC8`) incorporan los AC nuevos que las citan.

**Verificación.** `tools/check-epics.ps1` sobre `epics.md` devuelve `ok: true`, `total_findings: 0`,
`stories: 47`, `acceptance_criteria: 261`, `ar_definitions: 25`, `ar_table_rows: 25`. El recuento
arquitectónico **actual** del documento es 13 épicas / 47 historias / **261** AC, confirmado
mecánicamente (13 encabezados `## Epic N`; 47 encabezados `### Story N.M`; 261 ocurrencias de
`**Given**` / `**When**` / `**Then**`, 261/261/261). El corte de cierre por **AD-30** —antes de la
propagación de AD-31/AD-32— tenía 239 AC; esa cifra es **histórica** y no describe el documento
actual. `.agents/skills/bmad-architecture/scripts/lint_spine.py` sobre `ARCHITECTURE-SPINE.md` devuelve `ok: true`, `total_findings: 0`.

**Veredicto.** **D-5 queda RESUELTO.** La divergencia del par original —el servidor que acepta o
rechaza al segundo cliente homónimo— deja de ser construible: ambas unidades construyen el mismo
sistema, porque el nombre único derivado, la notificación al cliente, la adopción del nombre
efectivo y el rechazo por forma **están escritos como AC exigibles**, y el `clientId` permanece
como la clave en todo caso.

---

## Propagación de AD-31 y AD-32 (cierre mecánico restante)

**Por qué se propaga.** La resolución de D-5 por **AD-30** dejó el `machineName` único y validado
por el servidor, pero dejó **dos huecos construibles** que la propia lente adversarial detecta al
releer el backlog tras el cierre por AD-30:

- **El algoritmo de derivación no estaba acotado.** AD-30 fijaba que ante una colisión el servidor
  asigna un nombre «único derivado», pero **no** decía **cómo** se derivaba
  el siguiente candidato, **hasta dónde** se recortaba el `stem` ni **cuándo** el bucle de búsqueda
  de un nombre libre **terminaba**. Dos unidades podían derivar nombres distintos —una recortando
  por bytes y otra por puntos de código, una deteniéndose en `-9` y otra en `-10`— y producir
  **tablas de clientes incompatibles** para el mismo conjunto de entradas.
- **El contrato del registro no estaba cerrado.** El backlog nombraba la colisión, el rechazo por
  forma y el renombrado, pero **no** fijaba la forma exacta del cuerpo ni de la respuesta, ni el
  **estado HTTP** de cada desenlace, ni la regla de que el **renombrado no tiene endpoint propio**.
  Dos unidades podían construir el registro con cuerpos distintos, o resolver la colisión con un
  `201 Created` y con un `409 Conflict` —contradiciendo que «la colisión no es un error»— o
  materializar el renombrado como un endpoint de administración, **ampliando la superficie REST**
  que AD-30 declara fuera del MVP.

**Qué deciden AD-31 y AD-32.** Son dos decisiones de arquitectura que **precisan** AD-30 sin
ampliar el MVP (AD-30: «no añaden superficie nueva»):

- **AD-31 — Derivación determinista del nombre efectivo.** El nombre efectivo derivado se construye con
  un algoritmo **determinista, acotado y sin búsqueda**: `stem_truncado` (recortado a **≤ 31 puntos
  de código**) + el sufijo fijo `"-"` seguido de los **32 dígitos hexadecimales en minúsculas** de
  la forma canónica del `clientId` (`Guid.ToString("N")`, sin guiones); el `stem` es el nombre
  propuesto **ya recortado**, y la derivación **no**
  normaliza, **no** minúscula y **no** quita diacríticos. La cota dura es **≤ 64 puntos de código
  Unicode** (medidos con `System.Text.Rune`, no en bytes ni en unidades UTF-16), el recorte cae
  **siempre en un límite de punto de código** —ningún emoji ni ideograma queda partido— y el
  `stem` truncado **no puede quedar vacío** (se recurre al `stem` de reserva `equipo`). El sufijo
  tiene **longitud fija de 33 puntos de código**, luego no hay casos de sufijo multidígito variable
  ni búsqueda: el servidor **no** busca nombres libres, **no** incrementa ningún contador y **no**
  puede agotar el espacio de nombres. La **unicidad está garantizada por construcción**: la forma
  canónica es la identidad sobre `-` y los dígitos hexadecimales en minúsculas, la cola de 33
  puntos de código ocupa siempre las últimas posiciones, y dos nombres derivados iguales implican
  el mismo `clientId`. Por construcción, el nombre
  efectivo **nunca excede la cota**, **nunca necesita volver a ser derivado** y **no altera** el
  `clientId` ni fusiona ni reasigna filas.
- **AD-32 — Contrato REST del registro, la reconexión y el renombrado.** `POST /api/v1/clients` es
  el **único** punto de entrada del cliente al servidor, para el **alta**, la **reconexión** y el
  **renombrado**; no existe endpoint adicional de registro, de renombrado ni de administración de
  nombres. El cuerpo es `RegisterClientRequest` `{ clientId, machineName, clientVersion,
  protocolVersion }` —los mismos valores de versión que las cabeceras `X-Client-Version` y
  `X-Protocol-Version`, sin segunda fuente de verdad— y **no** lleva `schemaVersion`: el registro es
  la **única** mutación exenta. La respuesta es `RegisterClientResponse` `{ clientId, machineName,
  connectionState, lastSeenAt, renamed }`, donde `machineName` es **siempre el nombre efectivo** y
  `renamed` es `true` **únicamente** cuando hubo colisión y el nombre devuelto difiere del
  propuesto. Estados HTTP: **`200 OK`** en el éxito —no `201 Created`, porque la fila es
  actualizable—, **`422 Unprocessable Content`** ante nombre mal formado, **`426 Upgrade
  Required`** ante versión incompatible, y esa comprobación ocurre **antes** de cualquier
  validación de nombre. La **colisión no es un error**: el registro tiene éxito con `renamed: true`.
  La **reconexión** reconoce la fila por `clientId`, actualiza `connectionState`, `lastSeenAt` y
  `clientVersion`, **no** asigna nombre nuevo y **no** dispara la derivación: un cliente
  reconectado nunca colisiona consigo mismo.

**Cómo lo cierra el backlog.** Story 9.1 incorpora cuatro criterios exigibles (AC11..AC14) que
tienen los dos huecos anteriores como caso covered:

| AC | Qué fija | Cita |
| --- | --- | --- |
| 9.1 AC11 | Derivación determinista del nombre efectivo: `stem_truncado` (≤ 31 puntos de código) + sufijo fijo `"-"` + los **32 dígitos hexadecimales** del `clientId` (`Guid.ToString("N")`); **≤ 64 puntos de código Unicode** (`System.Text.Rune`, no bytes ni UTF-16); truncación **siempre en límite de punto de código**; `stem` truncado no vacío (`equipo` de reserva); sufijo de **longitud fija de 33 puntos de código**; el nombre derivado **vuelve a ser admisible**, **nunca necesita volver a ser derivado** y su **unicidad está garantizada por construcción** | *(FR-10, AR-14, AD-30, AD-31)* |
| 9.1 AC12 | El nombre efectivo es una **función pura** de (`stem`, `clientId`): **no** se buscan nombres libres, **no** se incrementa ningún contador y **no** se agotan candidatos; la **forma canónica** (NFKC → eliminación de marcas diacríticas → minúsculas Unicode invariantes, en ese orden) **conserva intacta** la cola de 33 puntos de código del sufijo, de modo que dos nombres derivados iguales implican el mismo `clientId` y **dos clientes distintos nunca reciben el mismo nombre**; el `clientId` no cambia, no se fusiona filas ni se reasigna la fila de otro cliente | *(FR-10, AR-14, AD-30, AD-31)* |
| 9.1 AC13 | Forma exacta del cuerpo (`RegisterClientRequest` `{ clientId, machineName, clientVersion, protocolVersion }`, sin `schemaVersion` y con la **única** exención de mutación) y de la respuesta (`RegisterClientResponse` `{ clientId, machineName, connectionState, lastSeenAt, renamed }`), con `machineName` **siempre efectivo** y `renamed` `true` **solo** ante colisión con nombre distinto | *(FR-10, AR-14, AD-5, AD-6, AD-22, AD-23, AD-30, AD-32)* |
| 9.1 AC14 | Alta, reconexión y renombrado en la **misma** operación de registro, **único** punto de entrada (sin endpoint adicional); **`200 OK`** en el éxito (no `201 Created`), **`422`** ante nombre mal formado con la forma única `ApiError`, **`426`** ante versión incompatible **antes** de validar el nombre; **la colisión no es un error** (`renamed: true`); la **reconexión conserva** fila y nombre, **no** deriva y **no** colisiona consigo misma | *(FR-10, AR-14, AD-5, AD-6, AD-15, AD-22, AD-23, AD-25, AD-29, AD-30, AD-32)* |

Se añade además la **restricción heredada R-MVP-10** a la tabla «Restricciones heredadas que
gobiernan todo el desglose» de `epics.md`: el alta, la reconexión y el renombrado ocurren en **una
sola** operación de registro, que es el **único** punto de entrada del cliente al servidor; el
nombre efectivo derivado es **determinista y acotado** (`stem_truncado` de **≤ 31 puntos de código** +
el sufijo fijo `-` + los **32 dígitos hexadecimales** del `clientId`, ≤ 64 **puntos de
código Unicode**, truncación que **no parte** ningún carácter) y su **unicidad está garantizada por
construcción**, sin búsqueda de nombres libres;
la colisión **no es un error**: el registro tiene éxito con `renamed: true` y el nombre efectivo.
La fila de la tabla de trazabilidad de **AR-14** pasa a `1.3 AC2, 1.4 AC1, 5.2 AC8, 9.1 AC5..AC14`.

**Efecto sobre la lente adversarial.** Ninguno de los dos huecos **reabre** un hallazgo: D-5
sigue **RESUELTO** (el `clientId` permanece como clave y la unicidad del nombre sigue siendo una
regla de nombres, no un control de acceso). Lo que hacen AD-31 y AD-32 es **endurecer** el cierre
de D-5, sustituyendo dos lecturas divergentes por reglas exigibles. Se mantiene el veredicto
general: **ADECUADO CON FALLOS REALES, YA CERRADOS**, ahora con **13 épicas / 47 historias / 261
AC** y **diez** restricciones heredadas (`R-MVP-1..R-MVP-10`) verificadas.

**Verificación.** `tools/check-epics.ps1` sobre `epics.md` devuelve `ok: true`, `total_findings: 0`,
`stories: 47`, `acceptance_criteria: 261`, `ar_definitions: 25`, `ar_table_rows: 25`. El recuento
arquitectónico del documento es 13 épicas / 47 historias / 261 AC, confirmado mecánicamente
(13 encabezados `## Epic N`; 47 encabezados `### Story N.M`; 261 ocurrencias de `**Given**` /
`**When**` / `**Then**`, 261/261/261) y 10 filas `R-MVP-1..R-MVP-10`.
`.agents/skills/bmad-architecture/scripts/lint_spine.py` sobre
`ARCHITECTURE-SPINE.md` devuelve `ok: true`, `total_findings: 0`.
