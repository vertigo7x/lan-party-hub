# Revisión — Rubric Walker (good-spine checklist)

- **Revisor:** rubric walker (checklist canónico del workflow `bmad-architecture`)
- **Objetivo:** `ARCHITECTURE-SPINE.md`
- **Fecha:** 2026-10-26
- **Modo:** headless, ejecución secuencial (sin subagentes disponibles)

## Veredicto

**ADECUADO CON RESERVAS.** La espina cubre el contrato completo que la PRD exige y cierra las tres restricciones abiertas (R3, R5, P2P) sin inventar. Cuatro hallazgos: dos **altos** de divergencia que un revisor adversarial construiría, dos **medios** de cobertura, ninguno bloqueante.

---

## Checklist punto por punto

### 1. «Fija los puntos de divergencia reales del nivel inferior y no deja ninguno fuera» — **ALTO (parcial)**

La espina fija bien los límites de módulo, los contratos de estado, el modelo de datos y la forma del contrato de API. Pero deja **dos contratos compartidos sin dueño explícito**, que son divergencias reales entre el servidor y el cliente:

- **H-1. La forma del error no está declarada como contrato compartido, solo como convención.**
  La convención de datos dice que toda respuesta de error usa `{ code, message, cause, nextStep }`, pero no dice **dónde vive ese tipo**. Si el servidor lo declara en `Shared` y el cliente lo redeclara en su propio namespace, los dos compilan, ambos cumplen la convención, y el cliente deserializa mal un error real. La `EXPERIENCE.md` exige además que el error tenga «nombre estable» y tres partes (qué falló, por qué, qué hacer ahora) — la convención lo dice en prosa, pero no lo convierte en tipo compartido ni en `AD`.

- **H-2. El vocabulario de la UI y el vocabulario del dominio no están enlazados.**
  `AD-10` dice que el vocabulario de estados «coincide con las insignias de estado de `EXPERIENCE.md`», pero no dice **cómo se garantiza** ni **quién detecta la deriva**. Un cliente construido de forma independiente puede tener `DownloadState.Downloading` y la insignia `Descargando` sin que ninguna regla mechanizable los una. Peor: la UX define `status-badge` con estados que **no** son estados de máquina — `sincronizando`, `disponible`, `desactualizado` — que no tienen ningún `AD` que los gobierne.

  *Por qué es divergencia real:* dos unidades un nivel abajo —el servidor y el cliente— pueden cada una tener su enumeración y su propia tabla de traducción a texto, y divergir en el tercer estado.

  *Corrección:* fijar un `AD` de mapeo de estados → microcopia, en `Shared`, con la regla de que la traducción de estado a texto de UI es una **tabla única** y que los estados que la UX usa pero el dominio no tiene (disponible, desactualizado, sincronizando) son **estados de vista derivados**, no estados de máquina, con su regla de derivación.

### 2. «Cada Regla de un AD es exigible y realmente previene su divergencia declarada» — **ALTO (parcial)**

La mayoría de las reglas son exigibles. Dos no lo son:

- **H-3. `AD-4` y `AD-13` dejan el P2P en un estado que dos unidades pueden leer de dos maneras.**
  `AD-4` dice que la configuración «selecciona la implementación; ninguna otra parte del sistema sabe cuál está activa». `AD-13` dice que el tracker existe «si la PoC valida BitTorrent». Pero `AD-21` y la tabla de spikes dicen que el spike S-3 «no bloquea el MVP». Resultado: en el momento de construir el MVP, ¿existe `BitTorrentTransferEngine`? ¿Existe el módulo `Tracking` en el servidor? El diagrama de módulos lo lista sin condición, y el diagrama de despliegue lo lista como `tracker interno`. Dos unidades pueden una **construir la abstracción con dos implementaciones** (una con `BitTorrentTransferEngine` stub que devuelve «no disponible») y otra **construirla con una sola implementación**. Divergen en la forma del puerto.

  *Corrección:* fijar que la abstracción se entrega con **una** implementación en el MVP y que la segunda se añade **solo** al cerrarse S-3, con la regla de que el puerto no cambia y la configuración no puede seleccionar una implementación ausente.

- **H-4. `AD-9` es exigible pero su alcance es ambiguo.**
  Dice «toda escritura de instalación se resuelve contra la raíz del directorio de instalación configurado». No dice si la **descarga** también está confinada, ni si el directorio de caché de descargas está dentro o fuera de la raíz. La `EXPERIENCE.md` distingue «directorio de instalación» (configurable) del progreso de descarga persistido (`AD-11`, tabla de fragmentos). Un cliente puede escribir el `.part` del paquete en la raíz de instalación (y ensuciar la instalación) o en `%TEMP%` (y perderlo al reiniciar, rompiendo SM-2). La espina no lo dice.

  *Corrección:* enumerar los directorios con nombre y propósito: raíz de instalación (confinado por `AD-9`), directorio de descargas parciales (ruta fija, **no** dentro de la raíz de instalación), directorio de caché de manifiestos, y los registros de base de datos.

### 3. «Nada bajo Deferred podría dejar a dos unidades divergir» — **ALTO (cumple)**

Las once entradas de Deferred son genuinamente pospuestas y ninguna es un contrato compartido no decidido. Verificado una por una: umbrales de rendimiento (medibles, no forma), tabla de versiones (mecanismo fijado), redes segmentadas (comportamiento de red), versión de MonoTorrent (depende de S-3), schemaVersion del manifiesto (mecanismo de rechazo fijado), Installer/External (fuera de MVP por producto), modo gestionado (fuera de MVP), diferenciales (fuera de MVP), TLS (modelo operativo), multiplataforma (fuera de MVP), conexión a servidores de juego (puerto preparado), multi-tenancy (modelo operativo), delta de catálogo (escala), chunking multipart (escala). **Correcto.**

### 4. «Tecnología nombrada verificada como actual» — **CUMPLE**

`.NET 10.0.13 LTS` (soporte hasta 2028-11-14), `ASP.NET Core 10.0.13`, `Microsoft.Data.Sqlite 10.0.13`, `Avalonia 12.1.4` (12.0.0 publicado 2026-04-07 con cambios incompatibles vs 11.x — la espina lo menciona, lo cual es la decisión correcta: Avalonia 11 sería una elección obsoleta), `MonoTorrent` **sin fijar** (correcto: hay alphas 3.9.x que no son fijables), `SQLite 3.x`, `xUnit 3.x`. Verificado en la web el 2026-10-26. **Correcto.**

### 5. «Ratifica en lugar de contradecir un codebase brownfield» — **NO APLICA**

Proyecto greenfield; no hay código. La espina no ratifica nada que no exista. **Correcto.**

### 6. «Si una spec condujo la ejecución, cubre sus capacidades» — **ALTO (cumple con hueco menor)**

Verificado contra `prd.md` §4: FR-1 a FR-11 **todos** presentes en el mapa de capacidades. NFR-1 a NFR-10: NFR-2 y NFR-6 **conservados como abiertos a benchmark** (correcto, no inventados). SM-1 a SM-5 y SM-C1/SM-C2: referenciados en AD-2, AD-11, AD-14, AD-16. UJ-1, UJ-2, UJ-3: cubiertos por el flujo de módulos. R3, R5, P2P: cerrados. OQ-4: honestamente abierto.

**Hueco menor:** `AD-16` lista 13 casos E2E de `docs/plan.md` §10, pero **falta «Intento de extracción fuera del directorio autorizado» como caso de integración del confinement** — está en la lista, sí, pero clasificado como E2E cuando es un caso unitario/integración barato; no es un fallo, es precisión de coste.

### 7. «Si una espina padre se hereda, ningún AD nuevo la debilita» — **NO APLICA**

Sin espina padre. **Correcto.**

### 8. «Cada dimensión que la altitud posee está decidida, diferida o es pregunta abierta» — **ALTO (cumple)**

Dimensiones del nivel `feature` revisadas una por una:

| Dimensión | Estado | Veredicto |
| --- | --- | --- |
| Paradigma y límites | AD-1 | Decidida |
| Contratos API y eventos | AD-5, AD-6 + tabla REST + tabla SignalR | Decidida |
| Modelo de dominio y persistencia | AD-7, AD-11 + ERD | Decidida |
| Modelo de paquetes / manifiestos | AD-2, AD-7, AD-8, AD-9 | Decidida |
| Transferencia y P2P | AD-4, AD-13, AD-14, AD-20, AD-21 | Decidida |
| Confianza / firma | AD-2, AD-8 | Decidida |
| Descubrimiento | AD-3 | Decidida |
| Estados y transiciones | AD-10 + 2 diagramas | Decidida |
| Despliegue y entornos | AD-17 + diagrama de topología | Decidida |
| Observabilidad | AD-15 | Decidida |
| Estrategia de pruebas | AD-16 + tabla de spikes | Decidida |
| Seguridad | AD-2, AD-8, AD-9, AD-12 | Decidida |
| Errores y diagnóstico | AD-15 + convención | Decidida |
| Concurrencia y límites | AD-14 + convención | Decidida |
| Configuración | AD-12, AD-17 + convención | Decidida |
| Migraciones | AD-11 | Decidida |
| Versionado de contratos | AD-5, AD-6 | Decidida |
| NFR-2 / NFR-6 (cifras) | Deferred + S-4 | Diferida con condición |
| Soporte multiplataforma | Deferred | Diferida |
| **Modo gestionado de sesiones** | Deferred | Diferida |

**Ninguna dimensión en silencio.** Este es el punto fuerte de la espina: el sobre operativo (despliegue, entornos, operación) está cubierto, que es donde fallan las espinas de dominio. **Correcto.**

---

## Hallazgos detallados

### H-1 — La forma del error no es un tipo compartido (ALTO)

- **Ubicación:** `Consistency Conventions` → fila `Data & formats`; AD-15.
- **Evidencia:** la convención declara `{ code, message, cause, nextStep }` en prosa. No hay AD, ni entrada en el mapa de capacidades, ni fila que diga dónde se declara ese tipo. `AD-18` dice que `Shared` contiene «DTOs de API» — pero no nombra el error como DTO de API.
- **Divergencia que previene:** servidor y cliente declaran el error por separado; el cliente no deserializa `cause`/`nextStep`; la UX recibe un error sin causa ni siguiente paso → viola `EXPERIENCE.md` *Voz y tono*.
- **Corrección propuesta:** añadir a `AD-18` la enumeración explícita de lo que `Shared` contiene, incluyendo el tipo de error como contrato; y a `AD-15` la regla de que ese tipo es **el único** tipo de error de API y que `cause`/`nextStep` son obligatorios y no opcionales.

### H-2 — El vocabulario de estados de la UI no está enlazado al dominio (ALTO)

- **Ubicación:** AD-10; `Consistency Conventions` → filas `Data & formats` y `State & cross-cutting`.
- **Evidencia:** `EXPERIENCE.md` define `status-badge` con los estados «sincronizando, disponible, descargando, pausado, validado, instalado, instalado con versión desactualizada, en ejecución, error». `AD-10` define `DownloadState` e `InstallationState` como enumeraciones **cerradas**. Los conjuntos **no coinciden**: «sincronizando», «disponible», «instalado con versión desactualizada» y «error» no son estados de máquina de descarga ni de instalación en la espina.
- **Divergencia que previene:** una tabla de traducción estado→texto duplicada en cliente y servidor, con deriva; y un badge «error» que no corresponde a ningún estado terminal del dominio.
- **Corrección propuesta:** añadir un AD de **mapeo de estados**: (a) el texto de UI de cada estado vive en **una sola tabla** compartida; (b) los badges que la UX define y el dominio no («sincronizando», «disponible», «desactualizado») son **estados de vista derivados** y su regla de derivación se declara (p. ej. «desactualizado» = instalación validada ∧ versión del catálogo mayor que la instalada); (c) «error» se mapea al conjunto de estados terminales de fallo, no a un estado propio.

### H-3 — La condición del módulo `Tracking` y de la segunda implementación del puerto no es inequívoca (ALTO)

- **Ubicación:** AD-4, AD-13, AD-21; diagrama de módulos del servidor; diagrama de despliegue.
- **Evidencia:** AD-13 «si la PoC valida BitTorrent»; AD-21 «el intercambio P2P no se expone como capacidad del MVP hasta que su spike valide…»; pero el diagrama de módulos lista `Tracking` sin marca de condicional y el de despliegue lista «tracker interno (mismo proceso, mismo puerto)» sin condicional; AD-4 nombra `BitTorrentTransferEngine` como implementación del puerto sin decir «a partir de S-3».
- **Divergencia que previene:** una unidad construye `ITransferEngine` con **dos** implementaciones y un selector de configuración; otra construye el puerto con **una** y un `NotSupportedException`. Los dominios de configuración divergen.
- **Corrección propuesta:** en `AD-4`, «el puerto se entrega en el MVP con **una** implementación (`HttpRangeTransferEngine`); la segunda se añade **solo** al cerrarse S-3 y sin cambiar el puerto»; en `AD-13`, marcar `Tracking` como **módulo condicional ausente hasta que S-3 cierre**, y en los diagramas, marcarlo con `(condicional: S-3)`.

### H-4 — Los confines de escritura no cubren el directorio de descargas parciales (MEDIO)

- **Ubicación:** AD-9, AD-11.
- **Evidencia:** AD-9 confina «toda escritura de instalación» a la raíz de instalación. AD-11 persiste el «registro de fragmentos/offset» y lo reconcilia «contra el archivo parcial en disco», pero no dice **dónde** vive ese archivo parcial.
- **Divergencia que previene:** un cliente escribe el `.part` dentro de la raíz de instalación (contamina la instalación validada y rompe la validación de instalación) y otro lo escribe en `%TEMP%` (se pierde al reiniciar → SM-2 roto, que es R-02, el riesgo que el propio PRD marca como el más frágil).
- **Corrección propuesta:** añadir a `AD-9` (o a la convención de datos) una enumeración de directorios con nombre y propósito: raíz de instalación (confinada), directorio de descargas parciales (**fuera** de la raíz de instalación, ruta fija y persistente), caché de manifiestos, base de datos del cliente.

### H-5 — El catálogo se sincroniza por snapshot completo sin regla de conflicto (MEDIO)

- **Ubicación:** AD-5, AD-10, tabla REST; `Deferred` (delta de catálogo).
- **Evidencia:** AD-10 establece que «el cliente es la única fuente de verdad del estado local de una operación», y `Deferred` aplaza el sync por delta. La tabla REST expone `GET /api/v1/catalog` con `catalogVersion`. Pero no hay regla de **resolución de conflicto** cuando el servidor cambia el catálogo mientras el cliente tiene una instalación en curso o una descarga activa.
- **Divergencia que previene:** un cliente aplica un catálogo nuevo que **retira** el paquete que está descargando; otro congela la vista. Divergen en si la operación en curso se cancela, se deja terminar o falla.
- **Corrección propuesta:** un AD breve de **consistencia del catálogo**: el cliente aplica un snapshot nuevo del catálogo **sin cancelar operaciones en curso**; una operación en curso que referencia un paquete retirado **termina** (no se cancela) y se marca el resultado como «paquete retirado del catálogo»; la instalación existente de un juego retirado **no se borra**.

### H-6 — La clasificación del caso «extracción fuera del directorio» (BAJA)

- **Ubicación:** AD-16 (lista E2E).
- **Evidencia:** el caso de `docs/plan.md` §10 «Intento de extracción fuera del directorio autorizado» está listado como caso E2E. `AD-9` es puramente una regla de rutas, verificable en unitarias.
- **Corrección propuesta:** moverlo a la lista de unitarias/integración de `AD-16` y dejar en E2E solo los casos que necesitan LAN real.

---

## Resumen

| Severidad | Cantidad | IDs |
| --- | --- | --- |
| Crítica | 0 | — |
| Alta | 3 | H-1, H-2, H-3 |
| Media | 2 | H-4, H-5 |
| Baja | 1 | H-6 |

Todos los hallazgos son **aplicables sin decisión de usuario** (no hay fork de producto, solo cierres de contrato). Ninguno exige información que la espina no pueda derivar de sus propias fuentes.

---

## Bloque de resolución — H-1..H-6 (cierre residual de arquitectura)

- **Fecha del cierre:** 2026-10-26
- **Método:** cada hallazgo se reverificó contra el **texto de `ARCHITECTURE-SPINE.md`**, no contra este informe. Una línea citada significa que la regla existe en la espina; la cita es la evidencia del cierre.
- **Verificación mecánica:** `lint_spine.py` sobre la espina actual → `ok: true`, `total_findings: 0`, sin hallazgos por categoría ni severidad.

### Estado por hallazgo

| ID | Severidad | Estado | Cierre en la espina | Evidencia (línea de la espina) |
| --- | --- | --- | --- | --- |
| **H-1** | ALTA | **CERRADO** | `ApiError` pasa de convención en prosa a **tipo único del sistema declarado en `Shared`**, con `cause` y `nextStep` obligatorios, catálogo único `ErrorCatalog` y enumeración explícita del contenido de `Shared`. | AD-15 L194–197; AD-18 L220, L221, L229; convención `Data & formats` L325 |
| **H-2** | ALTA | **CERRADO** | `StateDisplayMap` pasa a **tabla única y exhaustiva en `Shared`**; los badges de UX que no son estados de máquina pasan a **estados de vista derivados** con regla de derivación fija; `error` se declara proyección de estados terminales de fallo, no estado propio. | AD-10 L155–161; AD-18 L224, L225; AD-25 L300–307; convención `State & cross-cutting` L327 |
| **H-3** | ALTA | **CERRADO** | El puerto `ITransferEngine` se entrega con **una sola implementación** (`HttpRangeTransferEngine`); el módulo `Tracking` queda **ausente hasta que S-3 cierre** (ni módulo, ni endpoint, ni proceso, ni registro de swarm, ni puerto de configuración); la segunda implementación no cambia la forma del puerto. | AD-4 L103–106; AD-13 L179–181; AD-21 L254; diagramas L387, L401, L407 y topología L683; convención `Configuración y red` L330 |
| **H-4** | MEDIA | **CERRADO** | Se declara el **layout en disco del cliente con directorio y propósito nombrados**: raíz de instalación confinada, `downloads/` de ruta fija y persistente **fuera** de la raíz, `manifests/` de caché y `state.sqlite`. Los `.part` viven **solo** en `downloads/`, nunca en `%TEMP%`. | AD-9 L143–148; convención `Configuración y red` L330 |
| **H-5** | MEDIA | **CERRADO** | AD-26 fija la regla de **consistencia del catálogo frente a operaciones en curso**: un snapshot nuevo **no cancela** operaciones en curso; una operación cuyo paquete fue retirado **termina** con resultado explícito `Paquete retirado del catálogo` (distinto del fallo); la instalación existente **no se borra**; `catalogVersion` monótona. | AD-26 L309–318; tabla `Deferred` (delta) L740 |
| **H-6** | BAJA | **CERRADO** | La clasificación de `AD-16` mueve «extracción fuera del directorio autorizado» a **unitarias/integración** y deja en E2E en LAN real solo lo que necesita LAN. La clasificación no elimina ningún caso de la checklist: fija el **nivel mínimo** de detección. | AD-16 L203 |

### Pendientes de este informe

**Ninguno.** Los seis hallazgos H-1..H-6 están cerrados y verificados contra la espina.

### Notas de la verificación

- **H-1** era el hallazgo con más superficie: la corrección exigía dos cosas a la vez (dónde vive el tipo **y** que `cause`/`nextStep` no sean opcionales). Ambas están, y `AD-18` cierra además la puerta a la duplicación con la regla «un concepto, un tipo, un lugar».
- **H-3** era el hallazgo con más riesgo de cierre parcial: `AD-4` y `AD-13` ya declaraban la condición, pero los **diagramas** la contradecían (listaban `Tracking` sin marca de condicional). Los tres diagramas (módulos del servidor, dependencias, topología de despliegue) llevan ahora la marca de ausencia, y la nota al pie del diagrama de módulos lo dice explícitamente: `Tracking` aparece **solo** para dejar constancia de que no forma parte del MVP.
- **H-5** interactúa con `Deferred`: la sincronización por delta sigue aplazada, pero el mecanismo de aplicación de un snapshot completo ya no admite dos lecturas, así que el aplazamiento ya no deja dos unidades divergir.
- La **capa de verificación** no encontró ningún hallazgo del checklist que quedara abierto tras el cierre; los cierres de `H-1` y `H-2` se reforzaron mutuamente (`StateDisplayMap` y `ErrorCatalog` quedan como las dos tablas únicas de `Shared`, declaradas en `AD-25`).

### Referencias cruzadas

- Los cierres adversariales **A-1..A-12** están en `reviews/review-adversarial-divergence.md`.
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

- Los cierres del checklist **H-1..H-6** están en el bloque de resolución anterior de este mismo informe.
- Los cierres adversariales **A-1..A-12** están en `reviews/review-adversarial-divergence.md`.
- Las aclaraciones de semilla **T-1..T-5** están en `reviews/review-technology-currency.md`.
- Las decisiones **AD-27, AD-28, AD-29** y el refinamiento de **AD-21** quedaron registrados en `.memlog.md`.
