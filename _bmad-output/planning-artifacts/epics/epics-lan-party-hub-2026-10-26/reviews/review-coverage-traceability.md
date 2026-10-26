# Revisión — Cobertura y trazabilidad (epics)

- **Revisor:** cobertura de requisitos (checklist canónico del workflow `bmad-create-epics-and-stories`)
- **Objetivo:** `epics.md`
- **Fecha:** 2026-10-26
- **Modo:** headless, ejecución secuencial (sin subagentes disponibles)
- **Base:** `bde708d97766dfc607528abb9d3a28bc82487f0e`

## Veredicto

**ADECUADO CON RESERVAS.** El documento cubre el 100 % de FR-1..FR-11, NFR-1..NFR-10 y
UX-DR-1..UX-DR-45, con 13 épicas y 47 historias y 243 criterios de aceptación. Tres hallazgos
**medios** de cobertura y dos **bajos** de consistencia. Ninguno bloqueante.

> **Conteo arquitectónico final (cierre documental de Fase E + cierre por AD-30/AD-31/AD-32):**
> 13 épicas, 47 historias, 243 AC. Conteo confirmado mecánicamente sobre `epics.md`: 13 encabezados
> `## Epic N`, 47 encabezados `### Story N.M` y 243 ocurrencias de `**Given**` / `**When**` /
> `**Then**` (243/243/243). El verificador `verify_epics.py` devuelve `stories: 47`,
> `acceptance_criteria: 243`, `ar_definitions: 25`, `ar_table_rows: 25`, `ok: true`,
> `total_findings: 0`.
>
> **Recuento equivalente de las restricciones heredadas:** 10 filas `R-MVP-1..R-MVP-10`, de las
> que **R-MVP-10** es la añadida por la propagación de AD-31 y AD-32 (punto de entrada único del
> registro, derivación determinista del nombre efectivo, colisión no es error).

---

## Checklist punto por punto

### 1. «Todo FR del PRD aparece en al menos una historia» — **CUMPLE**

| FR | Épica | Historia que lo cubre | Evidencia |
| --- | --- | --- | --- |
| FR-1 | 2 | 2.1, 2.2, 2.3 | Story 2.1 AC1 (alta con metadatos completos), Story 2.2 AC1 (publicación bloqueada sin licencia) |
| FR-2 | 3 | 3.1, 3.2, 3.3 | Story 3.1 AC2 (refleja el catálogo publicado), Story 3.2 AC2 (filtro por categoría) |
| FR-3 | 4 | 4.1, 4.2, 4.5 | Story 4.1 AC1 (mDNS), Story 4.2 AC1 (dirección manual), Story 4.5 AC5 (reconexión) |
| FR-4 | 4 | 4.3 | Story 4.3 AC2 (`426 Upgrade Required`), AC3 (diagnóstico con versiones) |
| FR-5 | 5 | 5.2, 5.3 | Story 5.2 AC2 (progreso, pausa, reanudación), Story 5.3 AC2 (recuperación tras reinicio) |
| FR-6 | 6 | 6.1, 6.2 | Story 6.1 AC1 (sha256 + firma Ed25519), AC3 (no confiable) |
| FR-7 | 5 | 5.1 | Story 5.1 AC1 (puerto `ITransferEngine`), AC2 (una sola implementación) |
| FR-8 | 7 | 7.1, 7.2, 7.3 | Story 7.1 AC1 (orden normativo), Story 7.3 AC1 (extracción, detección de instalación existente) |
| FR-9 | 8 | 8.1, 8.2 | Story 8.1 AC1 (detección y lanzamiento), Story 8.2 AC5 (ejecutable ausente) |
| FR-10 | 9 | 9.1, 9.2, 9.3 | Story 9.1 AC2 (columnas), Story 9.3 AC1 (`ClientError`) |
| FR-11 | 10 | 10.1, 10.2 | Story 10.1 AC1 (detección), Story 10.2 AC1 (descarga e instalación de versión completa) |

Verificación mecánica: `FR-1`..`FR-11` aparecen 13..53 veces cada una en el documento (definición
en el inventario + mapa de cobertura + historia). **Ninguno en 0.**

### 2. «Todo NFR aparece en al menos una historia» — **CUMPLE**

| NFR | Épica | Evidencia |
| --- | --- | --- |
| NFR-1 | 1, 4, 5, 7, 9, 11 | Story 1.2 AC5 (sin Internet), Story 3.1 AC3 (conserva catálogo), Story 11.8 AC4 |
| NFR-2 | 11, 12 | Story 11.4 AC3 (no promete velocidad), Story 12.4 (S-4) |
| NFR-3 | 5, 7, 10 | Story 5.3 AC1/AC4, Story 10.3 AC2 |
| NFR-4 | 1, 11 | Story 1.2/1.3 (Windows x64), Story 11.7 |
| NFR-5 | 6, 7, 10 | Story 6.1 AC2/AC3, Story 7.2 AC1 |
| NFR-6 | 9, 11, 12 | Story 12.4 AC2 (S-4 mide CPU y memoria) |
| NFR-7 | 9 | Story 9.3 AC1, Story 9.4 AC2 |
| NFR-8 | 2 | Story 2.2 AC1 |
| NFR-9 | 4, 5, 7, 8, 9, 10 | Story 4.5 AC3, Story 7.3 AC7, Story 9.1 AC3 |
| NFR-10 | 1, 5, 11 | Story 1.2 AC5, Story 5.1 AC3 |

Verificación mecánica: NFR-1..NFR-10 aparecen 4..16 veces cada una. **Ninguno en 0.**

### 3. «Todo UX-DR aparece en al menos una historia» — **CUMPLE**

Verificación mecánica: los 45 UX-DR aparecen **al menos 2 veces** cada uno (definición en
`### UX Design Requirements` + entrada en `### FR Coverage Map`); cada UX-DR del mapa apunta a una
épica que contiene historias con AC que lo citan explícitamente.

Comprobación de los UX-DR con AC directa (no solo cita de mapa):

| UX-DR | Historia con AC explícita |
| --- | --- |
| UX-DR-1..6 | 11.1, 11.2, 11.3, 11.4 |
| UX-DR-7, 8, 9 | 11.5, 11.5, 3.2 |
| UX-DR-10 | 3.3, 11.2 |
| UX-DR-11 | 4.5 AC1 |
| UX-DR-12 | 11.7 AC5 |
| UX-DR-13 | 5.2 AC3, 7.3 AC5 |
| UX-DR-14 | 9.1 AC1, 11.5 AC4 |
| UX-DR-15 | 9.3 AC5 |
| UX-DR-16 | 7.3 AC3, 11.5 AC5 |
| UX-DR-17..20 | 8.2 AC1/AC2, 8.3 AC1, 10.1 AC4 |
| UX-DR-21 | 3.3 AC3 |
| UX-DR-22 | 5.2 AC4, 11.4 AC2 |
| UX-DR-23, 24 | 11.6 AC1, AC2, 9.1 |
| UX-DR-25 | 11.6 AC3 |
| UX-DR-26 | 8.2 (via tabla de estados) — **ver M-1** |
| UX-DR-27 | 3.2 AC5, 11.6 AC4 |
| UX-DR-28 | 3.2 AC4 |
| UX-DR-29 | 4.5 AC3 |
| UX-DR-30 | 4.1 AC3 |
| UX-DR-31 | 4.3 AC3, 9.3 AC3 |
| UX-DR-32 | 3.1 AC3, 10.1 AC1 |
| UX-DR-33 | 6.2 AC1, AC2 |
| UX-DR-34 | 7.3 AC5, AC7 |
| UX-DR-35 | 8.2 AC5 |
| UX-DR-36 | 6.2 AC3, 9.3 AC5 |
| UX-DR-37 | 7.3 AC3, AC4 |
| UX-DR-38..42 | 11.7, 11.8 |
| UX-DR-43 | 8.1 AC4/AC5, 11.8 AC5 |
| UX-DR-44 | 2.1 AC4, 9.3 AC2, 11.6 |
| UX-DR-45 | 5.3 AC3, 10.3 AC1 |

### 4. «Ninguna historia depende de una historia futura» — **CUMPLE (con M-2)**

Verificación por historia, en orden:

- 1.1 → 1.2 → 1.3 → 1.4: cada una construye sobre la anterior; ninguna invoca una posterior.
- 2.1 (alta de videojuegos) → 2.2 (publicación de paquete, necesita el juego de 2.1) → 2.3 (control de
  publicación, necesita lo publicado en 2.2).
- 3.1 → 3.2 → 3.3: la búsqueda necesita el catálogo sincronizado; la ficha necesita la búsqueda.
- 4.1 → 4.2 → 4.3 → 4.4 → 4.5: la dirección manual (4.2) **no** depende del descubrimiento (4.1);
  el diagnóstico de versión (4.3) no depende de la conexión (4.1/4.2) sino del protocolo;
  el ancla (4.4) no depende de 4.5; la reconexión (4.5) usa el contrato ya fijado.
- 5.1 (puerto) → 5.2 (descarga) → 5.3 (persistencia): la descarga HTTP no necesita la
  persistencia; la persistencia no necesita la descarga para compilar.
- 6.1 (validación) → 6.2 (UI de la validación): la UI puede construirse sobre el contrato de error.
- 7.1 (orden) → 7.2 (confines) → 7.3 (instalación): 7.2 no depende de 7.3; 7.3 usa ambos.
- 8.1 → 8.2 → 8.3: el bloqueo y la reparación usan el lanzador y los estados de instalación.
- 9.1 → 9.2 → 9.3 → 9.4: la proyección y los errores usan el registro de clientes.
- 10.1 → 10.2 → 10.3: la detección es previa; la actualización y la reparación la usan.
- 11.1..11.8: cada historia de UX es verificable sobre el contrato visual; ninguna requiere otra
  historia de UX para **comprobarse**, y todas se apoyan en contratos ya fijados en `DESIGN.md`.
- 12.1..12.5: cada spike es independiente de los demás.

**Sin dependencias futuras detectadas.**

### 5. «Las tablas/entidades se crean solo cuando la historia las necesita» — **CUMPLE**

Story 1.1 crea solo los contratos de `Shared` (justificados por AR-3). Story 1.2 crea el esquema
del servidor (`catalog.sqlite` + repositorio de paquetes). Story 1.3 crea el esquema del cliente
(`state.sqlite`). Story 2.1 crea `Game`. Story 2.2 crea `PackageDescriptor` e
`InstallationManifest`. Story 5.3 crea la tabla de progreso del cliente (`bytesPersisted`).
Story 9.1 crea `ClientRegistration`. Story 9.2 crea `ClientInstallationView`. Story 9.3 crea
`ClientError`. Story 1.3 crea `ClientIdentity`. Story 8/10 crean `ClientInstallation` vía el módulo de
instalación.

**No hay ninguna historia que cree todas las tablas por adelantado.**

### 6. «Plantilla de starter» — **NO APLICA**

La arquitectura no especifica plantilla de starter: el proyecto es greenfield y la espina define la
estructura de directorios (`src/Server`, `src/Client`, `src/Shared`, `tests/*`) pero no una plantilla
de arranque. Story 1.1 la implementa. Correcto.

### 7. «Los spikes no se convierten en producto» — **CUMPLE**

Épica 12 existe como **épica de experimentos**, con una regla de alcance explícita que prohíbe que
un spike produzca capacidad visible, modifique el contrato del MVP o se convierta en épica de
funcionalidad. Las cinco historias de la épica 12 tienen entregable de **informe con resultado**, no
de funcionalidad. La historia 12.1 (S-2) declara su carácter bloqueante **para el cierre del MVP**;
las 12.2..12.5 declaran su carácter no bloqueante y que su resultado «no cambia ningún artefacto del
MVP». Story 12.5 AC4 cierra explícitamente que la aparición de `BitTorrentTransferEngine`, del
módulo `Tracking` o de `chunkMap` requiere su propia AD y su propio traceability, «nunca por
edición del MVP».

### 8. «Las épicas entregan valor de usuario, no capas técnicas» — **CUMPLE**

Las 11 épicas de producto están enunciadas como **resultado para una persona** («El organizador
publica el catálogo y sus paquetes», «El jugador descarga un paquete íntegro del servidor», «El
jugador sabe por qué no puede jugar»), no como capas técnicas. Las cuatro épicas que sí son técnicas
por naturaleza están justificadas y acotadas: épica 1 (fundamentos, explícitamente marcada como
fundacional y no de valor) y épica 12 (spikes, explícitamente marcada como experimentos, no
producto).

### 9. «Solapamiento de archivos entre épicas (file churn)» — **CUMPLE**

Comprobación por módulo:

- Épica 2 (catálogo), 3 (cliente catálogo), 4 (conexión), 5 (descarga), 6 (confianza), 7
  (instalación), 8 (lanzamiento), 9 (panel), 10 (actualización) tocan **módulos distintos** de la
  arquitectura (`Server/Catalog`, `Client/CatalogSync`, `Client/Connection`, `Client/Download`,
  `Domain/PackageIntegrity`, `Client/Installation`, `Client/GameLauncher`,
  `Server/ClientManagement`, `Client/CatalogSync`+`Client/Installation`).
- Épica 10 **sí** toca `Client/Installation`, que también toca la épica 7. Está justificado: la
  épica 10 es la continuación del dominio de instalación (detección de versión y reparación), y
  separarla exigiría duplicar el modelo de `ClientInstallation` en dos épicas. Es el patrón
  «componente único con historias ordenadas» que el propio step-02 recomienda como correcto.
- Épica 11 es transversal por diseño (tokens y contrato visual): **no** modifica los módulos de
  negocio, sino la capa de presentación. Es la única épica que actúa sobre los mismos archivos de
  varias épicas, y su justificación es explícita en el objetivo: es la garantía de coherencia
  visual, no una capa técnica aislada.

### 10. «Cobertura de los criterios de éxito SM-1..SM-5 y SM-C1/SM-C2» — **CUMPLE (con B-1)**

| SM | Cobertura |
| --- | --- |
| SM-1 (flujo completo en LAN real con 2+ clientes) | Story 12.x no; flujo repartido: 4.1/4.3 (conectar), 3.1 (sincronizar), 3.3 (mostrar), 5.2 (descargar), 6.1 (validar), 7.3 (instalar), 8.1 (detectar instalado y ejecutar), 9.1 (mostrar estado). **Ver B-1**: no hay una historia de cierre E2E. |
| SM-2 (100 % de reanudaciones desde el progreso persistido) | Story 5.3 AC2, AC3 |
| SM-3 (ningún paquete alterado llega a instalación) | Story 6.1 AC2, AC3, AC5; Story 7.1 AC1 |
| SM-4 (servidor muestra estado y errores sin preguntar) | Story 9.1, 9.2, 9.3 |
| SM-5 (cada paso de demostración tiene instrucciones reproducibles) | **Ver B-1** |
| SM-C1 (no se optimiza velocidad sobre recuperación e integridad) | Story 11.4 AC3 (no promete velocidad), Story 12.4 (S-4 mide, no promete) |
| SM-C2 (no se optimiza cobertura P2P a costa del flujo servidor-cliente) | Story 5.1 AC2, AC4; Story 12.5 AC4 |

---

## Hallazgos detallados

### M-1 — UX-DR-26 (cobertura de teclado de las acciones de instalación) no tiene historia propia que lo cite — **MEDIA**

- **Ubicación:** `### UX Design Requirements` → UX-DR-26; `### FR Coverage Map` → «UX-DR-26: Epic 8».
- **Evidencia:** UX-DR-26 especifica un comportamiento muy preciso y verificable: «Jugar» bloqueada
  **recibe foco** (no se omite ni se deshabilita de forma invisible), al recibir foco lee su motivo
  junto a la etiqueta, `Enter` sobre «Jugar» bloqueada no produce ningún cambio de estado, `Espacio`
  alterna pausa y reanudación de la descarga enfocada y **no** actúa sobre la instalación, `Ctrl+4`
  salta a la Biblioteca y desde ahí `Tab` alcanza «Jugar», «Reparar» o «Descargar de nuevo», y el
  control bloqueado conserva un indicador de foco visible que no depende del color de la insignia.
  Laépica 8 cita UX-DR-26 en su encabezado y Story 8.2 cubre el motivo visible, pero **ningún AC de
  la historia cita UX-DR-26 ni enuncia sus reglas de teclado**. El mapa de cobertura afirma una
  cobertura que el texto de las historias no sostiene.
- **Divergencia que previene:** un implementador puede construir «Jugar» con `IsEnabled = false`
  cuando la instalación es inválida, lo que en Avalonia **excluye el control del recorrido de
  foco** y deja al usuario de teclado sin ninguna señal de que la acción existe. El resultado es
  exactamente el fallo que UX-DR-26 dice querer impedir, y el criterio de aceptación explícito de
  UX-DR-26 («un lector de pantalla anuncia el nombre de la acción, su estado bloqueado y el motivo,
  y la tecla `Enter` no produce ningún cambio de estado») quedaría sin historia que lo verifique.
- **Corrección propuesta:** añadir a Story 8.2 un grupo de AC que recoja las reglas de teclado de
  UX-DR-26, y hacer explícito el criterio de aceptación verificable de UX-DR-26 (anuncio del lector
  de pantalla + `Enter` sin efecto).

### M-2 — NFR-2 y NFR-6 solo aparecen en épicas cuyo entregable no las mide — **MEDIA**

- **Ubicación:** `### FR Coverage Map` → «NFR-2: Epic 11, S-4», «NFR-6: Epic 9, S-4».
- **Evidencia:** NFR-2 dice «El diseño debe admitir el objetivo de 8 a 20 jugadores concurrentes en
  una red Ethernet de 1 Gbps» y NFR-6 dice «El servidor y el cliente mantienen un consumo moderado
  de CPU y memoria; los límites concretos se fijarán con pruebas». Ambas remiten al benchmark
  (S-4), que es **no bloqueante** y cuya fecha de ejecución no depende del MVP. La épica 11 cita
  NFR-2 solo para *no prometer* velocidad, y la épica 9 cita NFR-6 sin ningún AC que la mida.
  Ninguna historia del backlog **verifica** que el diseño admita 20 clientes concurrentes ni que el
  consumo sea moderado: la afirmación «moderado» queda sin criterio comprobable.
- **Divergencia que previene:** el backlog se cierra sin ninguna historia que mida la escala que el
  PRD declara como hipótesis, y la épica 12 (S-4) puede no ejecutarse nunca porque es no
  bloqueante. El resultado es que NFR-2 y NFR-6 se declaran «cubiertas» sin que ninguna
  construcción del MVP haya demonstrated nada al respecto.
- **Corrección propuesta:** separar dos cosas que el mapa confunde. (a) **Verificabilidad
  estructural**: los límites por cliente de AD-14 (descargas simultáneas, conexiones totales) son
  configurables con valor por defecto y el reparto del enlace es equitativo entre los clientes que
  descargan a la vez — eso **sí** se puede verificar en una historia y así debe quedar dicho.
  (b) **Medición**: la cifra concreta es el resultado de S-4, no bloqueante, y su ausencia no
  invalida el MVP. Story 5.2 ya cubre el reparto equitativo, pero no lo declara como verificación de
  NFR-2; y la épica 12 debe declarar explícitamente que un resultado negativo de S-4 **no** invalida
  el MVP, como ya hace con los demás spikes no bloqueantes.

### M-3 — La historia 12.5 (S-3) usa vocabulario P2P dentro del backlog del MVP — **MEDIA**

- **Ubicación:** `### Epic 12` → Story 12.5, AC2, AC3, AC5.
- **Evidencia:** el AC2 dice «recibe **fragmentos de un *peer***, no solo del servidor»; el AC5 dice
  «decide el formato del mapa de fragmentos». El vocabulario «peer» y «fragmentos» es, por AD-28,
  **vocabulario que no pertenece al MVP**, y AD-21 prohíbe expresamente que un spike no bloqueante
  se propague por edición: «Un spike no bloqueante no se propaga por edición. Su resultado se
  incorpora con su propia AD y su propio traceability en la fase que lo ejecute».
- **Divergencia que previene:** un constructor que lea el backlog puede interpretar que el mapa de
  fragmentos es un requisito del MVP y añadir la columna `chunkMap` al esquema del cliente, que es
  precisamente lo que AD-24 y AD-28 excluyen. El riesgo es real porque la historia **es** del
  backlog aprobado, no de un documento de fase posterior.
- **Corrección propuesta:** marcar el AC2 y el AC5 de Story 12.5 como **evidencia del spike** y no
  como requisito, y añadir una línea explícita de que esta historia se ejecuta **fuera del backlog
  del MVP** y **no** puede adequacy su esquema ni su vocabulario. La forma del puerto
  `ITransferEngine` que se decide en S-3 es la **segunda implementación**, no la forma del puerto
  ya fijada.

### B-1 — No hay historia que cierre la demostración end-to-end del MVP (SM-1, SM-5) — **BAJA**

- **Ubicación:** `### Epic List` (no hay épica de aceptación del MVP); `### FR Coverage Map`.
- **Evidencia:** SM-1 exige que «el flujo de §9 se completa en una LAN real con dos o más clientes,
  sin Internet y con el mismo paquete íntegro para todos», y SM-5 exige que «cada paso de
  demostración tiene instrucciones reproducibles». Las 11 épicas de producto cubren cada paso
  **por separado**, pero ninguna historia los encadena en una LAN real con dos o más clientes. La
  arquitectura ya exige el E2E en LAN real como escenario de aceptación (AD-16) y la classify la
  checklist de `docs/plan.md` §10 con casos E2E que solo se ejecutan en esa red.
- **Divergencia que previene:** el MVP podría cerrarse con todas las historias «verdes» y sin que
  nadie haya corrido nunca el flujo completo en una LAN real, que es el criterio de éxito primario
  del PRD.
- **Corrección propuesta:** añadir una historia de cierre en la épica 9 (o una épica 13 de
  aceptación) que sea el **guion de demostración E2E en LAN real**: los diez pasos del PRD §9, con
  dos o más clientes, sin Internet, con el mismo paquete íntegro para todos, y con instrucciones
  reproducibles por paso. No es una historia de código: es la historia que hace verificable SM-1 y
  SM-5.

### B-2 — La story 11.2 (tipografía) no cita UX-DR-6 en su encabezado aunque el mapa la asigna a 11.x — **BAJA**

- **Ubicación:** `### FR Coverage Map` → «UX-DR-6: Epic 11 — Microcopia por estado crítico»;
  `### Epic 11` → Story 11.4.
- **Evidencia:** el mapa es correcto (UX-DR-6 está en 11.4), pero el encabezado de la épica 11
  lista «UX-DR-1, UX-DR-2, UX-DR-3, UX-DR-4, UX-DR-5, UX-DR-6, UX-DR-7…», lo cual es correcto, y
  Story 11.2 se titula «Tipografía y datos literales» sin citar UX-DR-6. No es un defecto de
  cobertura: Story 11.2 AC2 cubre la parte de «dato literal en forma exacta» de UX-DR-6 y Story
  11.4 AC1 cubre la parte de microcopia. Se registra solo para que la revisión posterior no lo
  confunda con una omisión.
- **Corrección propuesta:** ninguna, o añadir la cita de UX-DR-6 al AC2 de Story 11.2 para que la
  traza sea explícita.

---

## Resumen

| Severidad | Cantidad | IDs |
| --- | --- | --- |
| Crítica | 0 | — |
| Alta | 0 | — |
| Media | 3 | M-1, M-2, M-3 |
| Baja | 2 | B-1, B-2 |

Todos los hallazgos son aplicables sin decisión de producto: son cierres de cobertura y de
trazabilidad dentro del contrato ya aprobado por el PRD, la UX y la arquitectura.

### Verificaciones mecánicas ejecutadas

| Verificación | Resultado |
| --- | --- |
| Mención de cada FR-1..FR-11 | PASS — mínimo 13 menciones |
| Mención de cada NFR-1..NFR-10 | PASS — mínimo 4 menciones |
| Mención de cada UX-DR-1..UX-DR-45 | PASS — mínimo 2 menciones |
| Mención de cada AD-1..AD-32 | PASS — 0 con cero menciones |
| Secuencia de IDs de historia 1.1..13.1 | PASS — 47 historias, numeración continua sin huecos |
| Presencia de `**Given**` en cada historia | PASS — 47/47; mínimo 3 AC por historia, 243 en total |
| Secciones de plantilla (`Overview`, `Requirements Inventory`, `FR Coverage Map`, `Epic List`, `## Epic N`) | PASS — todas presentes y en orden |
| Placeholders `{{...}}` residuales | PASS — 0 |
| `verify_epics.py` (verificador oficial de trazabilidad) | PASS — `ok: true`, `total_findings: 0`, `stories: 47`, `acceptance_criteria: 243`, `ar_definitions: 25`, `ar_table_rows: 25` |
| Barrido de codificación (UTF-8 válido, CJK, U+FFFD) | PASS — 0 CJK, 0 U+FFFD, 0 archivos con UTF-8 inválido |
| Barrido de placeholders (`{{token}}`, `TODO`/`TBD`/`FIXME`/`XXX`, `<MAYUSCULAS>`) | PASS — 0 |
| Barrido de marcadores de énfasis `**` desbalanceados | PASS — par |
| Caracteres de control ASCII no permitidos | PASS — 0 |

---

## Bloque de resolución (cierre documental de Fase E)

Este bloque registra cómo queda cerrado cada hallazgo de esta revisión, y contra qué evidencia
del artefacto. **No añade ni modifica ningún AC**: el cierre es documental y verificable contra
el texto ya aprobado.

### E-TRACE — Trazabilidad de las Additional Requirements — **RESUELTO**

- **Origen:** los hallazgos E-3 (editorial) y H-1 (adversarial) de este mismo lote, referidos a
  AR-21 y AR-25.
- **Resolución:** `epics.md` incorpora una sección propia
  `### Trazabilidad de las Additional Requirements (AR)` con una tabla de 25 filas (AR-1..AR-25) y
  la columna «AC que la cita», que declara **la AC concreta que hace exigible cada AR**. Las AR ya
  no se trazan de forma dispersa en el cuerpo del documento: se auditan de un vistazo.
- **Evidencia:** la tabla declara 25 filas y la columna cita AC reales en las 25. La verificación
  mecánica `verify_epics.py` confirma `ar_definitions: 25` y `ar_table_rows: 25`, y no emite
  ningún hallazgo `ar_table_ref`, `ar_table_missing`, `ar_citation` ni `ar_unlisted`
  (`total_findings: 0`).
- **Cierre de AR-21 y AR-25:** ambas quedan citadas **dentro de AC que las hacen exigibles**:
  AR-21 en Story 1.1 AC7 y Story 13.1 AC1 (clasificación de niveles de prueba y exclusión
  explícita del caso P2P del conjunto obligatorio del MVP), y AR-25 en Story 12.1 AC8 y Story 12.2
  AC5 (carácter bloqueante/no bloqueante declarado como regla exigible, no descriptiva). Con ello,
  E-3 y H-1 quedan cerrados: ninguna AR se queda como definición sin historia que la verifique.

### AR-25 — Carácter normativo de los spikes — **RESUELTO**

- **Origen:** E-3 (editorial) y H-1 (adversarial), sobre el carácter de los spikes declarado solo
  en la definición de AR-25.
- **Resolución:** el carácter de cada spike queda **declarado dentro de su propia AC** y no solo en
  la definición del inventario. S-2 (Story 12.1 AC8) queda como **BLOQUEANTE para el cierre del
  MVP**; S-1, S-3, S-4 y S-5 (Story 12.2..12.5) quedan como **NO BLOQUEANTES**, con la regla de que
  su resultado no modifica ningún artefacto del MVP.
- **Evidencia:** las filas AR-25 de la tabla de trazabilidad son `12.1 AC8, 12.2 AC5`, y ambas AC
  citan `AR-25` junto con `R-MVP-5` y `AD-21`. `verify_epics.py` no reporta ningún hallazgo de
  cita inconsistente (`ar_citation: 0`).
- **Efecto de alcance:** ninguno. La resolución hace exigible lo que AD-21 ya fijaba; no introduce
  capacidad nueva ni cambia el carácter de ningún spike.

### R-MVP-8 — Fuente de verdad de la microcopia del resultado `Paquete retirado del catálogo` — **RESUELTO**

- **Origen:** D-2 (adversarial), clasificado allí como divergencia **ALTA**: `Paquete retirado del
  catálogo` no es un valor de `DownloadState` ni de `InstallationState`, de modo que no tiene
  entrada en `StateDisplayMap` ni en `ErrorCatalog`, y ninguna AC decía en cuál de las dos vive ni
  que no se presenta como fallo.
- **Resolución:** la microcopia de ese resultado queda **asignada a la tabla única que le corresponde
  en `Shared`** y se declara, mediante AC, que **no es un fallo** y que **no se persiste como
  `ClientError`**.
- **Evidencia:** Story 3.1 AC7 y Story 9.2 AC6 citan `R-MVP-8` (junto con `AR-8`, `AD-25`,
  `AD-26`), y la nota de resolución «Resolución R-MVP-8» queda en `epics.md` junto a la tabla de
  trazabilidad de las AR. Con ello D-2 queda cerrada **sin ampliar el alcance del MVP**: el
  resultado sigue siendo distinto del fallo (AD-26) y ahora tiene fuente de verdad declarada
  (R-MVP-7, R-MVP-8, AD-25).

### Estado del conjunto de la revisión

| Hallazgo | Severidad original | Estado |
| --- | --- | --- |
| M-1 (UX-DR-26 sin AC de teclado) | Media | Cerrado en `epics.md` (Story 8.2) |
| M-2 (NFR-2 y NFR-6 sin criterio comprobable) | Media | Cerrado en `epics.md` (separación verificabilidad/medición) |
| M-3 (vocabulario P2P en Story 12.5) | Media | Cerrado en `epics.md` (evidencia de spike, no requisito) |
| B-1 (sin historia de cierre E2E del MVP) | Baja | Cerrado en `epics.md` (Epic 13 / Story 13.1) |
| B-2 (cita de UX-DR-6 en Story 11.2) | Baja | Cerrado en `epics.md` (nota de trazabilidad) |
| E-TRACE (trazabilidad de las AR) | — | **RESUELTO** en este bloque |
| AR-25 (carácter normativo de los spikes) | — | **RESUELTO** en este bloque |
| R-MVP-8 (microcopia del resultado de catálogo) | — | **RESUELTO** en este bloque |

**Checkpoint E:** sigue **pendiente de aprobación humana**. Este bloque documenta el cierre
técnico; no lo aprueba.
