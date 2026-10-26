---
status: final
checkpoint:
  id: E
  scope: Aprobación del backlog inicial de la Fase E (13 épicas, 47 historias, 243 criterios de aceptación)
  status: approved
  approvedBy: user
  approvedOn: '2026-10-26'
  approvalNote: Aprobación humana y explícita del usuario; no queda implícita en el estado final del documento.
  nextCheckpoint: Sprint Planning (Fase F)
stepsCompleted:
  - step-01-validate-prerequisites
  - step-02-design-epics
  - step-03-create-stories
  - step-04-review
  - step-05-closure
inputDocuments:
  - _bmad-output/planning-artifacts/briefs/brief-lan-party-hub-2026-10-26/brief.md
  - _bmad-output/planning-artifacts/prds/prd-lan-party-hub-2026-10-26/prd.md
  - _bmad-output/planning-artifacts/ux-designs/ux-lan-party-hub-2026-10-26/DESIGN.md
  - _bmad-output/planning-artifacts/ux-designs/ux-lan-party-hub-2026-10-26/EXPERIENCE.md
  - _bmad-output/planning-artifacts/architecture/architecture-lan-party-hub-2026-10-26/ARCHITECTURE-SPINE.md
  - docs/plan.md
---

# LAN Party Hub - Epic Breakdown

## Overview

Este documento proporciona el desglose completo de épicas e historias de LAN Party Hub,
decomponiendo los requisitos del PRD, del contrato UX (`DESIGN.md` + `EXPERIENCE.md`) y de las
decisiones de arquitectura (`ARCHITECTURE-SPINE.md`, AD-1..AD-30) en historias implementables y
verificables.

**Fase:** E — Épicas e historias. **Base:** commit `bde708d97766dfc607528abb9d3a28bc82487f0e`
(arquitectura con Checkpoint D aprobado). **Checkpoint E:** **aprobado por el usuario el
2026-10-26**. **Siguiente checkpoint:** Sprint Planning (Fase F).

**Cierre documental de Fase E:** 13 épicas, 47 historias y 243 criterios de aceptación. Las revisiones
`reviews/review-coverage-traceability.md`, `reviews/review-adversarial.md` y
`reviews/review-editorial.md` quedan actualizadas con los conteos reales y las resoluciones
E-TRACE, AR-25 y R-MVP-8. La verificación mecánica final de `verify_epics.py` devuelve `ok: true`,
`total_findings: 0`.

**Cierre del Checkpoint E (aprobación humana del backlog inicial):** el usuario aprobó
explícitamente el backlog inicial de la Fase E —13 épicas, 47 historias y 243 criterios de
aceptación— el **2026-10-26**. La aprobación es **humana y explícita**: no queda implícita en el
estado `final` del documento ni en el mero cierre de la fase. El alcance aprobado es exactamente el
contenido vigente de este documento; esta entrada **no modifica** ninguna historia, criterio de
aceptación, epic count ni story count. El siguiente checkpoint es **Sprint Planning (Fase F)**,
donde el backlog aprobado se desglosa en tareas de implementación. Ningún otro checkpoint queda
aprobado por esta entrada.

**Cierre por AD-30 (nombres de equipo únicos y validados por el servidor):** Story 9.1 incorpora
los criterios AC5..AC10 —persistencia por `clientId`, resolución de colisión con nombre único
derivado, rechazo de nombre mal formado, diagnóstico diferenciado, renombrado del equipo y
exposición del nombre efectivo—; la fila de la tabla de trazabilidad de AR-14 pasa a
`1.3 AC2, 1.4 AC1, 5.2 AC8, 9.1 AC5..AC10`, y las de AR-4 y AR-20 incorporan `9.1 AC7` y
`9.1 AC8` respectivamente. El hallazgo **D-5** de `reviews/review-adversarial.md` se reabre y se
resuelve: la unicidad del `machineName` deja de ser una ambigüedad del backlog y pasa a ser una
regla exigible de la arquitectura (**AD-30**), con el recuento real de AC del documento
(**13/47/239** — cifra **histórica** de ese corte de cierre, anterior a la propagación de AD-31 y
AD-32; el recuento **vigente** del documento es **13/47/243**).

**Cierre por AD-31 y AD-32 (derivación determinista y contrato único del registro):** Story 9.1
incorpora los criterios AC11..AC14 —derivación determinista y acotada del nombre efectivo
(sufijo fijo `-` + los **32 dígitos hexadecimales** del `clientId`, `stem` recortado a **≤ 31
puntos de código**, ≤ 64 puntos de código en total, truncación que no parte caracteres,
unicidad garantizada por construcción y **sin búsqueda**),
cálculo de la forma canónica, forma exacta del cuerpo y de la
respuesta del registro, y contrato de estado HTTP (`200` en el éxito, `422` ante nombre mal
formado, `426` ante versión incompatible) con la colisión como éxito y la reconexión sin
derivación—; la fila de la tabla de trazabilidad de AR-14 pasa a `9.1 AC5..AC14`. Se añade la
restricción heredada **R-MVP-10** (punto de entrada único de registro, derivación determinista y
acotada, colisión no es error), gobernada por AD-31 y AD-32. El recuento real de AC del
documento pasa a **13/47/243**, que es el recuento **vigente** del documento (el **13/47/239** del
cierre por AD-30 queda como cifra **histórica** de ese corte).

### Alcance y límites de esta fase

Este documento **no** es sprint planning, ni scaffold, ni código, ni configuración de CI. No
contiene Estimates de puntos, niassignaciones de sprint, ni backlog grooming. Las épicas y
historias son el artefacto de planificación de producto; el desglose en tareas de implementación
corresponde a la Fase F.

### Restricciones heredadas que gobiernan todo el desglose

Estas restricciones proceden de la arquitectura aprobada (Checkpoint D) y son **normativas** para
cada historia de este documento:

| # | Restricción | Origen | Historias que la aplican |
| --- | --- | --- | --- |
| R-MVP-1 | El MVP tiene **exactamente un** camino de descarga: HTTP servidor-cliente (`HttpRangeTransferEngine`). No existe `BitTorrentTransferEngine`, ni módulo `Tracking`, ni endpoint `announce`, ni registro de swarm, ni seeding entre clientes, ni `chunkMap`, ni dependencia de `MonoTorrent`. | AD-4, AD-13, AD-28 | 1.1, 5.1, 5.3, 12.5 |
| R-MVP-2 | **No hay autenticación ni autorización de clientes.** El único control de acceso al protocolo es la **compatibilidad de versión** (`X-Client-Version` / `X-Protocol-Version`, respuesta `426 Upgrade Required`). Ninguna superficie de UI muestra credenciales ni pide inicio de sesión. | AD-6, AD-12; PRD FR-4; UX *Modelo de acceso* | 4.3, 9.3 |
| R-MVP-3 | El **ancla de confianza se fija out-of-band** (QR del organizador o contraste visual en la pantalla del organizador). El fingerprint **no** se propaga por mDNS, ni por el handshake de SignalR, ni por `GET /api/v1/server/info` como valor a aceptar sin contrastar. El rechazo **precede a persistir**, en tres niveles con `code` distinto: ancla sustituida / firma inválida / `sha256` divergente. | AD-2, AD-27 | 4.4, 6.1, 7.1, 12.1 |
| R-MVP-4 | La instalación es **solo modo portable**. Los modos `Installer` y `External` quedan fuera del MVP. | PRD FR-8, §6; AD-7 | 7.3, 10.2 |
| R-MVP-5 | Los spikes **S-1..S-5** son experimentos acotados con criterios de éxito observables, **no producto**. Solo **S-2** es bloqueante (condiciona el **cierre** del MVP, no su construcción). Ningún spike puede convertirse en capacidad del MVP ni propagarse por edición. | AD-21 | 12.1, 12.2, 12.3, 12.4, 12.5 |
| R-MVP-6 | El vocabulario del MVP es «servidor y cliente», no «peer». El modelo de dominio no menciona peers, swarms ni fragmentos. | AD-28 | 5.1 |
| R-MVP-7 | `Shared` contiene **solo contratos**, con la regla «un concepto, un tipo, un lugar». `StateDisplayMap` y `ErrorCatalog` son tablas **únicas y exhaustivas**. `ApiError` es el **único** tipo de error del sistema. | AD-18, AD-25 | 1.1, 3.3, 6.2, 9.2, 11.4 |
| R-MVP-8 | La microcopia de estado y los errores visibles al usuario salen **solo** de `StateDisplayMap` y `ErrorCatalog`. Un texto que no está en la tabla no existe. | AD-10, AD-15, AD-25 | 3.1, 3.3, 6.2, 9.2, 11.4 |
| R-MVP-9 | La vista de clientes se obtiene **exclusivamente** por REST (`GET /api/v1/clients`). No existe el evento `ClientsSnapshotChanged`. | AD-29 | 4.5, 9.1 |
| R-MVP-10 | El **alta, la reconexión y el renombrado** del nombre del equipo ocurren en **una sola operación de registro**, que es el **único** punto de entrada del cliente al servidor (`POST /api/v1/clients`). No existe un endpoint adicional de registro, de renombrado ni de administración de nombres. El nombre efectivo derivado ante una colisión es **determinista y acotado** (`stem_truncado` de **≤ 31 puntos de código** + sufijo fijo `-` + los **32 dígitos hexadecimales** del `clientId` en forma `Guid.ToString("N")`, ≤ 64 **puntos de código Unicode**, truncación que **no parte** ningún carácter) y la unicidad está **garantizada por construcción**, **sin búsqueda**. La colisión **no es un error**: el registro tiene éxito con `renamed: true` y el nombre efectivo. | AD-31, AD-32 | 9.1 |

## Requirements Inventory

### Functional Requirements

FR-1: El administrador del servidor puede registrar y modificar videojuegos con identificador
único, nombre, descripción, categorías, etiquetas, carátula, imágenes, versión, tamaño, jugadores
mínimo y máximo, sistema operativo, tipo de distribución, tipo de instalación, configuración de
lanzamiento, dependencias, licencia y restricciones de distribución.

FR-2: El cliente sincroniza automáticamente los cambios del catálogo y permite buscar, filtrar y
categorizar los videojuegos disponibles; si el servidor no está disponible, conserva el último
catálogo válido y comunica el estado de sincronización pendiente.

FR-3: El cliente puede descubrir automáticamente el servidor dentro de la red local (mDNS del
servicio `_lanpartyhub._tcp`) y también aceptar una dirección introducida manualmente; ante una
desconexión, intenta reconectar y sincroniza los cambios pendientes.

FR-4: El servidor verifica la versión del cliente antes de establecer una sesión operativa. Los
clientes compatibles pueden consultar el catálogo, descargar, instalar y reportar estado; los
incompatibles no pueden iniciar esas operaciones y reciben un diagnóstico explícito con la versión
del cliente y la del servidor. Esta verificación no autentica ni autoriza clientes.

FR-5: Un cliente compatible puede descargar un paquete del servidor, con seguimiento de progreso,
pausa, reanudación y recuperación tras desconexiones o reinicios.

FR-6: El cliente valida la integridad (`sha256`) y la autenticidad (firma Ed25519 sobre el
manifiesto de instalación canónico) del paquete antes de instalarlo, distingue corrupción de
autenticidad, y no continúa la instalación si la validación falla.

FR-7: El camino principal de descarga del MVP es servidor-cliente; el dominio de distribución
permite más de un mecanismo sin acoplar los requisitos a una biblioteca o protocolo concreto. El
intercambio real entre clientes no se presenta como capacidad disponible hasta que la PoC lo
demuestre.

FR-8: Un cliente puede extraer un paquete validado en un directorio configurable, detectar una
instalación existente, reparar cuando sea viable y comunicar errores y reintentos; las operaciones
del manifiesto son tipadas y las privilegiadas requieren confirmación explícita.

FR-9: Un cliente puede detectar el ejecutable de un juego instalado, aplicar sus argumentos de
lanzamiento, establecer el directorio de trabajo, detectar el proceso en ejecución y mostrar errores
comprensibles.

FR-10: El servidor muestra equipos conectados, identificador y nombre, estado de conexión, juegos y
versiones instalados, descargas activas, progreso de instalaciones y errores registrados.

FR-11: El cliente detecta cuando dispone de una versión antigua y puede obtener una versión
completa válida del catálogo, sustituyendo la anterior conforme al manifiesto.

### NonFunctional Requirements

NFR-1: **Red local autónoma.** El flujo completo del MVP funciona sin conexión a Internet; la
ausencia de Internet no invalida el catálogo ya sincronizado ni las operaciones locales.

NFR-2: **Escala inicial.** El diseño admite el objetivo de 8 a 20 jugadores concurrentes en una
red Ethernet de 1 Gbps. Es una hipótesis de producto: no se promete velocidad de transferencia
hasta validar rendimiento (S-4).

NFR-3: **Paquetes de varios gigabytes.** Descargas e instalaciones soportan paquetes de varios GB
y persisten el progreso ante reinicios.

NFR-4: **Plataforma inicial.** El entorno inicial es Windows 10/11; la evolución multiplataforma se
conserva como posibilidad, no como alcance del MVP.

NFR-5: **Seguridad de contenido.** El cliente no instala paquetes cuya integridad o autenticidad no
ha sido validada; los manifiestos restringen las acciones a operaciones tipadas y protegen el
sistema frente a extracción fuera de los directorios autorizados.

NFR-6: **Uso de recursos.** El servidor y el cliente mantienen un consumo moderado de CPU y
memoria; los límites concretos se fijan con pruebas (S-4), sin inventar umbrales.

NFR-7: **Diagnóstico.** El sistema produce logs estructurados y permite asociar errores a cliente,
descarga, instalación o lanzamiento.

NFR-8: **Licencias.** La publicación de paquetes respeta la información de licencia y las
restricciones de distribución registradas para cada videojuego.

NFR-9: **Resiliencia operativa.** El producto contempla desconexiones de clientes, servidor
temporalmente inaccesible, instalación incompleta y reinicios durante operaciones, con recuperación
o error explícito y recuperable.

NFR-10: **Sin cloud obligatorio.** El servidor opera sin infraestructura cloud y el MVP no depende
de trackers, DHT ni servicios externos públicos.

### Additional Requirements

Requisitos técnicos derivados de la arquitectura (AD-1..AD-30) que condicionan la
implementación. Se expresan como requisitos exigibles, no como decisiones de diseño.

- **AR-1 (Clean Architecture modular).** Ningún tipo de `Domain` importa `Infrastructure`, EF Core,
  Avalonia, HTTP ni bibliotecas de transferencia. Los casos de uso dependen de puertos declarados
  en `Application`. Los módulos se comunican por contratos en `Shared` y casos de uso publicados;
  el acceso a datos de otro módulo está prohibido. El servidor es un monolito modular en un único
  proceso y un único artefacto desplegable. *(AD-1)*
- **AR-2 (Contratos API).** API REST sobre ASP.NET Core con prefijo `/api/v1` y campo
  `schemaVersion` en el cuerpo de toda mutación. SignalR se usa **exclusivamente** para push de
  estado. El cliente no consulta el estado de otro cliente por *polling*. Los DTOs son de `Shared`
  y son la forma congelada del contrato. *(AD-5, convención `Contratos API`)*
- **AR-3 (Forma de los contratos de `Shared`).** `Shared` contiene solo contratos, con la
  enumeración exhaustiva de AD-18: `ApiError`, `ErrorCatalog`, `DownloadState`,
  `InstallationState`, `StateDisplayMap`, `CatalogViewState`, `ClientStateChanged`,
  `DownloadProgressUpdated`, `InstallationProgressUpdated`, `ClientErrorReported`,
  `CatalogChanged`, `ClientRegistrationView`, `TrustAnchor`, `PackageDescriptor`,
  `InstallationManifest` + sobre firmado, `VersionInfo`. Ninguno de estos tipos puede declararse
  también en `Server` o `Client`. *(AD-18)*
- **AR-4 (Formato de error único).** `ApiError` = `code` + `message` + `cause` + `nextStep`, con
  `cause` y `nextStep` **obligatorios**. Es el único tipo serializable en un cuerpo de error de la
  API REST y el único que puede viajar en `ClientErrorReported`. La estructura de tres partes es
  obligatoria en el mensaje de error de operación, en `toast-error` y en el detalle del error
  registrado en Diagnóstico. *(AD-15, AD-25)*
- **AR-5 (Microcopia de estado única).** Existe un único `StateDisplayMap` en `Shared` que traduce
  cada valor de `DownloadState` e `InstallationState` a su texto de interfaz. `sincronizando`,
  `disponible` e `instalado con versión desactualizada` son **estados de vista derivados** con regla
  de derivación fija; `error` es la proyección de los estados terminales de fallo, no un estado de
  máquina. La comparación de versiones usa el orden semántico declarado en el catálogo, no la
  comparación lexicográfica. *(AD-10, AD-25)*
- **AR-6 (Paquetes inmutables).** `PackageDescriptor` = `packageId`, `gameId`, `version`,
  `sizeBytes` (exacto), `chunkSizeBytes` (informativo y opcional en HTTP), `sha256`,
  `manifestRef`. **No transporta firma.** Un paquete publicado es inmutable: cambiar su contenido
  exige publicar una versión nueva. `sizeBytes` se almacena en bytes exactos; la conversión a GB
  decimales es una regla de presentación de UX. *(AD-7, AD-24, convención `Data & formats`)*
- **AR-7 (Manifiesto canónico y firmado).** `InstallationManifest` = `schemaVersion`,
  `packageId`, `packageVersion`, `sizeBytes`, `sha256`, `launch` (ruta, argumentos, directorio de
  trabajo) y `operations[]` tipadas y cerradas (`extract`, `createDirectory`, `copyFile`,
  `writeConfig`, `setLaunchConfig`, `verify`). **No existe ninguna operación de ejecución de
  comandos arbitrarios.** El manifiesto firmado es la raíz de verificación: sobre JSON canónico,
  Ed25519, en un sobre `{ manifest, signature, keyId }`. *(AD-2, AD-7)*
- **AR-8 (Orden normativo de una instalación).** (1) obtener el manifiesto firmado; (2) verificar
  firma y `schemaVersion`; (3) **solo entonces** crear el directorio de instalación y el archivo
  parcial; (4) descargar el contenido; (5) calcular el `sha256` y compararlo con el del manifiesto
  firmado; (6) si no coincide, descartar el archivo parcial y marcar la operación como fallida, no
  reparable por reanudación. Ningún byte se escribe en disco antes del paso 2, y
  `POST /api/v1/downloads` no inicia la escritura en disco. *(AD-8)*
- **AR-9 (Confines de extracción).** Toda escritura de instalación se resuelve contra la raíz del
  directorio de instalación configurado y se verifica que la ruta canónica resuelta (tras resolver
  enlaces simbólicos) quede dentro. La comprobación se repite **antes de cada escritura**. El
  manifiesto no admite rutas absolutas. *(AD-9)*
- **AR-10 (Layout en disco del cliente).** Bajo `%LOCALAPPDATA%\LanPartyHub`: `install/` (raíz de
  instalación configurable, único directorio confinado), `downloads/` (descargas parciales, ruta
  fija y persistente, **fuera** de `install/`), `manifests/` (caché de manifiestos firmados),
  `state.sqlite` (base de datos del cliente). Los `*.part` viven **solo** en `downloads/`, nunca
  en `%TEMP%` ni en la raíz de instalación. *(AD-9, AD-11, AD-17)*
- **AR-11 (Progreso persistido).** El camino HTTP persiste un **offset acumulado**
  (`bytesPersisted`, bytes válidos contiguos desde el inicio del parcial) en tabla propia. **No hay
  mapa de fragmentos ni columna `chunkMap`.** La reanudación usa `Range` sobre `bytesPersisted`. El
  progreso efectivo al arrancar es la intersección de lo persistido y lo que el archivo parcial
  contiene. Un registro cuyo `transferEngine` no exista en la versión activa se rechaza al
  arrancar. *(AD-11, AD-24)*
- **AR-12 (Persistencia por rol).** SQLite + EF Core, **un** archivo por rol. Las migraciones de EF
  Core son versionadas y se aplican al inicio. El esquema de cada archivo es propiedad de su rol:
  el del servidor no se replica en el cliente. *(AD-11)*
- **AR-13 (Propietario de cada entidad).** Servidor: `Game`, `PackageDescriptor`,
  `InstallationManifest` (+ sobre firmado), `DownloadOperation` (reflejo de la intención, **no** el
  estado), `ClientRegistration`, `ClientInstallationView` (proyección de solo lectura),
  `ClientError`. Cliente: `ClientInstallation`, `DownloadOperation` (estado real),
  `DownloadProgress`, `ClientIdentity`. El servidor **no** persiste el `InstallationState` ni el
  `DownloadState` de otro cliente: si los guarda, es como `validationStateText`, cadena de
  presentación copiada del `StateDisplayMap`. `installPath` es una ruta del cliente y no sale del
  cliente. *(AD-22)*
- **AR-14 (Identidad del cliente y nombre del equipo).** El `clientId` es un UUID v7 opaco que el
  cliente **emite una sola vez** y **persiste** en `ClientIdentity`. No se regenera al arrancar, al
  reinstalar el binario mientras sobreviva el archivo de datos, ni al cambiar de versión. El
  `clientId` es la **clave técnica**: identifica la fila de `ClientRegistration`, la deduplica y
  correlaciona los logs, y **no cambia** ante un rechazo por nombre, ante una colisión resuelta ni
  ante un cambio de nombre del equipo. Un `clientId` desconocido es un cliente nuevo con ese
  `clientId`.
  El `machineName` es la **etiqueta humana** del equipo: el cliente la **propone** y el **servidor la
  valida y la hace única** en el momento del registro (AD-30). La unicidad del nombre es una
  **restricción de una columna**, no un mecanismo de deduplicación: el servidor **no** agrupa, no
  fusiona ni reasigna filas por nombre (AD-23). Ante una colisión con otro equipo ya registrado, el
  servidor **no rechaza ni oculta** al segundo cliente: le asigna un **nombre único derivado** del
  propuesto, lo **persiste** y se lo **notifica**, y el cliente lo **adopta**; los dos equipos
  coexisten en la tabla con **nombres efectivos distintos**. Un `machineName` que no cumple las
  reglas de forma se **rechaza en el registro** y el cliente **queda sin registrar y no opera**.
  La **derivación del nombre efectivo es determinista y acotada** (AD-31): el nombre efectivo es
  `stem_truncado` (recortado a **≤ 31 puntos de código**) + el sufijo fijo `-` seguido de los **32
  dígitos hexadecimales en minúsculas** de la forma canónica del `clientId` (`Guid.ToString("N")`),
  la longitud se mide en **puntos de código Unicode** y
  **nunca excede 64**, el recorte **no parte ningún carácter por la mitad** y el nombre derivado
  **vuelve a ser admisible**, **nunca necesita volver a ser derivado** y su **unicidad está
  garantizada por construcción**, sin búsqueda de nombres libres. El **alta, la
  reconexión y el renombrado** ocurren en **una sola operación de registro**, que es el **único**
  punto de entrada del cliente al servidor (AD-32): cuerpo `{ clientId, machineName,
  clientVersion, protocolVersion }`, respuesta `{ clientId, machineName, connectionState,
  lastSeenAt, renamed }` con el **nombre efectivo**, `200 OK` en el éxito, `422` ante nombre mal
  formado y `426` ante versión incompatible; la **colisión no es un error** (`renamed: true` con
  éxito) y la **reconexión conserva el nombre** sin derivar. Esta regla **no introduce
  credenciales, autenticación ni autorización**: el `machineName` **no**
  viaja como credencial, no autentica ninguna petición y no confiere roles ni permisos; el único
  control de acceso al protocolo sigue siendo la compatibilidad de versión (AD-6, AD-12).
  *(AD-23, AD-30, AD-31, AD-32)*
- **AR-15 (Estados de máquina).** `DownloadState` e `InstallationState` son enumeraciones cerradas
  con transiciones explícitas y estado terminal por operación, **idénticas** en cliente y servidor.
  El cliente es la única fuente de verdad del estado local de una operación; el servidor lo
  proyecta. Toda transición inválida se rechaza. *(AD-10)*
- **AR-16 (Consistencia del catálogo).** El catálogo se sincroniza por **snapshot completo**
  (`GET /api/v1/catalog` con `catalogVersion` monótono creciente). Un snapshot nuevo **no cancela**
  operaciones en curso; una operación cuyo paquete o juego fue retirado **termina** con resultado
  explícito `Paquete retirado del catálogo` (distinto del fallo); una instalación existente **no se
  borra**. El cliente solo aplica un snapshot con versión mayor que la que tiene. *(AD-26)*
- **AR-17 (Superficie de red mínima).** El servidor escucha **solo** en interfaces privadas y abre
  **solo** HTTP (API + SignalR) y mDNS (UDP 5353). **No se exige TLS** en la LAN del MVP: la
  autenticidad la aporta la firma, no el canal. *(AD-12, AD-28)*
- **AR-18 (Fijación del ancla de confianza).** Orden normativo: (1) obtener el fingerprint esperado
  out-of-band; (2) obtener del servidor la clave pública del ancla y calcular su fingerprint;
  (3) comparar — si no coinciden, **rechazar**; (4) persistir el ancla fijada junto con el
  fingerprint esperado; (5) solo a partir de aquí validar manifiestos firmados. Estados de
  conexión del cliente: `AnchorVerified`, `AnchorUnverified`, `Disconnected`. En `AnchorUnverified`
  el cliente no descarga ni instala y la UI lo dice explícitamente. Un ancla fijada es inmutable
  durante el evento. *(AD-27)*
- **AR-19 (Despliegue).** Publicación **self-contained single-file** por rol (servidor, cliente)
  para Windows x64, más un paquete portable `zip` equivalente. El servidor guarda su estado en una
  carpeta de datos configurable (`--data-dir`); el cliente, en `%LOCALAPPDATA%\LanPartyHub`. **Sin
  instalador, sin registro, sin servicio de Windows, sin cloud.** *(AD-17)*
- **AR-20 (Observabilidad).** Logging estructurado **JSON** con `Microsoft.Extensions.Logging`, con
  correlación obligatoria por `operationId` y `clientId` propagados a través de la frontera
  servidor-cliente. Las métricas se exponen localmente vía el endpoint de métricas del host. Ningún
  log contiene credenciales (no hay). El servidor traduce la excepción de dominio al `code` del
  `ErrorCatalog`; el cliente presenta `message`, `cause` y `nextStep` tal cual. Un `code`
  desconocido se muestra con el texto genérico del catálogo. *(AD-15)*
- **AR-21 (Estrategia de pruebas).** xUnit en tres niveles: **unitarias** (dominio, máquinas de
  estado, validación de manifiesto, verificación del ancla de confianza, confines de ruta),
  **integración** (SQLite real en archivo, endpoints con `WebApplicationFactory`, motor de
  transferencia HTTP contra servidor de prueba) y **E2E en LAN real** con dos o más clientes. La
  checklist de `docs/plan.md` §10 gobierna las pruebas obligatorias y se clasifica por nivel
  mínimo de detección; el caso «cliente compartiendo fragmentos con otros» **pertenece a la PoC
  P2P y sale del conjunto obligatorio del MVP**. *(AD-16, AD-28)*
- **AR-22 (Lanzador aislado).** `GameLauncher` es un módulo del cliente con la interfaz
  `IGameProcessRunner`. Lanza el ejecutable con el working directory y los argumentos del catálogo,
  valida la instalación antes de lanzar y expone si el proceso **está en ejecución**. La conexión
  directa a servidores de juego queda como extensión con el puerto preparado y **no
  implementada**; los launchers externos quedan fuera del MVP. *(AD-19)*
- **AR-23 (Configuración y precedencia).** Configuración por archivo + variables de entorno + flags
  de línea de comandos, con precedencia `flags > env > archivo`. El servidor se configura por
  `--data-dir` y `--http-port`; el cliente, por `serverAddress`, `installDirectory` y `theme`.
  **No existe flag ni selector de motor P2P en el MVP.** *(AD-12, AD-17, convención
  `Configuración y red`)*
- **AR-24 (Concurrencia).** Cada operación de larga duración es una unidad de trabajo con su
  `operationId` y su registro de estado persistido. Las operaciones que tocan el mismo recurso se
  serializan **por recurso**, no globalmente. Los límites de concurrencia por cliente
  (descargas simultáneas, conexiones totales) son configurables con valor por defecto, y el reparto
  del enlace en el MVP HTTP es equitativo entre los clientes que descargan a la vez. *(AD-14,
  convención `Concurrencia`)*
- **AR-25 (Spikes con carácter normativo).** Cada spike tiene objetivo, alcance mínimo, criterios
  de éxito observables, resultado esperado y qué decisión cierra, y su carácter bloqueante /
  no bloqueante es normativo. **S-2 es BLOQUEANTE** (su resultado es pre requisite de FR-6 y
  SM-3; si falla, el MVP no se puede cerrar). **S-1, S-3, S-4 y S-5 son NO BLOQUEANTES**; su
  resultado es una decisión de fase posterior y puede no ejecutarse, ejecutarse tarde o fracasar
  sin que ningún artefacto del MVP cambie. Un spike sin carácter declarado es un hueco. *(AD-21)*

### UX Design Requirements

Requisitos de diseño extraídos del contrato UX par (`DESIGN.md` — identidad visual e identidad
técnica; `EXPERIENCE.md` — arquitectura de información, comportamiento, estados, interacción,
accesibilidad y flujos). Cada UX-DR es específico y acotado para generar historias con criterios
de aceptación verificables.

**Tokens e identidad visual (DESIGN.md).**

UX-DR-1: **Sistema de tokens cerrado.** La interfaz se construye sobre los tokens de `DESIGN.md`:
4 superficies (base `#12141A`, raised `#1B1E26`, overlay `#232733`, sunken `#0D0F13`), 5 niveles de
tinta, 2 acentos con función (`accent-lan` = sistema sano, `accent-action` = acción disponible),
4 colores de estado (progress, success, warning, danger), línea de pelo `#282C37` y borde fuerte
`#3A3F4D`, más el juego de tokens `-light` (`surface-base-light` `#F7F8FA`, `ink-primary-light`
`#14161C`, `ink-secondary-light` `#5A6172`, `accent-lan-light` `#127A57`,
`accent-action-light` `#2F5FE0`, `border-hairline-light` `#E2E5EB`, `state-success-light`
`#127A57`, `state-warning-light` `#8A5A00`, `state-danger-light` `#C0262E`). Ningún componente
puede introducir un token que no exista en `DESIGN.md`.

UX-DR-2: **Tipografía en dos familias con función.** `Inter` para interfaz (`display` 32/700,
`headline` 22/600, `title` 17/600, `body` 14/400, `body-strong` 14/600, `label` 12/500,
`meta` 11/500) y `Cascadia Mono` reservada a datos literales y derivados que se comparan entre sí
(`mono`, `size`, `percent`, 12/400). Rutas, hashes, versiones exactas, identificadores de paquete,
valores numéricos de tamaño y porcentaje van en monoespaciada; las unidades («GB», «%») van en
`meta`.

UX-DR-3: **Elevación tonal, sin sombras.** La profundidad se expresa con tres niveles de superficie
y línea de pelo. **Nunca** se usa sombra proyectada como indicador de *hover* ni como jerarquía,
salvo dos excepciones acotadas (diálogo modal y menú desplegable, `surface-sunken` al 40 % de
opacidad). El *hover* se marca con el paso de `surface-raised` a `surface-overlay`, o con cambio de
tono del texto.

UX-DR-4: **Geometría y espaciado.** Radios: 8 px controles, 12 px tarjetas y paneles, 16 px
diálogos, 4 px campos de texto pequeños y etiquetas, `full` solo para píldoras de estado (cápsulas
de una línea, alto 20 px) y el punto de presencia de conexión (círculo de 8 px). Escala base de
4 px con pasos 8/12/16/24/32/48. Separadores: `{spacing.gutter}` (24 px) entre columnas,
`{spacing.panel}` (16 px) dentro de un panel, `{spacing.card}` (20 px) dentro de una tarjeta o
diálogo. Ninguna superficie en cápsula; la carátula respeta exactamente las esquinas de su
contenedor.

UX-DR-5: **Regla única de tamaños y progreso.** Todos los tamaños se expresan en **GB decimales
(10⁹ bytes)** con separador decimal español (coma) y **exactamente un decimal** («7,6 GB»); el
porcentaje es siempre un **entero** derivado de los bytes transferidos sobre los totales («42 %»).
Cuando coexisten descarga y total se usa el par «<n> % · <a> GB de <t> GB», con el total siempre
segundo. Prohibido: «7,60 GB», «7 GB», «7,6 GiB», «42,3 %», «42/100», «~7,6 GB», «7.6 GB» (punto
decimal) y cualquier tamaño sin unidad explícita. La barra y el texto nunca se contradicen.

UX-DR-6: **Microcopia por estado crítico.** Textos fijos del contrato: «Descargando · 42 % · 3,2
GB de 7,6 GB», «Pausada · 42 % · 3,2 GB de 7,6 GB», «Reanudando desde 42 % · 3,2 GB de 7,6 GB»,
«Descargado · 100 % · 7,6 GB», «Paquete validado», «Paquete dañado: el hash no coincide · 7,6 GB»,
«Paquete no confiable: no se pudo validar contra la raíz de confianza · 7,6 GB», «Instalado ·
v1.2», «Instalando · 68 %», «Ya instalado · v1.2», «No se puede jugar: <causa>.», «En ejecución»,
«Este cliente (1.2) no es compatible con el servidor (1.4). Actualiza el cliente.». Los literales
(rutas, hashes, versiones) no se redondean ni se traducen; se muestran en su forma exacta.

**Comportamiento de componentes (EXPERIENCE.md — Patrones de componente).**

UX-DR-7: **`button-primary` reflects real state.** Una sola acción primaria por superficie. Se
deshabilita cuando el prerrequisito no se cumple (paquete no validado, cliente incompatible, sin
servidor). El foco pasa el borde a 2 px `{colors.accent-action}` **sin cambiar el alto** (36 px
estable), de modo que la rejilla no se deforma.

UX-DR-8: **Jerarquía de botones.** `button-secondary` actúa sin competir con la primaria y nunca
es la única acción disponible en un estado vacío; `button-danger` se reserva a cancelar una
descarga, eliminar una instalación o descartar un error, y exige confirmación solo si se pierde
trabajo no recuperable; `button-ghost` es la acción terciaria (descartar aviso, cerrar panel) y
nunca la única vía a un resultado.

UX-DR-9: **`field-input`.** Altura fija de 36 px, foco con borde de 2 px `{colors.accent-action}`
sin deformar la rejilla, acepta `Enter` como confirmación. La búsqueda **filtra al escribir con
200 ms de retardo**, sin botón «buscar».

UX-DR-10: **`card-game`.** Un clic en la tarjeta abre la ficha; las acciones secundarias viven en la
ficha. El nombre se trunca a una línea con elipsis. Muestra versión, tamaño y jugadores en una
línea de `{typography.meta}`, con el valor del tamaño en `{typography.size}` y la unidad «GB» en
`{typography.meta}`. En *hover* la superficie pasa a `surface-overlay` y el borde **no** se
refuerza.

UX-DR-11: **`panel-connection` persistente.** Panel derecho del cliente, persistente: no se colapsa
ni se esconde en menú o modal. Muestra punto de presencia, nombre del servidor, dirección en uso y
versión del servidor; abajo, la operación en curso con su barra. Si no hay operación en curso,
muestra la última operación terminada con su resultado, nunca un espacio vacío.

UX-DR-12: **`status-badge`.** Cápsula de una línea, 20 px de alto, radio `full`, fondo del color de
estado al 18 % con texto del color de estado. **El color nunca es la única señal:** siempre
acompaña a texto, y el nombre accesible de la insignia incluye su estado. Estados del contrato:
sincronizando, disponible, descargando, pausado, validado, instalado, instalado con versión
desactualizada, en ejecución, error. Nunca dos colores en la misma insignia; si un estado necesita
dos dimensiones, son dos insignias con texto.

UX-DR-13: **`progress-bar` honesta.** Refleja el progreso real derivado de los bytes transferidos
sobre los totales, igual que el porcentaje que lo acompaña. En error **conserva su longitud real**
—una barra al 100 % en rojo mentiría— y el estado lo comunica la insignia. En operación completada
verificada el relleno pasa a `{colors.state-success}`. Siempre va acompañada del texto «<n> %»; sin
texto, la barra no es accesible por sí sola.

UX-DR-14: **`data-table-row` densa.** Alto de 40 px, celdas de dato en `{typography.body-strong}` y
unidades en `{typography.meta}`, separador inferior de 1 px `border-hairline`, orden estable por
nombre de equipo, fila seleccionable para ver su detalle. La tabla de clientes del servidor es una
**tabla densa**, no una colección de tarjetas.

UX-DR-15: **`toast-error` textual.** Contenido: causa, acción intentada y siguiente paso. Sin
iconografía; el texto es el aviso. No sustituye a la persistencia del error en el registro.

UX-DR-16: **`dialog-surface` de un solo nivel.** Encabezado `headline`, cuerpo `body`, pie con
acciones alineadas a la derecha. `Esc` cancela la acción no destructiva y **devuelve el foco al
elemento invocador**. Un diálogo nunca se apila sobre otro.

**Estados, instalación y acciones (EXPERIENCE.md).**

UX-DR-17: **Tabla de estados de instalación y acción primaria.** Se aplica por igual en Biblioteca
y Ficha, y ninguna de las dos superficies abre un camino de lanzamiento distinto. Instalación
validada → «Jugar» habilitada y es la primaria; «Actualizar» si la versión está desactualizada;
«Reparar» solo bajo petición expresa. Instalación inválida **y reparable** → «Jugar» bloqueada con
el motivo visible junto a la etiqueta, primaria «Reparar», secundaria «Diagnosticar» que abre el
error registrado. Instalación inválida **y no reparable** → «Jugar» bloqueada con el motivo
visible, primaria «Descargar de nuevo», secundaria «Diagnosticar».

UX-DR-18: **El motivo del bloqueo es visible sin interactuar.** No está en un *tooltip*, no está solo
en el log de Diagnóstico y no requiere *hover* ni foco: aparece en la propia tarjeta o ficha, junto
a la etiqueta «Jugar». El *tooltip* puede repetirlo, nunca ser su único lugar. «Jugar» bloqueada no
es un error de estado, es un estado con su etiqueta, con la insignia `{colors.state-danger}`.

UX-DR-19: **No hay segunda vía de lanzamiento.** Mientras la instalación no esté validada, ninguna
otra superficie (panel de estado, ficha, error de lanzamiento) ofrece ejecutar el juego.

UX-DR-20: **«Reparar» reutiliza el paquete ya validado.** No vuelve a descargar y no pide una
confirmación que no tenga consecuencia; la interfaz nombra la consecuencia: «Se reparará la
instalación; la descarga no se repite».

UX-DR-21: **Estados de vista y su relación con el dominio.** La insignia `error` es la proyección de
los estados terminales de fallo; `sincronizando`, `disponible` e `instalado con versión
desactualizada` son estados de vista derivados, no estados de máquina. *(Consistente con AR-5,
AD-10.)*

UX-DR-22: **Cancelar una descarga nombra lo que se pierde.** Estado `{colors.state-warning}` con
acciones «Reanudar» (primaria) y «Cancelar» (peligro). Cancelar pide confirmación nombrando lo que
se pierde: «Se borrarán los 3,2 GB descargados». Nada de interrogación por culpa salvo acción que
destruya trabajo no recuperable.

**Navegación, teclado y superficies (EXPERIENCE.md).**

UX-DR-23: **Arquitectura de información del cliente.** Siete superficies: Conexión (apertura de la
app), Catálogo, Ficha de videojuego (desde la tarjeta), Descargas, Biblioteca, Configuración y
diagnóstico, y Panel de estado (columna derecha, persistente). La navegación es una **columna**,
no una barra inferior. Un solo nivel de modal.

UX-DR-24: **Arquitectura de información del servidor.** Tres secciones: Catálogo (registrar y
modificar videojuegos), Clientes (tabla densa de equipos) y Paquetes (publicar y controlar los
paquetes).

UX-DR-25: **Primitivas de teclado.** `Enter` confirma el campo enfocado (dirección del servidor,
búsqueda, directorio); `Esc` cierra el diálogo superior y, si no hay diálogo, limpia la búsqueda;
`Ctrl+F` enfoca la búsqueda del catálogo desde cualquier superficie; `Ctrl+,` abre Configuración y
diagnóstico; `Ctrl+1`…`Ctrl+4` saltan a Catálogo, Descargas, Biblioteca y Configuración;
`Espacio` pausa o reanuda la descarga enfocada; `Tab` / `Shift+Tab` recorren la superficie en orden
de lectura con foco siempre visible.

UX-DR-26: **Cobertura de teclado de las acciones de instalación.** «Jugar» bloqueada **recibe
foco** como cualquier otro control: no se omite ni se deshabilita de forma invisible; al recibir
foco lee su motivo junto a la etiqueta. `Enter` sobre «Jugar» bloqueada no produce ningún cambio
de estado y no dispara descarga ni reparación implícita. `Espacio` alterna pausa y reanudación de
la descarga enfocada, y no actúa sobre la instalación. `Ctrl+4` salta a la Biblioteca y desde ahí
`Tab` alcanza «Jugar», «Reparar» o «Descargar de nuevo» sin salir de la superficie. El control
bloqueado conserva indicador de foco visible y un estado de foco que no depende del color de la
insignia.

UX-DR-27: **Reglas de puntero.** Un clic en la tarjeta abre la ficha. El *hover* nunca es la única
vía: las acciones que solo aparecen en *hover* (descartar aviso, acciones de fila) tienen
equivalente de teclado o están siempre visibles en el detalle. Nada de desplazamiento infinito: el
catálogo pagina por scrolls de tamaño fijo con paginación explícita. El progreso no bloquea: la
navegación entre superficies es libre mientras una descarga está en curso.

**Estados vacíos, errores y recuperación (EXPERIENCE.md).**

UX-DR-28: **Estados vacíos con salida.** Catálogo vacío → «El catálogo está vacío.» + una acción:
«Sincronizar ahora» si el servidor está conectado, si no «Conectar al servidor». Sin resultados de
búsqueda → «Sin resultados para «<consulta>».» + «Quitar filtros» si hay filtros activos. Biblioteca
sin juegos → «No tienes juegos instalados todavía.» + «Ver catálogo». Ningún estado vacío sin
salida accionable.

UX-DR-29: **Estados de servidor no disponible y reconexión.** Servidor no disponible → panel derecho
en `{colors.state-warning}`: «Servidor no disponible. Se conserva el último catálogo
sincronizado.» Las acciones que requieren servidor se deshabilitan con su motivo visible.
Reconectando → punto de presencia en `{colors.state-warning}`, reintento automático con contador
visible «Reintentando · 3 s», sin modal que interrumpa.

UX-DR-30: **Descubrimiento sin resultado.** «No se encontró ningún servidor en esta red.» + acción
primaria «Introducir dirección manualmente». El descubrimiento es una vía, no una puerta única.

UX-DR-31: **Cliente incompatible.** Estado en `{colors.state-danger}` con la versión del cliente y la
del servidor, y el siguiente paso. **No hay formulario de credenciales: no las hay.**

UX-DR-32: **Catálogo desactualizado.** Aviso discreto «Catálogo desactualizado · sincronizado hace 12
min» con «Sincronizar ahora». No bloquea la navegación.

UX-DR-33: **Validación fallida con causa diferenciada.** Estado `{colors.state-danger}` con la causa
diferenciada: «dañado» (corrupción) o «no confiable» (no validable contra la raíz de confianza). El
alta de instalación queda **bloqueada** y el motivo es visible, no oculto en el log.

UX-DR-34: **Instalación en curso y fallida.** Instalación en curso → barra de progreso con fase
(«Extrayendo», «Aplicando manifiesto») y porcentaje. Instalación fallida → error persistido y
reintentable **sin reinstalar el cliente**, con la acción «Reintentar» disponible.

UX-DR-35: **Error de lanzamiento accionable.** «No se pudo iniciar: <causa>.» con el ejecutable y su
ruta exacta cuando el problema es de ruta.

UX-DR-36: **Error con tres partes obligatorio.** Todo error visible —mensaje de error de operación,
`toast-error` y detalle del error en Diagnóstico— presenta **qué falló, por qué y qué hacer ahora**,
sin códigos internos como texto principal. *(Consistente con AR-4.)*

UX-DR-37: **Confirmación de operación privilegiada.** Las operaciones privilegiadas del manifiesto
piden confirmación explícita antes de ejecutarse, en un `dialog-surface` de un solo nivel. Al
**reanudar** una operación tras reinicio, la confirmación **vuelve a pedirse**: la confirmación no
se hereda de una sesión anterior.

**Accesibilidad, escritorio y flujos (EXPERIENCE.md).**

UX-DR-38: **WCAG 2.2 AA en todas las superficies.** Contraste AA en cliente y servidor; teclado
completo, con el orden de tabulación siguiendo el orden de lectura de cada superficie; foco
visible siempre con `{colors.accent-action}`, sin depender del cambio de tono de la superficie.

UX-DR-39: **Anuncios de lector de pantalla.** Al cambiar de superficie se anuncia «Catálogo, catálogo
de videojuegos, 24 juegos». El progreso se anuncia por cambio de porcentaje significativo (10 %,
25 %, 50 %, 75 %, 100 %), no en cada *tick*. Los errores se anuncian como texto, no solo
visualmente.

UX-DR-40: **Objetivo de tamaño y escalado del sistema.** Objetivos interactivos con mínimo 24×24
px; botones primarios y campos de 36 px de alto. El escalado de texto y de ventana lo gobierna el
sistema operativo: la interfaz no reimplanta el escalado con factores propios. La rejilla del
catálogo recalcula columnas al redimensionar sin truncar contenido.

UX-DR-41: **Modo claro acotado.** Solo el visor de diagnóstico y la ficha de juego disponen de
variante clara con los tokens `-light`; el modo oscuro es el predeterminado y el resto de la
aplicación no cambia.

UX-DR-42: **Comportamiento de escritorio.** Ventana redimensionada: la columna de navegación
conserva su ancho y la rejilla recalcula columnas (mínimo 3 a 1280 px, hasta 6; por debajo de 3 → 2;
por debajo de 2 → lista de una columna), con la última fila alineada a la izquierda y las tarjetas
sin estirar. Ventana estrecha: el panel derecho se reduce a la línea de presencia y la operación en
curso, sin desaparecer. Sin Internet: ninguna superficie muestra un error de red global; un icono
de red ausente no se confunde con «sin servidor».

UX-DR-43: **Ciclo de vida del juego y del cliente.** El juego se lanza fuera de la ventana de LAN
Party Hub; el cliente queda en segundo plano y el panel de estado sigue mostrando «En ejecución».
LAN Party Hub **no captura teclas** mientras el juego está en ejecución. Cliente y servidor pueden
ejecutarse a la vez en el mismo equipo, y la instancia del servidor no ocupa el panel de estado de
un cliente.

UX-DR-44: **Flujos clave de extremo a extremo.** Flujo 1 (Ana, UJ-1): conexión → catálogo → ficha →
descarga con pausa y reanudación y recuperación tras reinicio → validación → instalación portable →
validación de instalación → «Jugar» → estado en el servidor, sin que el jugador pregunte en ningún
momento en qué estado está nada. Flujo 2 (Luis, UJ-2): tabla de clientes → fila desconectada →
detalle con el error y su causa → decisión sin preguntar a nadie. Flujo 3 (Marta, UJ-3): alta de
videojuego → tamaño en bytes exactos presentado como derivado → adjuntar paquete → la publicación se
bloquea si falta licencia o restricciones, indicando cuál falta → publicar → el registro aparece
en el catálogo de los clientes tras la sincronización.

UX-DR-45: **Recuperación de una operación interrumpida (Flujo 4).** Al reiniciar el equipo a mitad de
una descarga, la app se abre con la operación recuperada: «Reanudando desde 42 % · 3,2 GB de 7,6
GB». Una instalación incompleta se detecta y se marca como reparable en lugar de repetir el
paquete entero; «Jugar» permanece bloqueada con el motivo visible y «Reparar» es la primaria.

### FR Coverage Map

FR-1: Epic 2 — Administración del catálogo y publicación de paquetes
FR-2: Epic 3 — Catálogo sincronizado, búsqueda y filtrado
FR-3: Epic 4 — Descubrimiento, dirección manual y reconexión
FR-4: Epic 4 — Compatibilidad de versión de clientes sin autenticación
FR-5: Epic 5 — Descarga desde el servidor con pausa, reanudación y persistencia
FR-6: Epic 6 — Confianza de paquetes: ancla out-of-band, firma y verificación de integridad
FR-7: Epic 5 — Abstracción de transferencia con un único camino HTTP en el MVP
FR-8: Epic 7 — Instalación portable con manifiesto tipado y confirmación de operaciones privilegiadas
FR-9: Epic 8 — Lanzamiento de videojuegos y detección del proceso
FR-10: Epic 9 — Panel de estado de clientes y diagnóstico para el organizador
FR-11: Epic 10 — Actualización a versión completa, reparación y biblioteca

NFR-1: Epic 4, 5, 7, 9 — Autonomía de red en conexión, descarga, instalación y panel de estado
NFR-2: Epic 11, S-4 — Objetivo de escala como hipótesis, validado por spike, no como promesa
NFR-3: Epic 5, 7 — Paquetes de varios GB con progreso persistido y operación recuperable
NFR-4: Epic 1, 11 — Plataforma Windows 10/11 x64 como alcance de despliegue del MVP
NFR-5: Epic 6, 7 — Validación obligatoria antes de instalar y confinement de la extracción
NFR-6: Epic 11, S-4 — Consumo moderado de recursos, con umbrales fijados por medición
NFR-7: Epic 9 — Logs JSON correlacionados y errores asociados a cliente u operación
NFR-8: Epic 2 — Licencia y restricciones obligatorias antes de publicar
NFR-9: Epic 5, 7, 9 — Recuperación ante desconexión, instalación incompleta y reinicio
NFR-10: Epic 1, 11 — Servidor sin cloud y sin dependencias de trackers o DHT

UX-DR-1: Epic 11 — Sistema de tokens y tema
UX-DR-2: Epic 11 — Tipografía y monoespaciada para literales
UX-DR-3: Epic 11 — Elevación tonal sin sombras
UX-DR-4: Epic 11 — Geometría y espaciado
UX-DR-5: Epic 11 — Regla única de tamaños y progreso
UX-DR-6: Epic 11 — Microcopia por estado crítico
UX-DR-7: Epic 11 — Componentes base: botones, campo de entrada
UX-DR-8: Epic 11 — Jerarquía de botones
UX-DR-9: Epic 11 — Búsqueda al escribir con retardo
UX-DR-10: Epic 11 — Tarjeta de juego
UX-DR-11: Epic 4 — Panel de conexión persistente
UX-DR-12: Epic 11 — Insignia de estado
UX-DR-13: Epic 5 — Barra de progreso honesta
UX-DR-14: Epic 9 — Tabla densa de clientes
UX-DR-15: Epic 9 — Aviso de error textual
UX-DR-16: Epic 7 — Diálogo de un solo nivel
UX-DR-17: Epic 8 — Tabla de estados de instalación y acción primaria
UX-DR-18: Epic 8 — Motivo del bloqueo visible sin interactuar
UX-DR-19: Epic 8 — No hay segunda vía de lanzamiento
UX-DR-20: Epic 8 — Reparar reutiliza el paquete validado
UX-DR-21: Epic 3 — Estados de vista derivados
UX-DR-22: Epic 5 — Cancelar nombra lo que se pierde
UX-DR-23: Epic 11 — Arquitectura de información del cliente
UX-DR-24: Epic 11 — Arquitectura de información del servidor
UX-DR-25: Epic 11 — Primitivas de teclado
UX-DR-26: Epic 8 — Cobertura de teclado de las acciones de instalación
UX-DR-27: Epic 3 — Reglas de puntero y paginación
UX-DR-28: Epic 3 — Estados vacíos con salida
UX-DR-29: Epic 4 — Servidor no disponible y reconexión
UX-DR-30: Epic 4 — Descubrimiento sin resultado
UX-DR-31: Epic 4 — Cliente incompatible
UX-DR-32: Epic 3 — Catálogo desactualizado
UX-DR-33: Epic 6 — Validación fallida con causa diferenciada
UX-DR-34: Epic 7 — Instalación en curso y fallida
UX-DR-35: Epic 8 — Error de lanzamiento accionable
UX-DR-36: Epic 9 — Error con tres partes obligatorio
UX-DR-37: Epic 7 — Confirmación de operación privilegiada
UX-DR-38: Epic 11 — WCAG 2.2 AA
UX-DR-39: Epic 11 — Anuncios de lector de pantalla
UX-DR-40: Epic 11 — Objetivo de tamaño y escalado del sistema
UX-DR-41: Epic 11 — Modo claro acotado
UX-DR-42: Epic 11 — Comportamiento de escritorio
UX-DR-43: Epic 8 — Ciclo de vida del juego y del cliente
UX-DR-44: Epic 11 — Flujos clave de extremo a extremo
UX-DR-45: Epic 10 — Recuperación de una operación interrumpida

### Trazabilidad de las Additional Requirements (AR)

Las AR **no** se asignan a una épica por mapa: se trazan **por cita dentro del propio AC** que las
hace exigibles, con la forma `*(AR-n, …)*` al final del criterio. La columna «AC que la cita» de
esta tabla es la forma de auditarlas sin re-leer el documento entero.

| AR | AC que la cita | Verificabilidad |
| --- | --- | --- |
| AR-1 | 1.1 AC1, AC2, 2.1 AC5, 4.1 AC1, 5.1 AC1 | Estructural (grafo de dependencias) |
| AR-2 | 4.5 AC6 | Estructural (contrato de API) |
| AR-3 | 1.1 AC4, AC5, 2.2 AC4, 5.3 AC6, 9.1 AC4, 9.2 AC1, AC2 | Estructural (contenido de `Shared`) |
| AR-4 | 1.4 AC3, 4.2 AC3, 6.2 AC3, 9.1 AC7, 9.3 AC1 | Estructural (forma de `ApiError`) |
| AR-5 | 3.3 AC3, AC4, 7.1 AC7, 9.2 AC3, 10.1 AC1, AC2 | Estructural (`StateDisplayMap`) |
| AR-6 | 2.2 AC2, AC3 | Estructural (`PackageDescriptor`) |
| AR-7 | 2.2 AC2, AC4, AC5, AC6, 6.1 AC1, 7.1 AC2 | Estructural (manifiesto canónico) |
| AR-8 | 3.1 AC7, 3.3 AC4, 6.1 AC1, AC2, AC4, AC5, 7.1 AC1, AC3, AC5, AC6, 9.2 AC6, 10.2 AC2, 11.4 AC1 | Estructural (orden normativo) |
| AR-9 | 1.3 AC4, 7.1 AC5, AC6, 7.2 AC1, AC2, AC3 | Estructural (confines de ruta) |
| AR-10 | 1.3 AC1, 5.3 AC4, 7.1 AC4, 7.2 AC4 | Estructural (layout en disco) |
| AR-11 | 1.3 AC3, 5.1 AC5, 5.2 AC5, AC6, 5.3 AC1, AC2 | Estructural (progreso persistido) |
| AR-12 | 1.2 AC3, AC4, 1.3 AC3 | Estructural (persistencia por rol) |
| AR-13 | 2.1 AC5, 2.2 AC6, 5.2 AC1, 7.1 AC7, 9.1 AC5, 9.2 AC3, AC4 | Estructural (propietario de entidad) |
| AR-14 | 1.3 AC2, 1.4 AC1, 5.2 AC8, 9.1 AC5..AC14 | Estructural (identidad del cliente) |
| AR-15 | 1.1 AC6, 5.2 AC5, AC6, 5.3 AC6, 7.1 AC4, AC5, AC6, AC7 | Estructural (máquinas de estado) |
| AR-16 | 2.3 AC2, AC3, 3.1 AC1, AC4, AC5, AC6, 4.5 AC6, 9.2 AC5, 10.2 AC4 | Estructural (consistencia del catálogo) |
| AR-17 | 1.2 AC1 | Estructural (superficie de red) |
| AR-18 | 4.4 AC1, AC2, AC3, AC5, AC6, AC7 | Estructural (fijación del ancla) |
| AR-19 | 1.2 AC3 | Estructural (despliegue) |
| AR-20 | 1.4 AC1, AC2, AC3, 6.2 AC3, AC4, 9.1 AC8, 9.4 AC3 | Estructural (observabilidad) |
| AR-21 | 1.1 AC7, 13.1 AC1 | **Verificable** (clasificación de niveles de prueba) |
| AR-22 | 8.1 AC1, AC2, AC5, 9.2 AC2 | Estructural (lanzador aislado) |
| AR-23 | 1.2 AC2, 1.3 AC4 | Estructural (configuración y precedencia) |
| AR-24 | 1.3 AC3, 5.1 AC5, 5.2 AC8 | Estructural (concurrencia) |
| AR-25 | 12.1 AC8, 12.2 AC5 | **Verificable** (carácter normativo del spike) |

> **Nota de alcance (E-3, resuelta).** AR-21 y AR-25 no admiten una comprobación puramente
> estructural: la primera se verifica en la **existencia y clasificación** de los niveles de prueba
> (Story 1.1 AC7 y Story 13.1 AC1) y la segunda en el **carácter declarado y exigido** de cada spike
> (Story 12.1 AC8 y Story 12.2 AC5, los dos spikes cuyo carácter es normativo y queda citado dentro
> de su propio AC). Ninguna de las dos puede cerrarse por inspección del código: se cierran por
> evidencia.

> **Resolución R-MVP-8.** La microcopia del resultado `Paquete retirado del catálogo` queda
> asignada a la tabla única que le corresponde en `Shared` y se declara en Story 3.1 AC7 y Story 9.2
> AC6 que no es un fallo ni se persiste como `ClientError`. La resolución elimina la divergencia D-2
> sin ampliar el alcance del MVP.

## Epic List

### Epic 1: Fundamentos del sistema y arranque del evento

El organizador puede ejecutar el servidor del evento en su equipo y el jugador puede arrancar el
cliente en el suyo, sobre una LAN real y sin Internet, con una estructura de código y contratos
compartidos que ya no cambia durante el resto del MVP.

**FRs cubiertos:** ninguno directamente (épica fundacional); prepara FR-2..FR-11.
**NFRs cubiertos:** NFR-1, NFR-4, NFR-10.
**UX-DRs cubiertos:** ninguno directamente (define la base de las superficies de UX).

### Epic 2: El organizador publica el catálogo y sus paquetes

Marta puede registrar un videojuego con todos sus metadatos, adjuntar su paquete y publicarlo con
sus metadatos, su manifiesto firmado y sus restricciones de licencia; el catálogo publicado queda
disponible para que todos los clientes lo sincronicen.

**FRs cubiertos:** FR-1.
**NFRs cubiertos:** NFR-8, NFR-10.
**UX-DRs cubiertos:** UX-DR-24 (parcial), UX-DR-44 (Flujo 3).

### Epic 3: El jugador explora el catálogo sincronizado

Ana ve el catálogo del evento ya sincronizado en su cliente, lo busca, lo filtra y abre la ficha
completa de un videojuego, siempre sabiendo si el catálogo está al día.

**FRs cubiertos:** FR-2.
**NFRs cubiertos:** NFR-1.
**UX-DRs cubiertos:** UX-DR-21, UX-DR-27, UX-DR-28, UX-DR-32.

### Epic 4: El jugador se conecta al servidor de la LAN

Ana localiza el servidor de la LAN —por descubrimiento automático o por dirección manual— comprueba
que su cliente es compatible con el servidor, fija el ancla de confianza out-of-band y, ante una
interrupción, recupera la conexión sin perder el control.

**FRs cubiertos:** FR-3, FR-4.
**NFRs cubiertos:** NFR-1, NFR-9.
**UX-DRs cubiertos:** UX-DR-11, UX-DR-29, UX-DR-30, UX-DR-31.

### Epic 5: El jugador descarga un paquete íntegro del servidor

Ana descarga un paquete del servidor con progreso visible, puede pausar y reanudar, y si el equipo
se reinicia o el servidor cae, la descarga se recupera desde el progreso persistido sin empezar de
cero.

**FRs cubiertos:** FR-5, FR-7.
**NFRs cubiertos:** NFR-1, NFR-3, NFR-9, NFR-10.
**UX-DRs cubiertos:** UX-DR-13, UX-DR-22.

### Epic 6: El jugador verifica que el paquete es auténtico

Ana valida el paquete descargado contra el ancla de confianza fijada out-of-band, distingue entre
paquete dañado y paquete no confiable y ninguno de los dos llega a la fase de instalación.

**FRs cubiertos:** FR-6.
**NFRs cubiertos:** NFR-5, NFR-8.
**UX-DRs cubiertos:** UX-DR-33.

### Epic 7: El jugador instala el juego en modo portable

Ana instala el paquete validado en el directorio configurado mediante un manifiesto de operaciones
tipadas, con confirmación explícita de las operaciones privilegiadas, y puede reanudar una
instalación interrumpida sin repetir la descarga.

**FRs cubiertos:** FR-8.
**NFRs cubiertos:** NFR-3, NFR-5, NFR-9.
**UX-DRs cubiertos:** UX-DR-16, UX-DR-34, UX-DR-37.

### Epic 8: El jugador lanza el juego y sabe por qué no puede

Ana lanza el juego instalado desde la Biblioteca o la ficha, ve el proceso en ejecución y, si no
puede jugar, tiene el motivo visible y la acción correcta a mano.

**FRs cubiertos:** FR-9.
**NFRs cubiertos:** NFR-9.
**UX-DRs cubiertos:** UX-DR-17, UX-DR-18, UX-DR-19, UX-DR-20, UX-DR-26, UX-DR-35, UX-DR-43.

### Epic 9: El organizador ve el estado de cada equipo sin preguntar

Luis ve en el servidor la tabla de equipos conectados, sus instalaciones, sus descargas en curso y
sus errores con causa y siguiente paso, y puede diagnosticar una incidencia sin|roundas de
preguntas por el pasillo.

**FRs cubiertos:** FR-10.
**NFRs cubiertos:** NFR-6, NFR-7, NFR-9.
**UX-DRs cubiertos:** UX-DR-14, UX-DR-15, UX-DR-36, UX-DR-44 (Flujo 2).

### Epic 10: El jugador actualiza su biblioteca a la última versión

Ana detecta que su instalación está desactualizada, obtiene la versión completa del catálogo,
sustituye la anterior y repara una instalación inválida sin volver a descargar el paquete.

**FRs cubiertos:** FR-11.
**NFRs cubiertos:** NFR-3, NFR-5, NFR-9.
**UX-DRs cubiertos:** UX-DR-45.

### Epic 11: La interfaz cumple su contrato visual, de accesibilidad y de escritorio

La interfaz del cliente y del servidor es coherente con el contrato visual aprobado, es operable
solo con teclado, cumple WCAG 2.2 AA, aplica la regla única de tamaños y progreso y se comporta
bien en una sala a oscuras con las manos ocupadas.

**FRs cubiertos:** ninguno directamente (épica transversal de calidad); verifica FR-2..FR-11.
**NFRs cubiertos:** NFR-1, NFR-4, NFR-6.
**UX-DRs cubiertos:** UX-DR-1, UX-DR-2, UX-DR-3, UX-DR-4, UX-DR-5, UX-DR-6, UX-DR-7, UX-DR-8,
UX-DR-9, UX-DR-10, UX-DR-12, UX-DR-23, UX-DR-24, UX-DR-25, UX-DR-38, UX-DR-39, UX-DR-40,
UX-DR-41, UX-DR-42, UX-DR-44.

### Epic 12: Spikes de decisión técnica (experimentos, no producto)

Las decisiones que dependen de evidencia técnica se cierran con experimentos acotados y
publicables. Cada spike tiene criterios de éxito observables y carácter declarado. **S-2 es
bloqueante** para el cierre del MVP; S-1, S-3, S-4 y S-5 son no bloqueantes y su resultado es
una decisión de fase posterior. Ningún spike introduce capacidad de producto en el MVP.

**FRs cubiertos:** ninguno (S-2 valida el mecanismo de FR-6; S-5 valida el de FR-5).
**NFRs cubiertos:** NFR-2, NFR-6 (S-4).
**UX-DRs cubiertos:** ninguno.

### Epic 13: El guion de aceptación del MVP en LAN real

El MVP se cierra con un guion reproducible que encadena el flujo completo del PRD §9 en una LAN
real, con dos o más clientes, sin Internet y con el mismo paquete íntegro para todos. Es la
historia que hace verificables SM-1 y SM-5: sin ella, el backlog cerraría con todas las historias
«verdes» sin que nadie haya corrido nunca el flujo completo.

**FRs cubiertos:** FR-2..FR-11 (verificación conjunta).
**NFRs cubiertos:** NFR-1, NFR-9.
**UX-DRs cubiertos:** UX-DR-44 (los cuatro flujos, ejecutados de extremo a extremo).

<!-- Repeat for each epic in epics_list (N = 1, 2, 3...) -->

## Epic 1: Fundamentos del sistema y arranque del evento

El organizador puede ejecutar el servidor del evento en su equipo y el jugador puede arrancar el
cliente en el suyo, sobre una LAN real y sin Internet, con una estructura de código y contratos
compartidos que ya no cambia durante el resto del MVP.

Esta épica es **fundacional**: no entrega valor de usuario por sí misma, pero fija la forma del
sistema (Clean Architecture, monolito modular, contratos en `Shared`) y el hecho de que **el
proyecto del MVP no referencia MonoTorrent ni ninguna biblioteca P2P**.

### Story 1.1: Estructura de solución y contratos compartidos

Como arquitecto del producto,
quiero la solución estructurada en servidor, cliente y contratos compartidos,
so que ninguna regla de negocio se duplique y el dominio no dependa de la infraestructura.

**Acceptance Criteria:**

**Given** un repositorio vacío en la base `bde708d`,
**When** se crea la solución con `src/Server`, `src/Client`, `src/Shared` y `tests/UnitTests`,
`tests/IntegrationTests`, `tests/EndToEndTests`,
**Then** `Shared` no referencia ningún otro proyecto del repositorio y `Server` y `Client`
referencian `Shared`,
**And** ningún tipo de `Domain` importa `Infrastructure`, EF Core, Avalonia ni HTTP
*(AR-1, AD-1)*.

**Given** la solución creada,
**When** se inspecciona el grafo de dependencias,
**Then** las dependencias apuntan hacia adentro (`Infrastructure → Application → Domain`),
**And** ningún módulo de negocio importa la implementación interna de otro módulo de negocio
*(AR-1)*.

**Given** el proyecto del MVP,
**When** se inspecciona el fichero de dependencias del proyecto,
**Then** **no** referencia `MonoTorrent` ni ninguna biblioteca de transferencia P2P
*(R-MVP-1, AD-28)*.

**Given** el módulo `Shared`,
**When** se declaran sus contratos,
**Then** contiene **solo** contratos: DTOs de API, identificadores, enumeraciones, eventos de
SignalR y la versión de protocolo,
**And** ninguna lógica de negocio, ninguna dependencia de EF Core, Avalonia ni de la red
*(AR-3, AD-18)*.

**Given** los contratos de `Shared`,
**When** se implementan,
**Then** existen exactamente estos tipos, sin duplicación en `Server` o `Client`: `ApiError`,
`ErrorCatalog`, `DownloadState`, `InstallationState`, `StateDisplayMap`, `CatalogViewState`,
`ClientStateChanged`, `DownloadProgressUpdated`, `InstallationProgressUpdated`,
`ClientErrorReported`, `CatalogChanged`, `ClientRegistrationView`, `TrustAnchor`,
`PackageDescriptor`, `InstallationManifest` + sobre firmado y `VersionInfo`
*(AR-3, AD-18)*.

**Given** `DownloadState` e `InstallationState`,
**When** se declaran,
**Then** son enumeraciones **cerradas** con transiciones explícitas y estado terminal por operación,
**And** las mismas en cliente y servidor
*(AR-15, AD-10)*.

**Given** la solución del MVP con sus tres proyectos de prueba,
**When** se clasifica la estrategia de pruebas,
**Then** existen **tres niveles** de prueba con contenido declarado: **unitarias** (dominio, máquinas
de estado, validación de manifiesto, verificación del ancla de confianza y confines de ruta),
**integración** (SQLite real en archivo, endpoints con `WebApplicationFactory`, motor de transferencia
HTTP contra servidor de prueba) y **E2E en LAN real** con dos o más clientes, que es el escenario de
aceptación del MVP,
**And** la checklist de `docs/plan.md` §10 se clasifica por **nivel mínimo de detección** de cada
caso,
**And** el caso «cliente compartiendo fragmentos con otros» **pertenece a la PoC P2P** y **sale del
conjunto obligatorio del MVP**: no es un caso de prueba del MVP ni ninguna historia lo exige como
parte del conjunto obligatorio
*(AR-21, R-MVP-1, AD-16, AD-28)*.

### Story 1.2: Arranque del servidor con superficie de red mínima y datos configurables

Como organizador del evento,
quiero arrancar el servidor en mi equipo sin instalar nada y sin que abra puertos innecesarios,
so que el evento arranque en minutos y la superficie expuesta sea la mínima.

**Acceptance Criteria:**

**Given** el binario del servidor recién publicado,
**When** arranca con `--data-dir` y `--http-port`,
**Then** escucha **solo** en interfaces privadas y abre **solo** el puerto HTTP (API + SignalR) y el
UDP 5353 de mDNS,
**And** no abre ningún puerto de tracker ni de P2P
*(AR-17, AD-12, AD-28)*.

**Given** el servidor arrancado,
**When** se inspecciona la configuración efectiva,
**Then** la precedencia es `flags > env > archivo`,
**And** el servidor **no expone** ningún flag, selector ni puerto de motor de transferencia P2P
*(AR-23, AD-12)*.

**Given** el servidor arrancado con un `--data-dir` nuevo,
**When** se consulta su estado,
**Then** crea en esa carpeta su archivo de datos SQLite (`catalog.sqlite`) y su repositorio de
paquetes,
**And** la carpeta de datos sobrevive al reemplazo del binario
*(AR-12, AR-19, AD-17)*.

**Given** el servidor en ejecución,
**When** se aplica las migraciones de EF Core,
**Then** se aplican automáticamente al inicio y son versionadas
*(AR-12, AD-11)*.

**Given** una red local sin Internet,
**When** el servidor está en marcha,
**Then** **no** intenta contactar ningún servicio en Internet,
**And** no depende de trackers, DHT ni servicios externos públicos
*(NFR-10, AD-13)*.

### Story 1.3: Arranque del cliente con layout en disco y persistencia propia

Como jugador de LAN,
quiero que el cliente arranque en mi equipo conservando mi configuración y el estado de mis
operaciones,
so que no pierda nada al reiniciar la aplicación.

**Acceptance Criteria:**

**Given** un cliente nuevo,
**When** arranca por primera vez,
**Then** crea `%LOCALAPPDATA%\LanPartyHub` con `install/` (raíz de instalación configurable),
`downloads/` (descargas parciales), `manifests/` (caché de manifiestos) y `state.sqlite`
*(AR-10, AD-9, AD-17)*.

**Given** el cliente ya arrancado antes,
**When** arranca de nuevo,
**Then** **no** regenera su `clientId`: el UUID v7 emitido la primera vez se persiste en
`ClientIdentity` y sobrevive a reinicios y a reinstalaciones del binario mientras el archivo de
datos sobreviva,
**And** el `clientId` es la **clave técnica** que deduplica la fila del servidor y correlaciona los
logs, y **no cambia** ante un rechazo por nombre, ante una colisión resuelta ni ante un cambio de
nombre del equipo,
**And** el servidor **no deduplica ni agrupa** clientes por `machineName`: el `machineName` es la
**etiqueta humana** del equipo, la valida y la hace única el servidor, y su unicidad es una
restricción de nombre, **no** un control de acceso
*(AR-14, AD-23, AD-30)*.

**Given** el cliente con operaciones persistidas,
**When** arranca,
**Then** aplica las migraciones de EF Core al inicio,
**And** el esquema de `state.sqlite` es propiedad del cliente y **no** replica ninguna tabla del
servidor,
**And** un registro persistido cuyo `transferEngine` no exista en la versión activa se rechaza en
el arranque, en lugar de reinterpretarse
*(AR-11, AR-12, AR-24)*.

**Given** el cliente con un directorio de instalación configurado,
**When** se cambia el directorio en Configuración,
**Then** el cambio se persiste con precedencia `flags > env > archivo`,
**And** el directorio nuevo es el único confinado por la regla de confines de extracción
*(AR-23, AR-9)*.

### Story 1.4: Observabilidad correlacionada desde el primer arranque

Como ingeniero del producto,
quiero logs estructurados y correlacionados por operación y por cliente desde el primer arranque,
so que un fallo posterior sea diagnosticable sin adivinar.

**Acceptance Criteria:**

**Given** cualquier operación de larga duración,
**When** se ejecuta,
**Then** se emite logging estructurado **JSON** con `operationId` y `clientId` propagados a través
de la frontera servidor-cliente,
**And** el `clientId` es obligatorio en todas las peticiones y eventos
*(AR-20, AR-14, AD-15, AD-23)*.

**Given** el servidor en ejecución,
**When** se consulta el endpoint de métricas del host,
**Then** expone localmente tasa de transferencia, operaciones activas y errores por código,
**And** no expone telemetría a ningún servicio externo
*(AR-20, NFR-10)*.

**Given** un log emitido,
**When** se inspecciona su contenido,
**Then** **no** contiene credenciales ni datos de autenticación,
**And** los identificadores de código de error aparecen como `code` del catálogo, nunca como texto
principal de cara al usuario
*(AR-20, AR-4)*.

## Epic 2: El organizador publica el catálogo y sus paquetes

Marta puede registrar un videojuego con todos sus metadatos, adjuntar su paquete y publicarlo con
su manifiesto firmado y sus restricciones de licencia; el catálogo publicado queda disponible para
que todos los clientes lo sincronicen.

### Story 2.1: Alta y modificación de videojuegos con metadatos completos

Como administradora del servidor,
quiero registrar un videojuego con todos sus metadatos y modificarlo después,
so que el catálogo refleje la realidad de lo que se puede jugar en el evento.

**Acceptance Criteria:**

**Given** la sección Catálogo del servidor,
**When** Marta da de alta un videojuego,
**Then** se registran identificador único, nombre, descripción, categorías, etiquetas, carátula,
imágenes, versión, tamaño, jugadores mínimo y máximo, sistema operativo, tipo de distribución, tipo
de instalación, configuración de lanzamiento, dependencias, licencia y restricciones de
distribución
*(FR-1)*.

**Given** un videojuego persistido,
**When** se consulta,
**Then** conserva un identificador único y todos los metadatos obligatorios,
**And** dos registros con el mismo nombre se distinguen por identificador y versión
*(FR-1)*.

**Given** un videojuego guardado,
**When** se modifica su versión, tamaño o configuración de lanzamiento,
**Then** el registro conserva su identificador y refleja el cambio
*(FR-1)*.

**Given** la sección Catálogo del servidor,
**When** se abre el formulario de alta o edición,
**Then** el formulario marca como obligatorio lo que FR-1 exige y la ruta de lanzamiento y la
versión se muestran en monoespaciada,
**And** el tamaño se introduce en bytes exactos y se presenta como valor decimal derivado («7,6» en
`{typography.size}` y «GB» en `{typography.meta}`)
*(UX-DR-2, UX-DR-5, UX-DR-44)*.

**Given** el servidor,
**When** se inspecciona la persistencia,
**Then** `Game` es propiedad del servidor y vive en `catalog.sqlite`,
**And** ningún módulo del servidor accede a la tabla de otro módulo
*(AR-13, AR-1)*.

### Story 2.2: Publicación de un paquete con licencia y manifiesto firmado

Como administradora del servidor,
quiero adjuntar el paquete de un videojuego y publicarlo con su manifiesto firmado,
so que solo se distribuya contenido permitido y verificable.

**Acceptance Criteria:**

**Given** un videojuego sin información de licencia o sin restricciones de distribución,
**When** Marta intenta publicar su paquete,
**Then** la publicación se **bloquea** y el formulario explica **cuál** de los dos campos falta
*(FR-1, NFR-8, UX-DR-44)*.

**Given** un videojuego con licencia y restricciones registradas,
**When** Marta publica su paquete,
**Then** el servidor persiste un `PackageDescriptor` con `packageId`, `gameId`, `version`,
`sizeBytes` exacto, `chunkSizeBytes` (informativo y opcional en el camino HTTP), `sha256` y
`manifestRef`,
**And** el descriptor **no** transporta firma: la firma vive **solo** en el sobre firmado del
manifiesto
*(FR-1, AR-6, AR-7, AD-2, AD-7)*.

**Given** un paquete publicado,
**When** se intenta cambiar su contenido,
**Then** el sistema exige **publicar una versión nueva**: un paquete publicado es inmutable
*(AR-6, AD-7)*.

**Given** el manifiesto de instalación de un paquete,
**When** se firma,
**Then** la firma es **Ed25519** sobre el **manifiesto canónico** (JSON canónico, sin espacios
superfluos, claves ordenadas) y el sobre contiene `{ manifest, signature, keyId }`,
**And** el manifiesto firmado incluye `packageId`, `packageVersion`, `sizeBytes` y `sha256` del
paquete, de modo que la correspondencia entre manifiesto y paquete queda **firmada**, no supuesta
*(AR-7, AR-3, AD-2)*.

**Given** el manifiesto de instalación,
**When** se declaran sus operaciones,
**Then** son **tipadas y cerradas**: `extract`, `createDirectory`, `copyFile`, `writeConfig`,
`setLaunchConfig`, `verify`,
**And** **no existe ninguna operación de ejecución de comandos arbitrarios**
*(FR-8, AR-7, AD-7)*.

**Given** el servidor,
**When** se inspecciona la persistencia,
**Then** `PackageDescriptor` e `InstallationManifest` (con su sobre firmado) son propiedad del
servidor y se sirven como artefactos **aparte**, no incrustados en el binario del paquete
*(AR-13, AR-7, AD-20)*.

### Story 2.3: Control de la publicación por parte del organizador

Como organizador del evento,
quiero ver y ajustar qué juegos forman parte del catálogo,
so que la oferta del evento sea la que yo decido.

**Acceptance Criteria:**

**Given** la sección Paquetes del servidor,
**When** el organizador lista los paquetes publicados,
**Then** ve, por cada uno, el juego asociado, la versión, el tamaño exacto en bytes y la
información de licencia y restricciones
*(FR-1, UX-DR-24)*.

**Given** un paquete publicado,
**When** el organizador retira un juego del catálogo,
**Then** la retirada no borra la instalación existente de ningún cliente,
**And** el servidor tampoco borra la proyección de instalación de ese cliente
*(AR-16, AD-26)*.

**Given** una publicación,
**When** se completa,
**Then** el `catalogVersion` del servidor **incrementa** de forma monótona creciente
*(AR-16, AD-26)*.

## Epic 3: El jugador explora el catálogo sincronizado

Ana ve el catálogo del evento ya sincronizado en su cliente, lo busca, lo filtra y abre la ficha
completa de un videojuego, siempre sabiendo si el catálogo está al día.

### Story 3.1: Sincronización del catálogo por snapshot completo

Como jugador de LAN,
quiero que el catálogo del evento se sincronice solo en mi cliente,
so que vea lo que hay disponible sin pedirle nada a nadie.

**Acceptance Criteria:**

**Given** un cliente conectado con un catálogo aplicado,
**When** el servidor publica un cambio,
**Then** el cliente recibe `CatalogChanged` con el nuevo `catalogVersion` y aplica un **snapshot
completo** de `GET /api/v1/catalog`,
**And** el cliente **solo** aplica un snapshot con versión **mayor** que la que ya tiene, y
descarta uno igual o anterior
*(FR-2, AR-16, AD-26)*.

**Given** una sincronización correcta,
**When** el cliente termina de aplicarla,
**Then** refleja el catálogo publicado por el servidor
*(FR-2)*.

**Given** el servidor no disponible,
**When** el cliente pierde la conexión,
**Then** **conserva** el último catálogo válido y comunica el estado de sincronización pendiente
con el aviso «Catálogo desactualizado · sincronizado hace 12 min» y la acción «Sincronizar ahora»,
**And** el aviso **no** bloquea la navegación
*(FR-2, NFR-1, UX-DR-32)*.

**Given** un snapshot nuevo que llega mientras hay una descarga en curso,
**When** el cliente lo aplica,
**Then** **no** cancela la operación en curso,
**And** si el paquete o juego en curso fue retirado del catálogo, la operación **termina** con el
resultado explícito `Paquete retirado del catálogo`, que es **distinto del fallo**
*(AR-16, AD-26)*.

**Given** un juego retirado del catálogo que el cliente tiene instalado,
**When** el cliente aplica el snapshot,
**Then** **no borra** la instalación existente: la conserva, la sigue mostrando en su biblioteca y
la marca con el estado de vista derivado correspondiente
*(AR-16, UX-DR-21, AD-26)*.

**Given** el servidor,
**When** se inspecciona la persistencia,
**Then** el `catalogVersion` del servidor es monotónico creciente y la sincronización por delta
**no existe** en el MVP
*(AR-16, AD-26)*.

**Given** un paquete o juego retirado del catálogo mientras una operación está en curso,
**When** la operación termina,
**Then** el resultado `Paquete retirado del catálogo` se presenta desde la tabla única que le
corresponde en `Shared`,
**And** no se presenta como fallo ni se persiste como `ClientError`,
**And** el texto no se define fuera de esa tabla
*(AR-8, R-MVP-8, AD-25, AD-26)*.

### Story 3.2: Búsqueda, filtrado y categorización del catálogo

Como jugador de LAN,
quiero buscar y filtrar los juegos por nombre y categoría mientras escribo,
so que encuentre el que busco sin navegar tarjetas.

**Acceptance Criteria:**

**Given** el catálogo sincronizado,
**When** Ana escribe un nombre en el campo de búsqueda,
**Then** el filtro se aplica **al escribir con 200 ms de retardo**, sin botón «buscar»,
**And** los resultados son consistentes con el catálogo sincronizado
*(FR-2, UX-DR-9)*.

**Given** el catálogo sincronizado,
**When** Ana aplica un filtro por categoría,
**Then** los resultados muestran únicamente los videojuegos de esa categoría
*(FR-2)*.

**Given** una búsqueda sin resultados,
**When** hay filtros activos,
**Then** la superficie muestra «Sin resultados para «<consulta>».» y ofrece la acción «Quitar
filtros»,
**And** la búsqueda es reversible sin dejar la superficie
*(UX-DR-28, UX-DR-27)*.

**Given** un catálogo vacío,
**When** el servidor está conectado,
**Then** la superficie muestra «El catálogo está vacío.» y la acción «Sincronizar ahora»,
**And** si el servidor no está conectado, la acción es «Conectar al servidor»
*(UX-DR-28)*.

**Given** la rejilla del catálogo,
**When** la ventana se redimensiona,
**Then** la rejilla pagina por scrolls de tamaño fijo con paginación explícita, sin desplazamiento
infinito
*(UX-DR-27)*.

### Story 3.3: Ficha del videojuego con su estado de vista

Como jugador de LAN,
quiero abrir la ficha de un juego y ver su versión, tamaño, jugadores, sistema operativo y
requisitos,
so que sepa si me sirve antes de descargar.

**Acceptance Criteria:**

**Given** la rejilla del catálogo,
**When** Ana hace clic en una tarjeta,
**Then** se abre la **ficha** del juego: un clic en la tarjeta abre la ficha y las acciones
secundarias viven en la ficha, de modo que el clic significa siempre lo mismo
*(FR-2, UX-DR-10, UX-DR-27)*.

**Given** la ficha abierta,
**When** se muestra el tamaño del paquete,
**Then** se expresa en GB decimales con **exactamente un decimal** y separador decimal español
(«7,6 GB»), con el valor numérico en `{typography.size}` y la unidad «GB» en `{typography.meta}`,
**And** la versión, el sistema operativo y el número de jugadores se muestran en sus valores
exactos
*(FR-2, UX-DR-5, UX-DR-2)*.

**Given** un juego publicado en el catálogo,
**When** el cliente no tiene una instalación validada de ese juego,
**Then** su insignia de estado de vista es `disponible`,
**And** un juego con instalación validada cuya versión instalada es anterior a la publicada
muestra `instalado con versión desactualizada`,
**And** un cliente con snapshot desactualizado respecto al `catalogVersion` del servidor muestra
`sincronizando`,
**And** la comparación de versiones usa el orden semántico declarado en el catálogo, no la
comparación lexicográfica
*(UX-DR-21, AR-5, AD-10)*.

**Given** las insignias de estado de vista,
**When** se renderizan,
**Then** el texto de cada una sale **exclusivamente** del `StateDisplayMap` de `Shared`,
**And** ningún texto de estado se define en la vista
*(AR-5, AR-8, R-MVP-7)*.

## Epic 4: El jugador se conecta al servidor de la LAN

Ana localiza el servidor de la LAN —por descubrimiento automático o por dirección manual— comprueba
que su cliente es compatible con el servidor, fija el ancla de confianza out-of-band y, ante una
interrupción, recupera la conexión sin perder el control.

### Story 4.1: Descubrimiento automático del servidor por mDNS

Como jugador de LAN,
quiero que el cliente encuentre el servidor solo, sin teclear nada,
so que no dependa de que alguien me pase la dirección.

**Acceptance Criteria:**

**Given** el servidor en marcha en la misma LAN,
**When** el cliente busca servidores,
**Then** descubre el servicio `_lanpartyhub._tcp` (RFC 6762/6763) y lo ofrece como servidor
disponible,
**And** la dirección encontrada se resuelve al mismo contrato `ServerEndpoint` que usa la vía
manual
*(FR-3, AR-1, AD-3)*.

**Given** el cliente,
**When** implementa el descubrimiento,
**Then** **no** hace barrido *unicast* de subred, **no** usa SSDP y **no** consulta DNS-SRV de red
externa
*(FR-3, AD-3)*.

**Given** el cliente en una red donde el descubrimiento no devuelve nada,
**When** termina la búsqueda,
**Then** la superficie de Conexión muestra «No se encontró ningún servidor en esta red.» y la
**acción primaria** es «Introducir dirección manualmente»
*(FR-3, UX-DR-30)*.

**Given** el cliente,
**When** el descubrimiento mDNS está bloqueado por la política de la red,
**Then** el cliente **no** falla de forma fatal y la vía manual permanece operativa
*(FR-3, NFR-9, AD-3)*.

### Story 4.2: Dirección manual como vía permanente y equivalente

Como jugador de LAN,
quiero teclear la dirección del servidor cuando el descubrimiento no me sirve,
so que siempre tenga una salida y no dependa de una sola puerta.

**Acceptance Criteria:**

**Given** la superficie de Conexión,
**When** Ana introduce la dirección del servidor y pulsa `Enter`,
**Then** el cliente se conecta a esa dirección y el estado pasa a conectado,
**And** la dirección manual es una vía **permanente y equivalente** al descubrimiento, no un
*fallback* condicional
*(FR-3, AD-3)*.

**Given** la dirección introducida,
**When** se confirma con `Enter`,
**Then** el campo de entrada tiene altura fija de 36 px y el foco pasa el borde a 2 px
`{colors.accent-action}` **sin** cambiar el alto
*(UX-DR-7, UX-DR-9)*.

**Given** una dirección mal formada,
**When** se confirma,
**Then** el error se presenta con las tres partes obligatorias (qué falló, por qué, qué hacer ahora)
y **no** muestra el código interno como texto principal
*(UX-DR-36, AR-4)*.

### Story 4.3: Compatibilidad de versión de clientes, sin autenticación

Como jugador de LAN,
quiero saber con claridad si mi cliente es compatible con el servidor,
so que entienda por qué no puedo operar y qué tengo que hacer.

**Acceptance Criteria:**

**Given** un cliente compatible,
**When** envía cualquier petición con `X-Client-Version` y `X-Protocol-Version`,
**Then** el servidor la procesa con normalidad,
**And** el cliente puede sincronizar el catálogo, descargar un paquete, instalarlo y reportar su
estado
*(FR-4, AD-6)*.

**Given** un cliente incompatible,
**When** envía cualquier petición,
**Then** el servidor responde **`426 Upgrade Required`** con la versión de servidor requerida y el
rango mínimo soportado,
**And** el cliente **no** completa una sincronización ni inicia una descarga o instalación
*(FR-4, AD-6)*.

**Given** un cliente incompatible,
**When** la UI presenta el diagnóstico,
**Then** muestra la versión del cliente y la del servidor y el siguiente paso, con el texto «Este
cliente (1.2) no es compatible con el servidor (1.4). Actualiza el cliente.»,
**And** **no** hay formulario de credenciales: no las hay
*(FR-4, R-MVP-2, UX-DR-31)*.

**Given** el servidor,
**When** recibe peticiones de clientes incompatibles,
**Then** registra la versión del cliente y la incompatibilidad detectada,
**And** el modelo **no** introduce tokens, sesiones ni roles: el único control de acceso al
protocolo es la compatibilidad de versión
*(FR-4, R-MVP-2, AD-6, AD-12)*.

**Given** el panel de clientes del servidor,
**When** aparece un cliente incompatible,
**Then** se refleja con su versión y su diagnóstico, sin iniciar operaciones de juego
*(FR-4, FR-10, UX-DR-31)*.

### Story 4.4: Fijación del ancla de confianza fuera de banda

Como organizador del evento,
quiero que el fingerprint de la clave del servidor se entregue a los jugadores por un canal que no
pasa por la LAN,
so que un intermediario no pueda sustituir la clave en el primer emparejamiento.

**Acceptance Criteria:**

**Given** el servidor arrancado,
**When** el organizador abre la pantalla de arranque,
**Then** el **fingerprint** de la clave pública del ancla se muestra en pantalla y puede imprimirse o
exportarse como **código QR** que contiene el fingerprint y la dirección del servidor
*(AR-18, AD-27)*.

**Given** el cliente sin ancla fijada,
**When** se confirma la comparación del fingerprint recibido contra el esperado,
**Then** el cliente **calcula** el fingerprint de la clave pública que el servidor presenta y lo
**compara** con el esperado,
**And** si **no coinciden**, **rechaza**: no persiste el ancla, no fija nada, no descarga, no
instala, no escribe nada en la caché `manifests/`, no crea archivo parcial y no registra operación,
**And** el error de diagnóstico usa un `code` del `ErrorCatalog` **distinto** del de «firma
inválida»
*(FR-6, AR-18, R-MVP-3, AD-27)*.

**Given** el cliente sin ancla fijada,
**When** el fingerprint esperado **coincide**,
**Then** el cliente **persiste** el ancla fijada junto con el fingerprint esperado, de modo que las
conexiones posteriores se validan contra el valor ya persistido y no contra uno recién recibido
*(AR-18, AD-27)*.

**Given** el cliente,
**When** se revisan las vías por las que puede obtener el fingerprint esperado,
**Then** son **solo** dos: el código QR del organizador (escaneado o tecleado) y el contraste visual
en la pantalla del organizador,
**And** el fingerprint **no** se propaga por mDNS, ni por el handshake de SignalR, ni por
`GET /api/v1/server/info` como valor a aceptar sin contrastar
*(FR-6, R-MVP-3, AD-27)*.

**Given** `GET /api/v1/server/info`,
**When** el cliente lo consulta,
**Then** el servidor publica su fingerprint **para contrastarlo**, no para aceptarlo,
**And** el cliente solo persiste el ancla cuando se confirma que **coinciden**
*(AR-18, AD-27)*.

**Given** un ancla ya fijada y persistida,
**When** se presenta una clave distinta,
**Then** el cliente **no** la sustituye ni la acepta: la rechaza y exige un emparejamiento
out-of-band explícito nuevo
*(AR-18, AD-27)*.

**Given** el cliente sin ancla fijada,
**When** se consulta su estado de conexión,
**Then** es `AnchorUnverified`, un estado de seguridad **distinto** de `Disconnected`,
**And** en ese estado el cliente no descarga ni instala nada y la UI lo dice de forma explícita
*(AR-18, AD-27)*.

### Story 4.5: Panel de conexión persistente y reconexión

Como jugador de LAN,
quiero ver siempre el estado de mi conexión y que el cliente se recupere solo tras una caída del
servidor,
so que no tenga que preguntar qué está pasando.

**Acceptance Criteria:**

**Given** el cliente conectado,
**When** se observa el panel derecho,
**Then** es **persistente**: no se colapsa, no se esconde en un menú ni en un modal,
**And** muestra punto de presencia, nombre del servidor, dirección en uso y versión del servidor
*(UX-DR-11, UX-DR-23)*.

**Given** el panel de estado,
**When** no hay operación en curso,
**Then** muestra la **última operación terminada** con su resultado, no un espacio vacío
*(UX-DR-11)*.

**Given** el servidor temporalmente inaccesible,
**When** el cliente pierde la conexión,
**Then** el panel derecho pasa a `{colors.state-warning}` con «Servidor no disponible. Se conserva el
último catálogo sincronizado.»,
**And** el punto de presencia pasa a `{colors.state-warning}` con reintento automático y contador
visible «Reintentando · 3 s»,
**And** **no** hay modal que interrumpa
*(FR-3, NFR-9, UX-DR-29)*.

**Given** el servidor inaccesible,
**When** las acciones que requieren servidor se ofrecen,
**Then** están deshabilitadas con su motivo visible
*(UX-DR-29)*.

**Given** una interrupción temporal del servidor,
**When** el servidor vuelve,
**Then** el cliente recupera la conexión **sin reinstalarse** y vuelve a sincronizar el catálogo
*(FR-3, NFR-9)*.

**Given** el servidor,
**When** se detecta una transición de `connectionState` de un `ClientRegistration`,
**Then** es un evento **interno del servidor**, no un evento de hub,
**And** el cliente recibe `CatalogChanged` como **único** evento servidor → cliente del catálogo
*(AR-2, AD-29, AR-16)*.

## Epic 5: El jugador descarga un paquete íntegro del servidor

Ana descarga un paquete del servidor con progreso visible, puede pausar y reanudar, y si el equipo
se reinicia o el servidor cae, la descarga se recupera desde el progreso persistido sin empezar de
cero.

### Story 5.1: Abstracción de transferencia con un único camino HTTP

Como arquitecto del producto,
quiero que la transferencia de bytes tenga un puerto propio y una única implementación en el MVP,
so que el dominio no dependa de ninguna biblioteca de transferencia.

**Acceptance Criteria:**

**Given** el dominio de descarga,
**When** se inspecciona,
**Then** la transferencia de bytes entra **solo** por el puerto `ITransferEngine`,
**And** el dominio (`DownloadOperation`, `PackageIntegrity`) **no** menciona HTTP, BitTorrent,
*trackers* ni *sockets*
*(FR-7, AR-1, AD-4)*.

**Given** el MVP,
**When** se inspecciona la configuración de la aplicación,
**Then** el puerto se entrega con **una sola** implementación, `HttpRangeTransferEngine`,
**And** la configuración **no expone** ningún selector de motor de transferencia: el valor por
defecto es HTTP y no hay valor alternativo
*(FR-7, R-MVP-1, AD-4, AD-28)*.

**Given** el servidor,
**When** se inspecciona el catálogo de endpoints REST,
**Then** **no** expone ningún endpoint de P2P ni de *tracker*,
**And** la topología de despliegue **no** incluye ningún proceso, servicio o puerto de P2P
*(FR-7, R-MVP-1, AD-28)*.

**Given** el modelo de dominio,
**When** se inspecciona su vocabulario,
**Then** **no** menciona *peers*, *swarms* ni fragmentos
*(FR-7, R-MVP-6, AD-28)*.

**Given** la columna `transferEngine` de la operación de descarga persistida,
**When** se fija su valor en el MVP,
**Then** es **exactamente** el identificador `HttpRangeTransferEngine`, el mismo nombre de la única
implementación del puerto,
**And** es un **valor único y cerrado**: el cliente no admite ningún otro valor, no lo deduce de un
parámetro configurable y no reinterpretar un valor desconocido
*(FR-7, AR-11, AR-24, AD-4, AD-24)*.

### Story 5.2: Descarga del paquete con progreso, pausa y reanudación

Como jugador de LAN,
quiero ver el progreso de la descarga y poder pausarla y reanudarla,
so que atienda otra cosa sin perder lo descargado.

**Acceptance Criteria:**

**Given** un paquete publicado,
**When** Ana inicia la descarga,
**Then** el cliente registra la intención con `POST /api/v1/downloads` y obtiene un `operationId`,
**And** el servidor persiste un `DownloadOperation` que es **solo** el reflejo de la intención, **no**
el estado de la operación
*(FR-5, AR-13, AD-8, AD-22)*.

**Given** una descarga en curso,
**When** se observa su progreso,
**Then** el cliente informa el porcentaje entero y el par de tamaños «<n> % · <a> GB de <t> GB»,
**And** la barra refleja **exactamente** la misma fracción que el texto
*(FR-5, UX-DR-5, UX-DR-13)*.

**Given** una descarga en curso,
**When** Ana la pausa,
**Then** el estado pasa a `Paused` y la acción «Reanudar» queda disponible,
**And** al reanudar continúa **desde la misma posición**, sin volver a cero
*(FR-5, UX-DR-6, UX-DR-13)*.

**Given** una descarga pausada,
**When** Ana la cancela,
**Then** la interfaz pide confirmación **nombrando lo que se pierde**: «Se borrarán los 3,2 GB
descargados»,
**And** usa la acción `button-danger`
*(UX-DR-22, UX-DR-8)*.

**Given** una descarga en curso o pausada,
**When** la cancelación se confirma,
**Then** el estado de la operación pasa a `Cancelled`, que es un **estado terminal** de
`DownloadState`,
**And** al cancelar se **borra** el archivo parcial de `downloads/` y **se reinicia** el registro de
progreso persistido: `bytesPersisted` vuelve a cero y la operación **no** queda reintentable por
reanudación, a diferencia de `Failed`,
**And** la microcopia de `Cancelled` sale del `StateDisplayMap` de `Shared` y no de la vista
*(FR-5, AR-11, AR-15, UX-DR-6)*.

**Given** una descarga cancelada,
**When** el cliente vuelve a iniciar la descarga del mismo paquete,
**Then** la descarga **empieza de cero** y no reutiliza ningún offset de la descarga cancelada,
**And** el archivo parcial resultante no se confunde con el anterior ni produce un `sha256`
divergente
*(FR-5, AR-11, AR-15)*.

**Given** el servidor,
**When** sirve el contenido del paquete,
**Then** lo hace por HTTP con soporte de **Range** requests sobre una sola conexión, sin *chunking*
multipart,
**And** devuelve `416` cuando el rango no es satisfacible
*(FR-5, AD-20)*.

**Given** varios clientes,
**When** descargan paquetes simultáneos,
**Then** sus descargas avanzan de forma independiente y no comparten estado de progreso,
**And** el reparto del enlace es equitativo **entre los clientes** que descargan a la vez: la unidad
de reparto es el **cliente**, no la operación ni la conexión, de modo que un cliente que abre varias
descargas simultáneas recibe la parte que le corresponde de cliente, no una parte por operación
*(FR-5, NFR-2, AR-14, AR-24)*.

### Story 5.3: Persistencia del progreso y recuperación tras reinicio

Como jugador de LAN,
quiero que la descarga sobreviva a un reinicio del equipo,
so que un paquete de varios gigabytes no vuelva a empezar de cero.

**Acceptance Criteria:**

**Given** una descarga en curso,
**When** se persiste su progreso,
**Then** el cliente persiste un **offset acumulado** (`bytesPersisted`, bytes válidos contiguos desde
el inicio del parcial) en su tabla propia de progreso,
**And** **no** persiste ningún mapa de fragmentos ni columna `chunkMap`
*(FR-5, NFR-3, AR-11, AD-24, AD-28)*.

**Given** una descarga en curso,
**When** el cliente se reinicia,
**Then** al arrancar reconcilia el progreso persistido **contra el archivo parcial en disco** y el
progreso efectivo es la intersección de lo persistido y lo que el archivo contiene,
**And** la reanudación parte del offset persistido con `Range`, no de cero
*(FR-5, NFR-3, SM-2, AR-11, AD-20)*.

**Given** el cliente reiniciado a mitad de una descarga de 7,6 GB,
**When** se abre la aplicación,
**Then** la operación se presenta como «Reanudando desde 42 % · 3,2 GB de 7,6 GB»
*(FR-5, UX-DR-6, UX-DR-45)*.

**Given** el archivo parcial en curso,
**When** se escribe,
**Then** vive **solo** en `downloads/`, nunca en `%TEMP%` ni en la raíz de instalación,
**And** el progreso persistido sobrevive al reinicio
*(NFR-3, AR-10, AD-9, AD-11)*.

**Given** un cliente incompatible,
**When** intenta iniciar una descarga,
**Then** no la inicia y recibe el diagnóstico de incompatibilidad de versión
*(FR-5, FR-4)*.

**Given** una descarga completada,
**When** el cliente reporta el estado,
**Then** emite `DownloadProgressUpdated` con `clientId`, `operationId`, `bytesTransferred`,
`totalBytes` y `state` (`DownloadState`), con la forma exacta del contrato
*(FR-5, AR-3, AR-15, AD-22)*.

## Epic 6: El jugador verifica que el paquete es auténtico

Ana valida el paquete descargado contra el ancla de confianza fijada out-of-band, distingue entre
paquete dañado y paquete no confiable y ninguno de los dos llega a la fase de instalación.

### Story 6.1: Verificación de integridad y de autenticidad antes de instalar

Como jugador de LAN,
quiero que el cliente verifique el paquete antes de instalarlo y me diga si está dañado o si no es
confiable,
so que nunca ejecute contenido alterado ni procedente de una fuente suplantada.

**Acceptance Criteria:**

**Given** una descarga completada,
**When** el cliente la valida,
**Then** calcula el `sha256` de los bytes descargados y lo compara con el `sha256` del **manifiesto
firmado**,
**And** la firma Ed25519 del manifiesto se valida contra el ancla de confianza **ya fijada**
*(FR-6, AR-7, AR-8, AD-2)*.

**Given** un paquete alterado durante la transferencia,
**When** el `sha256` no coincide,
**Then** el paquete es **rechazado antes de la instalación**,
**And** el archivo parcial se descarta y la operación se marca como fallida y **no reparable por
reanudación**
*(FR-6, NFR-5, AR-8, AD-27)*.

**Given** un paquete que no puede validarse contra la raíz de confianza configurada,
**When** se valida,
**Then** se marca como **no confiable** y **no** se instala
*(FR-6, NFR-5, R-MVP-3)*.

**Given** un manifiesto cuya firma no valida contra el ancla ya fijada,
**When** el cliente lo recibe,
**Then** el manifiesto **no** se persiste en la caché `manifests/`, el archivo parcial **no** se crea
y la operación **no** se registra como iniciada,
**And** el error se persiste para diagnóstico
*(FR-6, NFR-5, AR-8, AD-27)*.

**Given** un manifiesto con `schemaVersion` no soportada por el cliente,
**When** el cliente lo evalúa,
**Then** la operación se rechaza, no se instala y el error se persiste para diagnóstico
*(FR-8, NFR-5, AR-8)*.

**Given** el resultado de una validación,
**When** se consulta,
**Then** está disponible para diagnóstico e incluye el fallo y su causa,
**And** distingue explícitamente **corrupción** de **falta de autenticidad**
*(FR-6, NFR-7, AD-27)*.

### Story 6.2: La UI diferencia «dañado» de «no confiable»

Como jugador de LAN,
quiero ver por qué falló la validación con una causa clara,
so que sepa si el problema es la red o la confianza.

**Acceptance Criteria:**

**Given** una validación fallida por corrupción,
**When** se presenta el estado,
**Then** muestra «Paquete dañado: el hash no coincide · 7,6 GB» en `{colors.state-danger}`,
**And** el alta de instalación queda **bloqueada** con el motivo visible, no oculto en el log
*(FR-6, UX-DR-33, UX-DR-6)*.

**Given** una validación fallida por falta de autenticidad,
**When** se presenta el estado,
**Then** muestra «Paquete no confiable: no se pudo validar contra la raíz de confianza · 7,6 GB»,
**And** la causa es **distinta** de la del paquete dañado
*(FR-6, UX-DR-33, UX-DR-6)*.

**Given** el error de validación,
**When** se muestra en el mensaje de operación, en el `toast-error` o en el detalle de Diagnóstico,
**Then** presenta **qué falló, por qué y qué hacer ahora**,
**And** el `code` del `ErrorCatalog` es identificador interno, nunca el texto principal
*(UX-DR-36, AR-4, AR-20)*.

**Given** un `code` de error desconocido,
**When** llega al cliente,
**Then** se muestra con el texto genérico del catálogo, nunca con el código como texto principal
*(AR-20, AD-15)*.

## Epic 7: El jugador instala el juego en modo portable

Ana instala el paquete validado en el directorio configurado mediante un manifiesto de operaciones
tipadas, con confirmación explícita de las operaciones privilegiadas, y puede reanudar una
instalación interrumpida sin repetir la descarga.

### Story 7.1: Orden normativo de la instalación y rechazo previo a persistir

Como ingeniero de seguridad,
quiero que la verificación del manifiesto ocurra antes de escribir un solo byte en disco,
so que un manifiesto manipulado no pueda ejecutar nada.

**Acceptance Criteria:**

**Given** una instalación solicitada,
**When** el cliente la ejecuta,
**Then** sigue el orden normativo: (1) obtener el manifiesto firmado; (2) verificar firma y
`schemaVersion`; (3) **solo entonces** crear el directorio de instalación y el archivo parcial;
(4) descargar el contenido; (5) calcular el `sha256` y compararlo con el del manifiesto firmado;
(6) si no coincide, descartar el parcial y marcar la operación como fallida
*(FR-8, AR-8, AD-8)*.

**Given** un manifiesto manipulado que contiene una acción no permitida,
**When** el cliente lo evalúa,
**Then** la operación se rechaza y no se instala,
**And** el error queda registrado con su causa
*(FR-8, NFR-5, AR-7)*.

**Given** el paso 2 del orden normativo,
**When** todavía no ha ocurrido,
**Then** **ningún** byte se escribe en disco: ni manifiesto en la caché `manifests/`, ni archivo
parcial, ni registro de operación,
**And** `POST /api/v1/downloads` **no** inicia la escritura en disco
*(FR-8, NFR-5, AR-8, R-MVP-3)*.

**Given** una instalación completada,
**When** el cliente la valida,
**Then** la operación pasa por `ValidatingInstallation` y termina en `Installed` o en `Repairable`,
**And** el cliente es la **única** fuente de verdad del estado local de la operación
*(FR-8, AR-15, AR-10)*.

**Given** una instalación validada,
**When** el cliente certifica el árbol instalado contra el manifiesto firmado,
**Then** el estado es **`Installed`** **solo** si se cumplen **todas** estas condiciones: la
integridad y la autenticidad del paquete ya fueron validadas contra el ancla de confianza
fijada, la operación `verify` del manifiesto pasa sobre el árbol instalado, y cada ruta escrita
resuelve **dentro** de la raíz del directorio de instalación configurado,
**And** ninguna de las tres condiciones es sustituible por otra: la sola presencia del ejecutable,
la sola presencia del directorio o un árbol no vacío **no** bastan para certificar `Installed`,
**And** la certificación es **determinista**: dos clientes que evalúan el mismo árbol de
instalación concludes el mismo estado
*(FR-8, NFR-5, AR-8, AR-9, AR-15, AD-7, AD-9, AD-10)*.

**Given** una instalación que no supera la certificación `Installed`,
**When** el cliente determina su reparabilidad,
**Then** el estado es **`Repairable`** **solo** si falla **exactamente una condición reparable**:
el paquete ya validado sigue disponible y su `sha256` sigue coincidiendo, de modo que la
instalación puede rehacerse desde el paquete ya validado sin volver a descargar,
**And** el motivo de la reparabilidad y la acción de reparación quedan **registrados** y
**visibles**: qué condición falló y qué operación la repara,
**And** si **ninguna** condición reparable se cumple —paquete no disponible, `sha256` divergente o
paquete no confiable— el estado **no** es `Repairable` y la instalación se trata como inválida y
**no reparable**, con su motivo visible
*(FR-8, NFR-5, UX-DR-17, UX-DR-18, UX-DR-20, AR-8, AR-9, AR-15, AD-7, AD-10, AD-22)*.

**Given** el estado de certificación de una instalación,
**When** Story 8.2, Story 10.3 o el servidor ramifican sobre `Installed` o `Repairable`,
**Then** ramifican sobre las **mismas** condiciones de certificación y reparabilidad de esta
historia: no existe una segunda definición de «instalación válida» ni de «reparable»,
**And** el servidor recibe la `validationStateText` derivada del `StateDisplayMap` y no de una
decisión propia sobre la integridad del árbol
*(FR-8, FR-10, AR-5, AR-13, AR-15)*.

### Story 7.2: Confines de la extracción dentro del directorio autorizado

Como jugador de LAN,
quiero que la instalación nunca escriba fuera del directorio que configuré,
so que un paquete malicioso no pueda tocar el resto de mi equipo.

**Acceptance Criteria:**

**Given** una operación de escritura de instalación,
**When** se resuelve la ruta de destino,
**Then** se resuelve contra la **raíz del directorio de instalación configurado** y se verifica que
la ruta canónica resuelta (tras resolver enlaces simbólicos) queda **dentro**,
**And** la comprobación se repite **antes de cada escritura**, no solo al inicio
*(FR-8, NFR-5, AR-9)*.

**Given** un manifiesto con una ruta absoluta,
**When** se evalúa,
**Then** el manifiesto **no lo admite** y la operación se rechaza con su causa registrada
*(FR-8, NFR-5, AR-9)*.

**Given** un destino fuera de la raíz de instalación,
**When** se resuelve,
**Then** se rechaza y se registra
*(FR-8, NFR-5, AR-9)*.

**Given** el layout del cliente,
**When** se inspeccionan las rutas de escritura,
**Then** la caché `manifests/` y la base `state.sqlite` viven bajo `%LOCALAPPDATA%\LanPartyHub`, y
los parciales en `downloads/`, **fuera** de la raíz de instalación
*(AR-10, AD-9, AD-11)*.

### Story 7.3: Instalación portable con manifiesto tipado y confirmación de lo privilegiado

Como jugador de LAN,
quiero que la instalación se haga con el mínimo de intervención y que cada operación que toque el
sistema me pida confirmación,
so que no ejecute nada que no haya aprobado.

**Acceptance Criteria:**

**Given** un paquete validado,
**When** Ana inicia la instalación,
**Then** el cliente extrae los archivos del paquete **dentro** del directorio autorizado y no fuera
de él,
**And** el cliente detecta una instalación existente y evita repetir operaciones ya completadas
cuando la instalación es válida
*(FR-8)*.

**Given** un manifiesto que declara una operación privilegiada,
**When** el cliente alcanza esa operación,
**Then** pide **confirmación explícita** antes de ejecutarla, en un `dialog-surface` de un solo nivel,
**And** `Esc` cancela la acción no destructiva y devuelve el foco al elemento invocador
*(FR-8, UX-DR-16, UX-DR-37)*.

**Given** una operación privilegiada reanudada tras un reinicio del cliente,
**When** se alcanza de nuevo la operación,
**Then** se vuelve a pedir la confirmación explícita: la confirmación **no** se hereda de una sesión
anterior
*(FR-8, UX-DR-37)*.

**Given** una instalación en curso,
**When** se observa el progreso,
**Then** la barra muestra fase («Extrayendo», «Aplicando manifiesto») y porcentaje,
**And** el porcentaje es un entero derivado de los bytes reales
*(FR-8, UX-DR-34, UX-DR-13)*.

**Given** una instalación fallida,
**When** el cliente persiste el error,
**Then** el error queda persistido y la operación es **reintentable sin reinstalar el cliente**, con
la acción «Reintentar» disponible
*(FR-8, NFR-9, UX-DR-34)*.

**Given** la instalación portable,
**When** se revisa el alcance,
**Then** los modos `Installer` y `External` **no existen** en el MVP
*(R-MVP-4, FR-8)*.

## Epic 8: El jugador lanza el juego y sabe por qué no puede

Ana lanza el juego instalado desde la Biblioteca o la ficha, ve el proceso en ejecución y, si no
puede jugar, tiene el motivo visible y la acción correcta a mano.

### Story 8.1: Detección del ejecutable y lanzamiento con argumentos y directorio de trabajo

Como jugador de LAN,
quiero pulsar «Jugar» y que el juego arranque con su configuración,
so que no tenga que configurarlo a mano.

**Acceptance Criteria:**

**Given** una instalación validada,
**When** Ana pulsa «Jugar»,
**Then** el cliente detecta el ejecutable definido en el catálogo, valida la instalación **antes**
de lanzar y lo lanza con sus argumentos de lanzamiento y su directorio de trabajo
*(FR-9, AR-22, AD-19)*.

**Given** un juego en ejecución,
**When** se consulta el panel de estado,
**Then** muestra «En ejecución»,
**And** el cliente puede **indicar** si el proceso está en ejecución
*(FR-9, UX-DR-6, AR-22)*.

**Given** un juego lanzado,
**When** se observa el escritorio,
**Then** el juego se ejecuta **fuera** de la ventana de LAN Party Hub, el cliente queda en segundo
plano y el panel de estado sigue mostrando «En ejecución»
*(FR-9, UX-DR-43)*.

**Given** un juego en ejecución,
**When** se teclea,
**Then** LAN Party Hub **no captura teclas**
*(FR-9, UX-DR-43)*.

**Given** el módulo de lanzamiento,
**When** se implementa,
**Then** usa la interfaz `IGameProcessRunner` del módulo `GameLauncher`,
**And** la conexión directa a servidores de juego queda **no implementada** y los *launchers*
externos quedan fuera del MVP
*(FR-9, AR-22, AD-19)*.

### Story 8.2: Bloqueo de «Jugar» con el motivo visible

Como jugador de LAN,
quiero saber por qué no puedo jugar sin abrir un diagnóstico,
so que la acción correcta esté a la vista.

**Acceptance Criteria:**

**Given** una instalación inválida **y reparable**,
**When** se muestra la Biblioteca o la ficha,
**Then** «Jugar» aparece **bloqueada** con el motivo visible junto a la etiqueta («No se puede
jugar: instalación incompleta», «No se puede jugar: falta el ejecutable», «No se puede jugar: el
paquete no coincide con la validación»),
**And** la acción primaria es «Reparar» y la secundaria «Diagnosticar» abre el error registrado
*(FR-9, UX-DR-17, UX-DR-18)*.

**Given** una instalación inválida **y no reparable**,
**When** se muestra la Biblioteca o la ficha,
**Then** «Jugar» aparece bloqueada con el motivo visible y la única acción primaria es «Descargar de
nuevo», con «Diagnosticar» como secundaria
*(FR-9, UX-DR-17, UX-DR-18)*.

**Given** una instalación inválida,
**When** se inspecciona la pantalla,
**Then** el motivo **no** está en un *tooltip*, **no** está solo en el log de Diagnóstico y **no**
requiere *hover* ni foco: aparece en la propia tarjeta o ficha, junto a la etiqueta «Jugar»
*(UX-DR-18)*.

**Given** una instalación no validada,
**When** se recorren las superficies,
**Then** **no hay segunda vía de lanzamiento**: ni el panel de estado, ni la ficha, ni el error de
lanzamiento ofrecen ejecutar el juego
*(FR-9, UX-DR-19)*.

**Given** una instalación inválida mostrada en la Biblioteca o en la ficha,
**When** se navega con `Tab` hasta «Jugar»,
**Then** el control bloqueado **recibe foco**, conserva un indicador de foco visible que no depende
del color de la insignia y no se omite del recorrido,
**And** el lector de pantalla anuncia el nombre de la acción, su estado bloqueado y el motivo visible
junto a la etiqueta
*(FR-9, UX-DR-26, UX-DR-38)*.

**Given** «Jugar» bloqueada con el foco activo en la Biblioteca o en la ficha,
**When** se pulsa `Enter`,
**Then** **no** se produce ningún cambio de estado, **no** se inicia el juego y **no** se dispara una
descarga, instalación o reparación implícita
*(FR-9, UX-DR-26)*.

**Given** un ejecutable ausente o una instalación inválida,
**When** se intenta lanzar,
**Then** el lanzamiento se impide y produce un error identificable con el ejecutable y su ruta
exacta cuando el problema es de ruta: «No se pudo iniciar: <causa>.»
*(FR-9, UX-DR-35)*.

### Story 8.3: Reparar sin volver a descargar

Como jugador de LAN,
quiero reparar una instalación inválida sin volver a bajar el paquete,
so que recupere el juego sin esperar otra descarga de varios gigabytes.

**Acceptance Criteria:**

**Given** una instalación inválida reparable,
**When** Ana pulsa «Reparar»,
**Then** la reparación rehace la instalación **desde el paquete ya validado**, sin volver a
descargar,
**And** la interfaz nombra la consecuencia: «Se reparará la instalación; la descarga no se repite»
*(FR-8, UX-DR-20)*.

**Given** una reparación en curso,
**When** se muestra,
**Then** «Jugar» permanece **bloqueada** con el motivo visible hasta que la instalación se valida
*(FR-8, UX-DR-17, UX-DR-18)*.

**Given** una instalación validada,
**When** se muestra la Biblioteca o la ficha,
**Then** la insignia muestra «Instalado · v1.2» o «Ya instalado · v1.2», y la acción primaria es
«Jugar»
*(FR-8, FR-9, UX-DR-6, UX-DR-17)*.

## Epic 9: El organizador ve el estado de cada equipo sin preguntar

Luis ve en el servidor la tabla de equipos conectados, sus instalaciones, sus descargas en curso y
sus errores con causa y siguiente paso, y puede diagnosticar una incidencia sin rondas de
preguntas por el pasillo.

### Story 9.1: Tabla densa de clientes conectados

Como organizador del evento,
quiero ver en una tabla todos los equipos conectados con su estado,
so que sepa de un vistazo qué está pasando en la sala.

**Acceptance Criteria:**

**Given** la sección Clientes del servidor,
**When** Luis la abre,
**Then** ve una **tabla densa**, no una colección de tarjetas, con una fila por cliente y 40 px de
alto,
**And** las celdas de dato usan `{typography.body-strong}` y las unidades `{typography.meta}`,
**And** el orden es estable por nombre de equipo y la fila es seleccionable para ver su detalle
*(FR-10, UX-DR-14)*.

**Given** la tabla de clientes,
**When** se listan las columnas,
**Then** muestran identificador, nombre del equipo, estado de conexión, juegos y versiones
instalados, descargas activas, progreso de las instalaciones y errores registrados
*(FR-10, UX-DR-14)*.

**Given** un cliente compatible,
**When** se conecta y se desconecta,
**Then** el servidor refleja la conexión y la desconexión en la tabla
*(FR-10)*.

**Given** la vista de clientes,
**When** se solicita,
**Then** se obtiene **exclusivamente** por REST con `GET /api/v1/clients`, que devuelve
`ClientRegistrationView[]` con la forma exacta (`clientId`, `machineName`, `clientVersion`,
`protocolVersion`, `connectionState`, `lastSeenAt`),
**And** **no existe** el evento `ClientsSnapshotChanged` en el contrato
*(FR-10, R-MVP-9, AR-3, AD-29)*.

**Given** el servidor,
**When** se inspecciona la persistencia,
**Then** `ClientRegistration` es propiedad del servidor y su **clave** es el `clientId`: el servidor
**no** deduplica, **no** agrupa, **no** fusiona ni reasigna filas por `machineName`
*(AR-13, AR-14, AD-23, AD-30)*.

**Given** dos clientes con **distinto** `clientId` que proponen el **mismo** `machineName`,
**When** ambos se registran,
**Then** el servidor **no rechaza** al segundo ni lo deja invisible: le asigna un `machineName`
**único derivado** del propuesto —el `stem` recortado más el guion y los **32 dígitos hexadecimales**
del `clientId`— y lo **persiste**,
**And** ambos equipos **coexisten** en la tabla de clientes con **nombres efectivos distintos**, y el
cliente que ya usaba el nombre propuesto **conserva** el suyo,
**And** el `clientId` de cada uno **no cambia**
*(FR-10, AR-14, AD-30)*.

**Given** un cliente cuyo `machineName` propuesto no cumple las reglas de forma (vacío tras
recortar, más de 64 caracteres, con caracteres de control, con espacios iniciales, finales o
interiores consecutivos, o con un carácter fuera del conjunto admitido),
**When** se registra ante el servidor,
**Then** el registro se **rechaza** con un `ApiError` del `ErrorCatalog` cuyo `code` es **propio** y
**distinto** del de «firma inválida» y del de «fingerprint de ancla no coincide», con `cause` y
`nextStep` **obligatorios**,
**And** el cliente **queda sin registrar y no puede operar**, y el error se presenta con `message`,
`cause` y `nextStep` tal cual, sin texto inventado fuera de la tabla
*(FR-10, AR-4, AR-14, AD-30)*.

**Given** un `machineName` **no válido** o una **colisión** resuelta,
**When** el servidor registra el hecho,
**Then** lo hace en el logging estructurado **JSON** con `clientId` y `operationId` de correlación, y
con un `code` del `ErrorCatalog` **distinto** en cada caso, de modo que el rechazo y la colisión son
**diagnosticables** y distinguibles entre sí en los logs del servidor
*(AR-14, AR-20, AD-15, AD-25, AD-30)*.

**Given** un cliente que cambia el nombre de su equipo,
**When** se registra de nuevo,
**Then** propone el nuevo nombre y el servidor aplica la misma regla: si el nuevo nombre está
libre lo adopta, y si colisiona le asigna el nombre único derivado y se lo notifica,
**And** el `clientId` **no cambia** en ningún caso,
**And** el servidor **no ofrece** en el MVP una operación de renombrado de otro cliente
*(FR-10, AR-14, AD-30)*.

**Given** la vista de clientes,
**When** el servidor expone la columna de nombre del equipo,
**Then** muestra **siempre el nombre efectivo** —el único bajo el que esa fila está registrada—, nunca
el nombre propuesto que colisionó,
**And** el `machineName` **no** es una credencial: no hay contraseña, token, sesión ni rol asociados,
no se usa para autenticar ninguna petición y no confiere permisos
*(FR-10, AR-14, AD-6, AD-12, AD-30)*.

**Given** un cliente cuyo `machineName` propuesto está en la longitud máxima admitida y **colisiona**
con el de otro equipo ya registrado,
**When** el servidor deriva el nombre efectivo,
**Then** el nombre efectivo es `stem_truncado` + el sufijo fijo `"-"` seguido de los **32 dígitos
hexadecimales en minúsculas** de la forma canónica del `clientId` (`Guid.ToString("N")`, sin
guiones, sin prefijo y sin corchetes),
**And** la longitud del nombre efectivo **nunca excede 64 puntos de código Unicode**, medidos en
puntos de código (`System.Text.Rune`), no en bytes ni en unidades de código UTF-16: un `stem` de 64
ideogramas CJK se recorta a 31 ideogramas, y uno con emoji se recorta sin partir ningún emoji,
**And** el sufijo tiene **longitud fija de 33 puntos de código**, de modo que el `stem` se recorta
siempre a **≤ 31 puntos de código**,
**And** la truncación del `stem` **cae siempre en un límite de punto de código** y el `stem`
truncado **no puede quedar vacío** (si lo estuviera, se usa el `stem` de reserva `equipo`),
**And** el nombre efectivo derivado **vuelve a ser admisible** por las reglas de forma (longitud y
set de caracteres), **nunca necesita volver a ser derivado** y su **unicidad está garantizada por
construcción**, sin búsqueda de nombres libres
*(FR-10, AR-14, AD-30, AD-31)*.

**Given** un nombre propuesto que colisiona con el de otro `clientId` ya registrado,
**When** el servidor deriva el nombre efectivo,
**Then** el nombre efectivo es una **función pura** de (`stem`, `clientId`): el servidor **no**
busca nombres libres, **no** incrementa ningún contador, **no** prueba candidatos sucesivos y
**no** puede agotar el espacio de nombres,
**And** la **forma canónica** del nombre efectivo (Unicode NFKC, eliminación de marcas
diacríticas y minúsculas Unicode invariantes, en ese orden) **conserva intacta** la cola de 33
puntos de código del sufijo, porque esas operaciones son la identidad sobre `-` y los dígitos
hexadecimales en minúsculas,
**And** dos nombres efectivos derivados iguales en forma canónica **implican el mismo
`clientId`**, luego **dos clientes distintos nunca reciben el mismo nombre efectivo derivado**,
**And** el `clientId` del cliente **no cambia**, no se fusiona ninguna fila y no se reasigna la fila de
otro cliente
*(FR-10, AR-14, AD-30, AD-31)*.

**Given** un cliente que se registra,
**When** llama al punto de entrada del registro del cliente,
**Then** envía un cuerpo con la forma exacta `{ clientId, machineName, clientVersion,
protocolVersion }`, donde `clientId` es el UUID v7 emitido por el cliente, `machineName` es el
**nombre propuesto** y `clientVersion` y `protocolVersion` son los **mismos valores** que las
cabeceras `X-Client-Version` y `X-Protocol-Version` (el cuerpo no introduce una segunda versión ni
una segunda fuente de verdad),
**And** el cuerpo **no** lleva `schemaVersion`: el registro es la **única** mutación exenta de
`schemaVersion` y ninguna otra mutación puede usar esa exención,
**And** recibe una respuesta con la forma exacta `{ clientId, machineName, connectionState,
lastSeenAt, renamed }`, donde `machineName` es **siempre el nombre efectivo** y `renamed` es un
booleano que vale `true` **únicamente** cuando el servidor resolvió una colisión y el `machineName`
devuelto **difiere** del propuesto
*(FR-10, AR-14, AD-5, AD-6, AD-22, AD-23, AD-30, AD-32)*.

**Given** un cliente que se registra ante el servidor,
**When** el servidor responde al registro,
**Then** el **alta**, la **reconexión** y el **renombrado** ocurren en **la misma** operación de
registro, que es el **único** punto de entrada del cliente al servidor: no existe un endpoint
adicional de registro, de renombrado ni de administración de nombres,
**And** el éxito responde **`200 OK`** —no `201 Created`, porque la fila es actualizable—, con el
nombre efectivo y el indicador `renamed`,
**And** un nombre mal formado responde **`422 Unprocessable Content`** con la forma única `ApiError`
(`code`, `message`, `cause`, `nextStep`) y un `code` **propio**,
**And** una versión incompatible responde **`426 Upgrade Required`** con la versión de servidor
requerida y el rango mínimo soportado, y esa comprobación ocurre **antes** de cualquier validación
de nombre,
**And** la **colisión no es un error**: el registro **tiene éxito** con `renamed: true` y el nombre
efectivo en la respuesta,
**And** una **reconexión** (el cliente registra de nuevo con su `clientId` y su nombre efectivo
actual) **conserva** la fila y el nombre: el servidor actualiza `connectionState`, `lastSeenAt` y
`clientVersion`, **no** asigna un nombre nuevo y **no** dispara la derivación, de modo que un
cliente reconectado **nunca colisiona consigo mismo**
*(FR-10, AR-14, AD-5, AD-6, AD-15, AD-22, AD-23, AD-25, AD-29, AD-30, AD-32)*.

### Story 9.2: Proyección de instalaciones y de operaciones en curso

Como organizador del evento,
quiero ver qué tiene instalado cada equipo y qué está descargando ahora,
so que sepa si un equipo va retrasado sin preguntarle.

**Acceptance Criteria:**

**Given** un cliente con operaciones en curso,
**When** el servidor recibe `DownloadProgressUpdated` o `InstallationProgressUpdated`,
**Then** la tabla muestra la descarga activa y el progreso de la instalación con el valor real
*(FR-10, AR-3, AD-22)*.

**Given** un cliente que reporta su estado,
**When** el servidor recibe `ClientStateChanged`,
**Then** proyecta `installedGames[]` (`gameId`, `installedVersion`, `validationState`) y
`activeOperations[]` (`operationId`, `operationType`, `state`) con la **forma exacta** del
contrato
*(FR-10, AR-3, AR-22)*.

**Given** la proyección del servidor,
**When** se inspecciona la persistencia,
**Then** `ClientInstallationView` es una entidad **distinta** de `ClientInstallation`, con nombre
propio, y guarda `validationStateText` —una **cadena de presentación** copiada del
`StateDisplayMap`— **nunca** el enum de `Shared`,
**And** el servidor **no** persiste el `InstallationState` ni el `DownloadState` de otro cliente
*(AR-13, AR-5, AD-22)*.

**Given** la proyección del servidor,
**When** se presenta,
**Then** **no** muestra la `installPath` del cliente: la vista muestra la versión instalada y la
existencia de la instalación, no su ruta
*(AR-13, AD-22)*.

**Given** un cliente cuyo juego fue retirado del catálogo,
**When** el servidor proyecta su estado,
**Then** **no borra** la `ClientInstallationView`: la proyección refleja lo que el cliente realmente
tiene instalado
*(AR-16, AD-26)*.

**Given** una operación terminada con el resultado `Paquete retirado del catálogo`,
**When** el servidor actualiza la proyección,
**Then** **no** persiste ese resultado como `ClientError`,
**And** lo presenta como resultado de operación distinto del fallo, usando únicamente la microcopia
de la tabla asignada en `Shared`
*(AR-8, R-MVP-8, AD-25, AD-26)*.

### Story 9.3: Errores registrados, con causa y siguiente paso

Como organizador del evento,
quiero ver el error registrado de cada equipo con su causa,
so que sepa si necesita reintentar o asistencia.

**Acceptance Criteria:**

**Given** un cliente que reporta un error,
**When** el servidor recibe `ClientErrorReported`,
**Then** persiste un `ClientError` con `errorId`, `clientId`, `operationId`, `code`, `message`,
`cause`, `nextStep` y `occurredAt`,
**And** el `error` del payload es del **único** tipo `ApiError` del sistema
*(FR-10, NFR-7, AR-4, AD-22)*.

**Given** un error registrado,
**When** Luis abre el detalle de la fila del equipo,
**Then** ve el error con su causa («No se pudo validar el paquete: el hash no coincide») y la
descarga activa con su progreso real
*(FR-10, NFR-7, UX-DR-44)*.

**Given** un cliente incompatible,
**When** aparece en la tabla,
**Then** su fila muestra su versión y su diagnóstico, e indica que **no opera**, sin iniciar
operaciones de juego
*(FR-10, FR-4, UX-DR-31)*.

**Given** la fila de un cliente desconectado,
**When** se muestra,
**Then** el estado va en `{colors.state-danger}` y el error asociado se muestra en el detalle de la
fila
*(FR-10, UX-DR-44)*.

**Given** el `toast-error` de un error,
**When** se presenta,
**Then** su contenido es causa, acción intentada y siguiente paso, sin iconografía: el texto es el
aviso,
**And** **no** sustituye a la persistencia del error en el registro
*(UX-DR-15, UX-DR-36)*.

### Story 9.4: Diagnóstico correlacionado en el cliente

Como jugador de LAN,
quiero consultar el registro de diagnóstico de mi cliente,
so que pueda comunicar al organizador el nombre estable del error que me ocurrió.

**Acceptance Criteria:**

**Given** la sección Configuración y diagnóstico del cliente,
**When** se abre,
**Then** ve el registro de errores persistidos de su equipo, con el `code` estable, el mensaje, la
causa y el siguiente paso
*(NFR-7, UX-DR-23, UX-DR-36)*.

**Given** un error de una descarga, instalación o lanzamiento,
**When** se consulta el registro,
**Then** el error está **asociado** a cliente, descarga, instalación o lanzamiento
*(NFR-7)*.

**Given** los logs del cliente y del servidor,
**When** se correlacionan,
**Then** comparten `operationId` y `clientId`, de modo que un error del servidor y su correspondiente
en el cliente se pueden unir
*(NFR-7, AR-20, AD-15, AD-23)*.

**Given** el visor de diagnóstico,
**When** se abre,
**Then** dispone de **variante clara** con los tokens `-light` de `DESIGN.md`,
**And** el modo oscuro es el predeterminado
*(UX-DR-41, UX-DR-1)*.

## Epic 10: El jugador actualiza su biblioteca a la última versión

Ana detecta que su instalación está desactualizada, obtiene la versión completa del catálogo,
sustituye la anterior y repara una instalación inválida sin volver a descargar el paquete.

### Story 10.1: Detección de versión desactualizada

Como jugador de LAN,
quiero que mi biblioteca me avise de que hay una versión nueva,
so que no juegue una versión obsoleta sin saberlo.

**Acceptance Criteria:**

**Given** una instalación validada cuya versión instalada es anterior a la versión publicada en el
catálogo para ese `gameId`,
**When** el cliente sincroniza el catálogo,
**Then** identifica que su versión está desactualizada y muestra la insignia
`instalado con versión desactualizada` con la acción «Actualizar»
*(FR-11, UX-DR-32, AR-5, AD-26)*.

**Given** la comparación de versiones,
**When** se determina cuál es la más reciente,
**Then** usa el **orden semántico de versión declarado en el catálogo**, no la comparación
lexicográfica de cadenas,
**And** la regla de orden de versiones es **única** y vive en `Shared`
*(FR-11, AR-5, AD-10)*.

**Given** la insignia de versión desactualizada,
**When** se muestra,
**Then** la versión instalada y la disponible se muestran en monoespaciada
*(UX-DR-2, UX-DR-21)*.

**Given** un cliente con una instalación desactualizada,
**When** se muestra la Biblioteca o la ficha,
**Then** «Actualizar» sustituye a la acción primaria y «Jugar» permanece disponible **solo si** la
instalación sigue siendo válida
*(FR-11, UX-DR-17)*.

### Story 10.2: Actualización a versión completa

Como jugador de LAN,
quiero pasar a la versión nueva con una descarga completa del catálogo,
so que mi biblioteca quede al día.

**Acceptance Criteria:**

**Given** un juego con versión desactualizada,
**When** Ana pulsa «Actualizar»,
**Then** el cliente puede descargar e instalar la **versión completa** sustituyendo la anterior
conforme al manifiesto
*(FR-11)*.

**Given** una actualización en curso,
**When** se observa,
**Then** sigue las mismas reglas de descarga e instalación que el flujo inicial: verificación de
firma y `schemaVersion` antes de escribir, validación de `sha256` al final, y estado de vista
derivado coherente
*(FR-11, NFR-5, AR-8)*.

**Given** el alcance del MVP,
**When** se revisa el mecanismo de actualización,
**Then** se resuelven **versiones completas**: las actualizaciones diferenciales avanzadas **no**
existen en el MVP
*(FR-11, R-MVP-4)*.

**Given** un snapshot del catálogo que retira un juego,
**When** el cliente lo aplica,
**Then** la instalación existente **no se borra** y la retirada no altera la validez de lo ya
instalado
*(FR-11, AR-16, AD-26)*.

### Story 10.3: Reparación de una instalación interrumpida

Como jugador de LAN,
quiero que una instalación a medias se marque como reparable en vez de obligarme a repetir el
paquete entero,
so que recupere el juego sin perder tiempo ni datos.

**Acceptance Criteria:**

**Given** una instalación incompleta,
**When** el cliente valida la instalación,
**Then** la marca como reparable en lugar de repetir el paquete entero,
**And** en la Biblioteca y en la ficha «Jugar» permanece bloqueada con el motivo visible y «Reparar»
es la acción primaria
*(FR-8, FR-11, UX-DR-45, UX-DR-17)*.

**Given** un reinicio del equipo a mitad de una operación,
**When** el cliente se abre,
**Then** la operación se recupera con su progreso persistido y no obliga a reiniciar el paquete desde
cero
*(FR-5, FR-8, NFR-3, NFR-9, SM-2)*.

**Given** un error de instalación persistido,
**When** se decide reintentar,
**Then** el reintento es posible **sin reinstalar el cliente** y la operación relanza desde el
progreso persistido
*(FR-8, NFR-9, UX-DR-34)*.

## Epic 11: La interfaz cumple su contrato visual, de accesibilidad y de escritorio

La interfaz del cliente y del servidor es coherente con el contrato visual aprobado, es operable
solo con teclado, cumple WCAG 2.2 AA, aplica la regla única de tamaños y progreso y se comporta
bien en una sala a oscuras con las manos ocupadas.

### Story 11.1: Sistema de tokens y tema

Como diseñador de la interfaz,
quiero una capa de componentes construida sobre los tokens aprobados,
so que la interfaz se vea igual en el cliente y en el servidor y no aparezcan colores sueltos.

**Acceptance Criteria:**

**Given** el conjunto de tokens de `DESIGN.md`,
**When** se implementa la capa de componentes,
**Then** usa **solo** esos tokens: 4 superficies, 5 niveles de tinta, 2 acentos con función
(`accent-lan` = sistema sano, `accent-action` = acción disponible), 4 colores de estado, línea de
pelo y borde fuerte, más el juego de tokens `-light`,
**And** ningún componente introduce un token que no exista en `DESIGN.md`
*(UX-DR-1)*.

**Given** una superficie cualquiera,
**When** se construye la jerarquía visual,
**Then** la profundidad se expresa **tonalmente**, con tres niveles de superficie y línea de pelo,
**And** **no** se usa sombra proyectada como indicador de *hover* ni como jerarquía, salvo el
diálogo modal y el menú desplegable (`surface-sunken` al 40 % de opacidad)
*(UX-DR-3)*.

**Given** un control, una tarjeta, un panel o un diálogo,
**When** se aplica la geometría,
**Then** los radios son 8 px en controles, 12 px en tarjetas y paneles, 16 px en diálogos, 4 px en
campos de texto pequeños y etiquetas, y `full` solo en píldoras de estado (alto 20 px) y en el punto
de presencia de conexión (círculo de 8 px),
**And** los separadores son `{spacing.gutter}` entre columnas, `{spacing.panel}` dentro de un panel
y `{spacing.card}` dentro de una tarjeta o diálogo
*(UX-DR-4)*.

**Given** la rejilla de catálogo y las tarjetas,
**When** se renderizan,
**Then** la última fila se alinea a la izquierda y las tarjetas **no** se estiran para rellenar
*(UX-DR-4, UX-DR-42)*.

### Story 11.2: Tipografía y datos literales

Como jugador de LAN,
quiero que las rutas, los hashes y las versiones se lean con precisión,
so que pueda copiarlos y comunicarlos sin errores.

**Acceptance Criteria:**

**Given** un texto de interfaz,
**When** se compone,
**Then** la interfaz usa `Inter` en sus siete pesos declarados y `Cascadia Mono` **solo** para datos
que deben alinearse en columna: rutas, hashes, versiones exactas, identificadores de paquete y
valores numéricos derivados de tamaño y porcentaje
*(UX-DR-2)*.

**Given** un dato literal,
**When** se muestra,
**Then** se muestra en su forma exacta, sin redondear ni traducir,
**And** la unidad («GB» o «%») que acompaña a un valor numérico va en `{typography.meta}`
*(UX-DR-2, UX-DR-6)*.

**Given** un texto largo en una tarjeta,
**When** se renderiza,
**Then** el nombre se trunca a **una** línea con elipsis y **nunca** se envuelve a tres
*(UX-DR-2, UX-DR-10)*.

### Story 11.3: Regla única de tamaños y progreso

Como jugador de LAN,
quiero ver los tamaños y los porcentajes en un formato único,
so que pueda compararlos entre juegos sin descifrar unidades.

**Acceptance Criteria:**

**Given** cualquier tamaño mostrado,
**When** se presenta,
**Then** se expresa en **GB decimales (10⁹ bytes)** con separador decimal español (coma) y
**exactamente un decimal** («7,6 GB»),
**And** no se mezclan unidades: no hay MiB ni GiB, ni «kB» junto a «GB»
*(UX-DR-5)*.

**Given** un porcentaje mostrado,
**When** se presenta,
**Then** es un **entero** derivado de los bytes transferidos sobre los bytes totales («42 %»),
**And** nunca hay decimales, ni fracción alternativa («42/100»), ni redondeo a decenas
*(UX-DR-5)*.

**Given** dos magnitudes mostradas juntas,
**When** se compone el par,
**Then** se expresan en la **misma** unidad, con la **misma** precisión y en el **mismo** orden
(«3,2 GB de 7,6 GB»), con el total siempre segundo
*(UX-DR-5)*.

**Given** la barra y su texto,
**When** se renderizan,
**Then** ambos derivan del mismo par (bytes transferidos, bytes totales) y **nunca** se contradicen:
si el texto muestra «3,2 GB de 7,6 GB», el porcentaje corresponde a esa misma fracción
*(UX-DR-5, UX-DR-13)*.

**Given** una operación completada,
**When** se muestra el tamaño,
**Then** el texto sigue la forma «<n> % · <t> GB» («100 % · 7,6 GB»)
*(UX-DR-5)*.

### Story 11.4: Microcopia de estado crítico

Como jugador de LAN,
quiero que cada estado tenga un texto fijo y comprensible,
so que nunca tenga que interpretar lo que veo.

**Acceptance Criteria:**

**Given** los estados críticos del producto,
**When** se muestra su microcopia,
**Then** son los textos fijos del contrato: «Descargando · 42 % · 3,2 GB de 7,6 GB», «Pausada · 42 %
· 3,2 GB de 7,6 GB», «Reanudando desde 42 % · 3,2 GB de 7,6 GB», «Descargado · 100 % · 7,6 GB»,
«Paquete validado», «Paquete dañado: el hash no coincide · 7,6 GB», «Paquete no confiable: no se
pudo validar contra la raíz de confianza · 7,6 GB», «Instalado · v1.2», «Instalando · 68 %», «Ya
instalado · v1.2», «En ejecución»,
**And** ningún texto se inventa fuera del `StateDisplayMap` y del `ErrorCatalog`
*(UX-DR-6, AR-8, R-MVP-7, R-MVP-8)*.

**Given** la voz de la interfaz,
**When** se escribe cualquier texto,
**Then** usa segunda persona y verbo en primera («Pulsa», «Elige», «Indica», «Comprueba»), nunca
«el usuario»,
**And** sin emojis, sin superlativos, sin gamificación, sin coletilla comercial,
**And** no dice «¿estás seguro?» salvo que la acción destruya trabajo no recuperable, y en ese caso
nombra qué se pierde
*(UX-DR-6, UX-DR-22)*.

**Given** un dato de tiempo,
**When** se muestra,
**Then** se dice «hace 2 min», nunca «en 30 segundos» para una descarga: la velocidad no está
validada
*(UX-DR-6, NFR-2)*.

### Story 11.5: Componentes base y jerarquía de acciones

Como jugador de LAN,
quiero que en cada pantalla haya una sola acción principal evidente,
so que sepa qué puedo pulsar ahora mismo.

**Acceptance Criteria:**

**Given** una superficie cualquiera,
**When** se compone,
**Then** hay **una sola** acción primaria (`button-primary`), que se deshabilita cuando el
prerrequisito no se cumple (paquete no validado, cliente incompatible, sin servidor),
**And** el foco no cambia el alto: 36 px estables
*(UX-DR-7)*.

**Given** una acción secundaria, de peligro o fantasma,
**When** se presenta,
**Then** `button-secondary` actúa sin competir con la primaria y nunca es la única acción
disponible en un estado vacío,
**And** `button-danger` se reserva a cancelar una descarga, eliminar una instalación o descartar un
error, y exige confirmación solo si se pierde trabajo no recuperable,
**And** `button-ghost` es la acción terciaria y nunca la única vía a un resultado
*(UX-DR-8)*.

**Given** una tabla de clientes,
**When** se renderiza,
**Then** usa `data-table-row` con 40 px de alto, separador inferior de 1 px `border-hairline` y
orden estable por nombre de equipo
*(UX-DR-14)*.

**Given** un diálogo,
**When** se muestra,
**Then** es `dialog-surface` de un **solo** nivel, con encabezado `headline`, cuerpo `body` y pie con
acciones alineadas a la derecha,
**And** `Esc` cancela la acción no destructiva y **devuelve el foco** al elemento invocador
*(UX-DR-16)*.

### Story 11.6: Arquitectura de información y navegación

Como jugador de LAN,
quiero navegar siempre igual y saber dónde está cada cosa,
so que no tenga que aprender la aplicación mientras juego.

**Acceptance Criteria:**

**Given** el cliente,
**When** se recorre su arquitectura de información,
**Then** existen siete superficies: Conexión (apertura), Catálogo, Ficha de videojuego (desde la
tarjeta), Descargas, Biblioteca, Configuración y diagnóstico, y Panel de estado (columna derecha,
persistente),
**And** la navegación es una **columna**, no una barra inferior,
**And** hay **un solo nivel de modal**
*(UX-DR-23, UX-DR-1)*.

**Given** el servidor,
**When** se recorre su arquitectura de información,
**Then** existen tres secciones: Catálogo, Clientes y Paquetes
*(UX-DR-24)*.

**Given** las primitivas de teclado,
**When** se usan,
**Then** `Enter` confirma el campo enfocado, `Esc` cierra el diálogo superior y, si no hay diálogo,
limpia la búsqueda, `Ctrl+F` enfoca la búsqueda del catálogo desde cualquier superficie, `Ctrl+,`
abre Configuración y diagnóstico, `Ctrl+1`…`Ctrl+4` saltan a Catálogo, Descargas, Biblioteca y
Configuración, y `Espacio` pausa o reanuda la descarga enfocada
*(UX-DR-25)*.

**Given** el *hover*,
**When** una acción solo aparece al pasar el ratón,
**Then** tiene equivalente de teclado o está siempre visible en el detalle
*(UX-DR-27)*.

**Given** una descarga en curso,
**When** se navega a otra superficie,
**Then** la navegación es libre y el progreso vive en el panel derecho
*(UX-DR-27)*.

### Story 11.7: Accesibilidad

Como jugador de LAN que usa solo el teclado o un lector de pantalla,
quiero que todas las acciones sean alcanzables y announced,
so que no dependa del ratón ni de la vista.

**Acceptance Criteria:**

**Given** cualquier superficie del cliente o del servidor,
**When** se audita el contraste,
**Then** cumple **WCAG 2.2 AA**
*(UX-DR-38)*.

**Given** cualquier estado alcanzable con el ratón,
**When** se navega con `Tab` / `Shift+Tab`,
**Then** es alcanzable con teclado, incluidos diagonales, tabla de clientes, filas de historial y
acciones de error,
**And** el orden de tabulación sigue el orden de lectura de cada superficie,
**And** el foco es **siempre** visible con `{colors.accent-action}` y no se suprime
*(UX-DR-38)*.

**Given** un cambio de superficie,
**When** un lector de pantalla está activo,
**Then** se anuncia «Catálogo, catálogo de videojuegos, 24 juegos»
*(UX-DR-39)*.

**Given** una descarga en curso,
**When** cambia el porcentaje,
**Then** se anuncia por cambio de porcentaje significativo (10 %, 25 %, 50 %, 75 %, 100 %), no en
cada *tick*,
**And** los errores se anuncian como texto, no solo visualmente
*(UX-DR-39)*.

**Given** un estado con color,
**When** se representa,
**Then** lleva **siempre** además texto: el color nunca es la única señal, y el nombre accesible de
la insignia incluye su estado
*(UX-DR-38, UX-DR-12)*.

**Given** una barra de progreso,
**When** se representa,
**Then** el porcentaje va en texto junto a la barra y la interfaz **no** depende del color de la
barra
*(UX-DR-38, UX-DR-13)*.

**Given** un objetivo interactivo,
**When** se mide,
**Then** cumple el mínimo de 24×24 px, y los botones primarios y los campos 36 px de alto
*(UX-DR-40)*.

**Given** un error,
**When** el jugador quiere comunicarlo al organizador,
**Then** tiene un **nombre estable** en texto
*(UX-DR-38, NFR-7)*.

### Story 11.8: Comportamiento de escritorio y escalado

Como jugador de LAN,
quiero que la interfaz se comporte bien en un portátil de sala con cualquier tamaño de ventana,
so que no se descuadre mientras juego.

**Acceptance Criteria:**

**Given** una ventana redimensionada,
**When** se recalcula la rejilla del catálogo,
**Then** la columna de navegación conserva su ancho y la rejilla calcula mínimo 3 columnas a 1280 px
y hasta 6; por debajo de 3 columnas cabe a 2, y por debajo de 2, a lista de una columna,
**And** la última fila se alinea a la izquierda y las tarjetas no se estiran
*(UX-DR-42, UX-DR-4)*.

**Given** una ventana estrecha,
**When** se visualiza,
**Then** el panel derecho de estado se reduce a la línea de presencia y la operación en curso, sin
desaparecer
*(UX-DR-42)*.

**Given** el escalado del sistema operativo,
**When** se cambia el escalado de texto o el tamaño de ventana,
**Then** el escalado lo gobierna el sistema operativo y la interfaz **no** reimplanta el escalado con
factores propios
*(UX-DR-40)*.

**Given** una red sin Internet,
**When** el cliente opera,
**Then** ninguna superficie muestra un error de red global,
**And** un icono de red ausente no se confunde con «sin servidor»
*(UX-DR-42, NFR-1)*.

**Given** el servidor y un cliente en el mismo equipo,
**When** ambos están en ejecución,
**Then** la instancia del servidor **no** ocupa el panel de estado de un cliente
*(UX-DR-43)*.

## Epic 12: Spikes de decisión técnica (experimentos, no producto)

Las decisiones que dependen de evidencia técnica se cierran con experimentos acotados y
publicables. Cada spike tiene criterios de éxito observables y carácter declarado. **S-2 es
bloqueante** para el cierre del MVP; S-1, S-3, S-4 y S-5 son no bloqueantes y su resultado es
una decisión de fase posterior. Ningún spike introduce capacidad de producto en el MVP.

> **Regla de alcance de esta épica.** Un spike **no** es una historia de producto: no produce
> capacidad visible en el cliente ni en el servidor, no se convierte en épica de backlog de
> funcionalidad y no puede modificar el contrato del MVP. Su entregable es un **informe con
> resultado** que decide una pregunta abierta. La única excepción de carácter es S-2, que es
> **bloqueante para el cierre** del MVP: si su resultado es negativo, el MVP **no se puede cerrar**
> hasta que se resuelva la confianza de paquetes.

### Story 12.1: S-2 · Raíz de confianza — validación del bootstrap out-of-band (BLOQUEANTE)

Como responsable de seguridad del producto,
quiero validar experimentalmente la cadena firma → manifiesto → paquete con bootstrap out-of-band
y rechazo previo a persistir,
so que el cierre del MVP no dependa de un mecanismo no verificado.

**Alcance mínimo:** fijar el ancla por fingerprint out-of-band (QR o contraste en la pantalla del
organizador); firmar un manifiesto con Ed25519; verificar; comprobar el rechazo de manifiesto
alterado; **simular un intermediario que sustituye la clave del ancla**.

**Acceptance Criteria:**

**Given** un manifiesto alterado,
**When** se valida,
**Then** **se rechaza** y no se ejecuta
*(Criterio S-2a, AD-21)*.

**Given** un manifiesto firmado por otra clave,
**When** se valida contra el ancla fijada,
**Then** **se rechaza**
*(Criterio S-2b)*.

**Given** un paquete cuyo `sha256` no coincide,
**When** se valida,
**Then** **se rechaza**
*(Criterio S-2c)*.

**Given** el fingerprint del ancla,
**When** se muestra y se confirma en la UI,
**Then** la confirmación ocurre **antes** de fijar el ancla
*(Criterio S-2d)*.

**Given** un intermediario que presenta una clave de ancla distinta de la esperada,
**When** el cliente la recibe,
**Then** **rechaza la conexión**, **no persiste el ancla sustituta** y **no escribe nada**: ni
manifiesto en `manifests/`, ni archivo parcial, ni operación registrada
*(Criterio S-2e, prueba MITM de sustitución de ancla)*.

**Given** una firma inválida contra el ancla ya fijada,
**When** se procesa,
**Then** **no queda ninguna escritura en disco ni operación iniciada** (prueba de rechazo previo a
persistir)
*(Criterio S-2f)*.

**Given** el resultado del spike,
**When** se publica,
**Then** se emite un **informe** con el resultado, la configuración y la evidencia de cada criterio,
**And** el resultado **no** introduce ninguna capacidad nueva en el cliente ni en el servidor: el
diseño de la cadena ya está fijado en AD-2, AD-8 y AD-27 y el spike lo **valida**
*(AD-21, R-MVP-5)*.

**Given** el carácter declarado de S-2,
**When** se evalúa el resultado del spike,
**Then** es **BLOQUEANTE**: su resultado es **requisito previo** de FR-6 y de SM-3, de modo que si
el resultado es negativo el MVP **no se puede cerrar** hasta que se resuelva la confianza de
paquetes,
**And** el carácter es **normativo**, no descriptivo: bloquea el **cierre** del MVP, no su
construcción, porque el diseño de la cadena ya está fijado en AD-2, AD-8 y AD-27,
**And** si S-2 no se ejecuta o fracasa, el MVP **no se puede cerrar** y el backlog queda bloqueado
en ese punto
*(AR-25, R-MVP-5, AD-21)*.

### Story 12.2: S-1 · Descubrimiento — validación de mDNS junto a la dirección manual (NO BLOQUEANTE)

Como arquitecto del producto,
quiero medir el comportamiento real de mDNS en redes con políticas distintas,
so que el riesgo R-03 quede documentado antes de confiar en él.

**Alcance mínimo:** servidor que anuncia `_lanpartyhub._tcp`; cliente que descubre y compara con la
dirección manual.

**Acceptance Criteria:**

**Given** la LAN de prueba,
**When** el cliente busca el servidor,
**Then** lo descubre **sin** introducir la dirección
*(Criterio S-1a)*.

**Given** la misma red,
**When** se usa la dirección manual,
**Then** conecta igualmente
*(Criterio S-1b)*.

**Given** al menos dos redes con políticas distintas (con y sin *multicast*),
**When** se prueba,
**Then** el comportamiento queda **registrado** en un informe
*(Criterio S-1c)*.

**Given** el resultado del spike,
**When** se publica,
**Then** es una decisión de fase posterior: **no** cambia ningún artefacto del MVP,
**And** si el spike no se ejecuta o fracasa, el MVP **no cambia** (la vía manual basta, AD-3)
*(AD-21, R-MVP-5)*.

**Given** el carácter declarado de S-1,
**When** se evalúa el resultado del spike,
**Then** es **NO BLOQUEANTE**: su resultado es una decisión de fase posterior y **no** es requisito
previo de ninguna capacidad del MVP,
**And** el carácter es **normativo**, no descriptivo: puede no ejecutarse, ejecutarse tarde o
fracasar **sin** que ningún artefacto del MVP cambie
*(AR-25, R-MVP-5, AD-21)*.

### Story 12.3: S-5 · Reanudación de paquetes grandes — validación del offset persistido (NO BLOQUEANTE)

Como ingeniero del producto,
quiero validar experimentalmente la reanudación de un paquete de 7+ GB con reinicio del proceso,
so que el punto más frágil del MVP tenga evidencia y no suposición.

**Alcance mínimo:** descarga de 7+ GB interrumpida y reanudada varias veces, con reinicio del
proceso del cliente.

**Acceptance Criteria:**

**Given** una descarga interrumpida,
**When** se reanuda,
**Then** parte del **offset persistido**, no de cero
*(Criterio S-5a)*.

**Given** el reinicio del proceso del cliente,
**When** se reconcilian el archivo parcial y el registro de progreso,
**Then** lo hacen **sin divergencia**
*(Criterio S-5b)*.

**Given** N interrupciones,
**When** se completa la descarga,
**Then** el hash final **coincide**
*(Criterio S-5c)*.

**Given** el resultado del spike,
**When** se publica,
**Then** es una decisión de fase posterior: **no** cambia ningún artefacto del MVP,
**And** valida AD-11 cuyo mecanismo ya está fijado (AD-21, R-MVP-5)*.

### Story 12.4: S-4 · Rendimiento y recursos — medición en lugar de suposición (NO BLOQUEANTE)

Como organizador del evento,
quiero cifras reales de tasa de transferencia y consumo de recursos,
so que el producto no prometa lo que no ha medido.

**Alcance mínimo:** descarga de un paquete de varios GB con 2, 5, 10 y 20 clientes simultáneos;
medición de CPU, memoria y enlace.

**Acceptance Criteria:**

**Given** los escenarios de 2, 5, 10 y 20 clientes,
**When** se mide,
**Then** se mide la tasa de transferencia agregada por número de clientes
*(Criterio S-4a)*.

**Given** los mismos escenarios,
**When** se mide,
**Then** se mide el consumo de CPU y memoria del servidor y del cliente en cada escenario
*(Criterio S-4b)*.

**Given** una interrupción de la descarga,
**When** se reanuda,
**Then** se registra el tiempo de reanudación
*(Criterio S-4c)*.

**Given** la medición completa,
**When** se publica,
**Then** se emite un informe con los números y la configuración
*(Criterio S-4d)*.

**Given** el resultado del spike,
**When** se aplica,
**Then** fija los umbrales numéricos de NFR-2 y NFR-6 y los límites por cliente de AD-14,
**And** **no** introduce ninguna capacidad nueva en el MVP (AD-21, R-MVP-5)*.

### Story 12.5: S-3 · PoC P2P — decisión de fase posterior (NO BLOQUEANTE)

Como arquitecto del producto,
quiero saber si BitTorrent con tracker local es viable como mecanismo real,
so que la decisión de fase posterior tenga evidencia y no suposición.

**Alcance mínimo:** tracker interno en proceso; 3 *peers*; *seed* desde el servidor; intercambio por
fragmentos.

**Acceptance Criteria:**

**Given** el servidor como origen,
**When** un cliente completa la descarga,
**Then** el servidor hace *seed* y un cliente completa
*(Criterio S-3a)*.

**Given** tres *peers*,
**When** un segundo cliente recibe datos,
**Then** recibe **fragmentos de un *peer***, no solo del servidor
*(Criterio S-3b)*.

**Given** la transferencia por fragmentos,
**When** termina,
**Then** se verifican integridad y *hash* final
*(Criterio S-3c)*.

**Given** una interrupción,
**When** se reanuda,
**Then** la transferencia **se reanuda**
*(Criterio S-3d)*.

**Given** la configuración de la PoC,
**When** se inspecciona,
**Then** **ningún *peer* sale a Internet** (DHT y LPD verificados como deshabilitados)
*(Criterio S-3e)*.

**Given** el resultado del spike,
**When** se publica,
**Then** decide la **forma** de la segunda implementación de `ITransferEngine` sin cambiar la forma
del puerto ni los contratos de dominio, y decide el formato del mapa de fragmentos,
**And** el resultado **no** reescribe el MVP: si S-3 no se ejecuta o fracasa, el resultado es «el
MVP sigue por HTTP» y **ningún** artefacto del MVP cambia,
**And** la aparición de `BitTorrentTransferEngine`, del módulo `Tracking` o de `chunkMap` requiere
su **propia AD** y su **propio traceability** en la fase que lo ejecute, **nunca** por edición del
MVP
*(AD-21, AD-28, R-MVP-1, R-MVP-5)*.

## Epic 13: El guion de aceptación del MVP en LAN real

El MVP se cierra con un guion reproducible que encadena el flujo completo del PRD §9 en una LAN
real, con dos o más clientes, sin Internet y con el mismo paquete íntegro para todos. Es la
historia que hace verificables SM-1 y SM-5: sin ella, el backlog cerraría con todas las historias
«verdes» sin que nadie haya corrido nunca el flujo completo.

**FRs cubiertos:** FR-2..FR-11 (verificación conjunta).
**NFRs cubiertos:** NFR-1, NFR-9.
**UX-DRs cubiertos:** UX-DR-44 (los cuatro flujos, ejecutados de extremo a extremo).

### Story 13.1: Demostración reproducible de extremo a extremo en LAN real

Como responsable de la aceptación del MVP,
quiero ejecutar el guion completo del PRD §9 con dos o más clientes en una LAN real y sin Internet,
so que SM-1 y SM-5 queden verificados con evidencia reproducible.

**Acceptance Criteria:**

**Given** una LAN real aislada de Internet, un servidor y dos o más clientes compatibles,
**When** se ejecutan los diez pasos del guion del PRD §9,
**Then** queda evidencia reproducible de cada paso: (1) arranque del servidor; (2) conexión de los
clientes; (3) sincronización del catálogo; (4) visualización de un videojuego disponible; (5)
descarga del paquete desde el servidor; (6) validación de integridad y autenticidad; (7) instalación
portable; (8) detección de la instalación existente; (9) ejecución desde el cliente; y (10) visualización
del estado en el servidor,
**And** el mismo paquete íntegro se distribuye a todos los clientes sin acceso a Internet
*(FR-2..FR-11, NFR-1, SM-1, AR-21)*.

**Given** el guion de aceptación en ejecución,
**When** se recorre cada uno de los diez pasos,
**Then** cada paso incluye instrucciones reproducibles, estado observable y resultado esperado,
**And** SM-5 queda verificado para los diez pasos sin depender de conocimiento no documentado
*(FR-2..FR-11, SM-5)*.

**Given** dos o más clientes incompatibles o un servidor inaccesible durante la preparación,
**When** se ejecuta el guion de aceptación,
**Then** el flujo no se declara aprobado por evasión ni por omisión de pasos: el resultado registra el
paso fallido, su diagnóstico y la condición de reintento,
**And** la evidencia distingue SM-1 de SM-5 y no sustituye una ejecución completa por resultados
parciales
*(FR-3, FR-4, NFR-9, SM-1, SM-5)*.
