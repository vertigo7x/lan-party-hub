# Revisión editorial — Voz, consistencia y trazabilidad del backlog de épicas e historias

- **Lente:** editorial (voz de la interfaz del documento, consistencia interna, trazabilidad de las
  citas y microcopia).
- **Objetivo:** `epics.md`
- **Fecha:** 2026-10-26
- **Modo:** headless, ejecución secuencial (sin subagentes disponibles)
- **Base:** `bde708d97766dfc607528abb9d3a28bc82487f0e`

## Veredicto

**ADECUADO CON RESERVAS.** El documento está bien construido: 46 historias con la forma
`Como / quiero / so que`, 218 AC en forma `Given / When / Then`, marcadores de énfasis balanceados
(2478, par), frontmatter íntegro y cero placeholders. LaVoice de las historias es **coherente** con el
contrato de UX: segunda persona, sin emojis, sin superlativos.

Sin embargo, hay **cinco defectos editoriales reales** y **dos inconsistencias de trazabilidad**.
Ninguno es bloqueante; todos se cierran con una corrección local, sin decisión de producto.

---

## Checklist editorial punto por punto

### 1. Forma de las historias (`Como / quiero / so que`) — **CUMPLE**

Las 46 historias tienen las tres partes. Ninguna historia abre con una descripción técnica en lugar
de la necesidad de una persona. La persona declarada es la correcta por historia (Marta en la
épica 2, Ana en las épicas 3-8 y 10, Luis en la 9, «arquitecto del producto» e «ingeniero de
seguridad» en las fundacionales y de seguridad). La Skill exige que cada historia sea completable
por un agente de desarrollo con su valor declarado: la forma lo cumple.

### 2. Forma de los AC (`Given / When / Then / And`) — **CUMPLE**

218 de 218. Cada AC es una frase condicional completa, con precondición, acción y resultado
esperado. Ningún AC es una frase suelta ni una lista de tareas.

### 3. Microcopia literal — **CUMPLE CON RESERVA (E-1)**

Todas las cadenas literales que el documento reproduce («7,6 GB», «42 %», «Paquete dañado: el hash
no coincide · 7,6 GB», «Este cliente (1.2) no es compatible con el servidor (1.4). Actualiza el
cliente.», «Se reparará la instalación; la descarga no se repite», «Se borrarán los 3,2 GB
descargados») coinciden con el contrato de UX. Las prohibiciones de formato (nunca «7,60 GB»,
«7 GB», «42,3 %», «42/100») están enunciadas en la AC correcta.

**Reserva E-1**: ver abajo.

### 4. Voz de las historias — **CUMPLE**

Segunda persona, verbo en primera, sin emojis, sin superlativos, sin gamificación, sin coletilla
comercial. No hay interrogación por culpa salvo en la acción que destruye trabajo no recuperable
(cancelar una descarga), que nombra lo que se pierde.

### 5. Consistencia interna de la voz (actor de los AC) — **FALLA (E-1)**

Ver E-1.

### 6. Ortografía y tipografía — **FALLA (E-2)**

Ver E-2.

### 7. Encabezados y estructura de plantilla — **CUMPLE**

Presentes y en el orden de la plantilla: `Overview`, `Requirements Inventory` (con
`Functional Requirements`, `NonFunctional Requirements`, `Additional Requirements`, `UX Design
Requirements`, `FR Coverage Map`), `Epic List`, y luego `## Epic N` con sus historias. No quedan
secciones de plantilla sin sustituir: el comentario `<!-- Repeat for each epic in epics_list
(N = 1, 2, 3...) -->` es el marcador de la plantilla y queda fuera del contenido.

### 8. Placeholders residuales — **CUMPLE**

Cero `{{...}}`, cero `TODO`/`TBD`/`FIXME`, cero `<PLACEHOLDER>`. Los corchetes angulares que
aparecen (`<n> %`, `<a> GB de <t> GB`, `<causa>`, `<consulta>`) son **parámetros de plantilla de
microcopia** definidos por el contrato de UX, no placeholders sin resolver.

### 9. Codificación — **CUMPLE**

UTF-8 válido. Cero caracteres CJK, cero U+FFFD. La tilde, las comillas angulares «» y el em dash se
renderizan correctamente.

### 10. Trazabilidad de las citas (AR / AD / UX-DR / R-MVP) — **FALLA (E-3, E-4)**

Ver E-3 y E-4.

### 11. Duplicación de la lista de épicas — **CUMPLE (nota menor)**

`## Epic List` (resumen de las 12 épicas) y luego los `## Epic N` con el detalle. Es la forma que
exige la plantilla: la lista como índice y el detalle después. **Sin embargo**, cada `## Epic N`
**repite literalmente el párrafo de objetivo** de su entrada en `## Epic List`. La repetición es
exacta (12 párrafos duplicados). No es un defecto —el lector puede leer el índice o el detalle—,
pero es ruido: la lista podría ser un índice de una línea por épica.

### 12. Coherencia con la nomenclatura del contrato — **CUMPLE**

Los nombres de tipo (`ApiError`, `ErrorCatalog`, `DownloadState`, `InstallationState`,
`StateDisplayMap`, `CatalogViewState`, `ClientStateChanged`, `DownloadProgressUpdated`,
`InstallationProgressUpdated`, `ClientErrorReported`, `CatalogChanged`, `ClientRegistrationView`,
`TrustAnchor`, `PackageDescriptor`, `InstallationManifest`, `VersionInfo`), de endpoint
(`/api/v1/...`), de cabecera (`X-Client-Version`, `X-Protocol-Version`) y de motor
(`HttpRangeTransferEngine`, `ITransferEngine`) coinciden con la espina de arquitectura.

---

## Hallazgos detallados

### E-1 — Los AC usan «el usuario» como actor, y una AC prohíbe explícitamente esa forma — **MEDIA**

- **Ubicación:** 9 AC de `epics.md`; la AC que lo prohíbe es Story 11.4 AC2.
- **Evidencia:**
  - **Story 11.4 AC2** (L2178) establece la regla de voz: «usa segunda persona y verbo en primera
    («Pulsa», «Elige», «Indica», «Comprueba»), **nunca «el usuario»**» —*(UX-DR-6, UX-DR-22)*.
  - Y sin embargo 9 AC usan «el usuario» como sujeto del `When`:
    - Story 1.3 AC4: «**When** el usuario cambia el directorio en Configuración»
    - Story 4.2 AC2: «**When** el usuario confirma con `Enter`»
    - Story 4.4 AC2: «**When** el usuario confirma la comparación del fingerprint recibido contra el
      esperado»
    - Story 4.4 AC5: «**And** el cliente solo persiste el ancla cuando el usuario confirma que
      **coinciden**»
    - Story 8.1 AC4: «**When** el usuario teclea»
    - Story 9.4 AC1: «**When** el usuario la abre»
    - Story 10.3 AC4: «**When** el usuario decide reintentar»
    - Story 11.6 AC5: «**When** el usuario navega a otra superficie»
    - Story 11.8 AC3: «**When** el usuario cambia el escalado de texto o el tamaño de ventana»
- **Por qué importa:** la AC que fija la voz es **normativa para la interfaz del producto**, pero
  aquí la regla se incumple **en el propio documento que la declara**. El resultado practical es que
  el backlog transmite dos voces: la que exige la UX (segunda persona) y la que usa el backlog
  (tercera persona impersonal). Un implementador que tome el backlog como referencia de estilo
  escribe `«El usuario no puede jugar»` en vez de `«No se puede jugar: <causa>.»` — que es
  precisamente la microcopia que UX-DR-18 y UX-DR-35 exigen.
- **Corrección propuesta:** reescribir las 9 ocurrencias con la persona nombrada por la historia
  (Ana, Marta, Luis) o con la forma impersonal que la UX usa en el propio producto
  («Al cambiar el directorio en Configuración», «Al confirmar con `Enter`», «Al teclear»,
  «Al abrirla», «Al decidir reintentar», «Al navegar a otra superficie», «Al cambiar el escalado»).
  La forma impersonal es la que ya usa el resto del backlog y es la coherente con UX-DR-6.

### E-2 — «videogameos»: error ortográfico en el requisito FR-2 y en un AC — **BAJA**

- **Ubicación:** FR-2 (L62) y Story 3.2 AC2 (L1083).
- **Evidencia:** FR-2 dice «categorizar los **videogameos** disponibles» y Story 3.2 AC2 dice «los
  resultados muestran únicamente los **videogameos** de esa categoría». La forma correcta en
  español es **«videojuegos»**, y es la que el propio documento usa en las otras 22 apariciones
  (`videojuego` ×16, `videojuegos` ×6).
- **Por qué importa:** FR-2 es la **definición normativa** del requisito: es la línea que se copia
  al backlog, al tracker y a las historias de otras fases. Un error en la fuente se propaga a
  todos los derivados, y en un documento cuya función es ser la fuente de verdad del alcance, la
  fuente no puede tener erratas.
- **Corrección propuesta:** «videojuegos» en ambos sitios. Verificación de barrido: `videojuego` (16),
  `videojuegos` (6), `videogameos` (2 → 0), `videjuegos` (0), `juegoes` (0).

### E-3 — AR-21 y AR-25 se citan **solo** en su definición; ninguna AC las referencia — **MEDIA**

- **Ubicación:** `### Additional Requirements` → AR-21 (L244), AR-25 (L267).
- **Evidencia:** verificación mecánica de menciones: **AR-21 = 1**, **AR-25 = 1**. En ambos casos la
  única mención es la definición. Ninguna de las 218 AC cita AR-21 ni AR-25. Lo mismo ocurre, en
  menor grado, con **AR-19** (2 menciones: definición + Story 1.2 AC3) y **AR-17** (2: definición +
  Story 1.2 AC1) — estas dos sí tienen al menos un AC que las respalda.
- **Por qué importa:** AR-21 fija la estrategia de pruebas en tres niveles y, sobre todo, declara
  que el caso «cliente compartiendo fragmentos con otros» **pertenece a la PoC P2P y sale del
  conjunto obligatorio del MVP**. AR-25 fija el carácter bloqueante/no bloqueante de cada spike como
  **valor normativo**. Son las dos únicas exigences del inventario que no tienen ninguna historia que
  las verifique: quedan como declaración sin cobertura, y el mapa de cobertura no lo declara
  porque no hay mapa para las AR.
- **Corrección propuesta:** (a) añadir un AC en Story 1.1 con la clasificación de niveles de prueba
  y la exclusión explícita del caso P2P del conjunto obligatorio del MVP; (b) añadir un AC en
  Story 12.1 con el carácter de S-2 como regla exigible. (c) Añadir una fila de mapa para AR en
  `### FR Coverage Map` o una nota explícita de que las AR se trazan por cita dentro de las AC.

### E-4 — La tabla `R-MVP-*` no tiene columna de trazabilidad hacia la historia que la aplica — **BAJA**

- **Ubicación:** `### Restricciones heredadas que gobiernan todo el desglose` (tabla de 9 filas).
- **Evidencia:** la tabla declara 9 restricciones con su origen arquitectónico (`AD-4, AD-13,
  AD-28`…), pero **no dice qué historia las aplica**. La información existe dispersa en las AC
  (R-MVP-1 en Story 5.1/5.3/12.5, R-MVP-2 en Story 4.3, R-MVP-3 en Story 4.4/6.1/7.1, R-MVP-4 en
  Story 7.3/10.2, R-MVP-5 en Story 12.1..12.5, R-MVP-6 en Story 5.1, R-MVP-7 en Story 1.1/3.3,
  R-MVP-8 en Story 3.3/6.2/11.4, R-MVP-9 en Story 9.1), pero no es **legible de un vistazo**: para
  auditar que una restricción se aplica, hay que buscar cada identificador en las 2.570 líneas.
- **Por qué importa:** las R-MVP son la capa de restricciones **heredadas del Checkpoint D** y la
  que impide que el backlog se desvíe hacia P2P, autenticación o modos de instalación no previstos.
  Un lector que audita la conformidad del backlog tiene que reconstruir esa columna a mano.
- **Corrección propuesta:** añadir una tercera columna a la tabla `R-MVP-*` con las historias que la
  aplican. Es una mejora de legibilidad, no un hueco de cobertura: la información ya está en el
  documento y está verificada.

### E-5 — El objetivo de cada épica está duplicado literalmente entre `## Epic List` y `## Epic N` — **BAJA**

- **Ubicación:** `## Epic List` (12 párrafos de objetivo) y cada `## Epic N` (los mismos 12
  párrafos repetidos).
- **Evidencia:** los 12 textos son idénticos palabra por palabra.
- **Por qué importa:** es ruido, no error. La plantilla pide la lista y luego el detalle; el detalle
  puede asumir el objetivo ya declarado, o la lista puede ser un índice de una línea. El duplicado
  aumenta la superficie de mantenimiento sin añadir información: si el objetivo de una épica cambia,
  hay que editarlo en dos sitios y basta editar uno para que el documento se contradiga.
- **Corrección propuesta:** convertir `## Epic List` en un índice de una línea por épica (título +
  una frase de resultado), dejando el párrafo de objetivo completo solo en `## Epic N`. Alternativa
  equivalente: conservar el duplicado y aceptarlo, por coherencia con las fases anteriores.

---

## Resumen

| Severidad | Cantidad | IDs |
| --- | --- | --- |
| Crítica | 0 | — |
| Alta | 0 | — |
| Media | 2 | E-1, E-3 |
| Baja | 3 | E-2, E-4, E-5 |

### Verificaciones mecánicas ejecutadas

| Verificación | Resultado |
| --- | --- |
| Historias con forma `Como / quiero / so que` | PASS — 46/46 en las tres partes |
| Historias con `**Acceptance Criteria:**` | PASS — 46/46 |
| AC `**Given**` / `**When**` / `**Then**` | PASS — 218/218 en las tres |
| Marcadores `**` balanceados | PASS — 2478 (par) |
| Placeholders `{{...}}` | PASS — 0 |
| Placeholders `TODO`/`TBD`/`FIXME`/`XXX` | PASS — 0 |
| Placeholders `<MAYUSCULAS>` | PASS — 0 (los `<n> %` / `<causa>` son parámetros de microcopia definidos) |
| Caracteres CJK | PASS — 0 |
| Caracteres U+FFFD | PASS — 0 |
| Secciones de plantilla presentes y en orden | PASS |
| Frontmatter con `status`, `stepsCompleted`, `inputDocuments` | PASS |
| Coherencia de nomenclatura con la espina | PASS |
| Ocurrencias de «el usuario» frente a la AC que lo prohíbe | **FALLA** — 9 (E-1) |
| `videogameos` | **FALLA** — 2 (E-2) |
| `AR-21` / `AR-25` citadas por alguna AC | **FALLA** — 0 cada una (E-3) |
| Líneas de más de 100 caracteres | 28 (líneas de tabla y de encabezado de AC con formato `**Then** ... *(refs)*`) — **dentro de norma**: el documento usa ancho de 100 y las excedencias son de tabla o de una AC con cita larga |
