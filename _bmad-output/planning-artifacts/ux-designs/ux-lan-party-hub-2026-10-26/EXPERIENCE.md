---
name: LAN Party Hub
status: final
description: "Contrato de experiencia de LAN Party Hub: arquitectura de información, comportamiento, estados, interacción, accesibilidad y flujos del cliente de escritorio y del servidor del evento."
sources:
  - {planning_artifacts}/prds/prd-lan-party-hub-2026-10-26/prd.md
  - {planning_artifacts}/briefs/brief-lan-party-hub-2026-10-26/brief.md
  - {project-root}/docs/plan.md
updated: 2026-10-26
checkpoint: "Checkpoint C"
checkpoint_status: approved
checkpoint_approved: 2026-10-26
next_checkpoint: "Checkpoint D — validación de arquitectura y riesgos (Fase D)"
---

# LAN Party Hub — Espina de experiencia

> Contrato par de `DESIGN.md`. `DESIGN.md` es la identidad visual (cómo se ve); esta espina es la
> experiencia (cómo funciona). Ambas son spines de contrato par: gana la espina ante conflicto con
> cualquier mock, wireframe o importación.

## Fundación

**Factor de forma.** Escritorio. Dos aplicaciones distintas con roles distintos: **Servidor** (una
instancia por evento, administrada por la organización) y **Cliente** (una instancia por equipo
participante). Entorno inicial Windows 10/11 (PRD NFR-4); la evolución multiplataforma se conserva
como posibilidad, no como alcance del MVP.

**Sistema de UI.** Ninguno heredado. La UX define su propia capa de componentes sobre los tokens
de `DESIGN.md`; Avalonia es la hipótesis de stack del plan maestro, no una decisión de Fase D. El
comportamiento se define a nivel de escritorio, sin depender de un kit concreto.

**Superficie.** Una superficie por aplicación, sin multi-superficie y sin breakpoints responsive.
El cliente y el servidor son ventanas redimensionables; el comportamiento de redimensionado se
define en *Escritorio y plataforma*.

**Idioma de la interfaz.** Español. Los identificadores de código, tokens, nombres de componente y
contratos técnicos van en inglés (PRD §14, convención del plan maestro).

**Modelo de acceso.** LAN administrada con ejecutable distribuido por la organización. Todos los
clientes que ejecutan el producto son válidos: **no hay autenticación ni autorización de clientes**.
La única verificación de acceso al protocolo es la **compatibilidad de versión** (FR-4). La
interfaz no muestra credenciales, no pide inicio de sesión y no tiene pantalla dePermissions.

**Contrato de estado.** Regla de oro del producto, enunciada en `DESIGN.md.Brand & Style`: el
jugador nunca debe tener que preguntar en qué estado está una operación. Barra de progreso, etiqueta
de estado y registro de errores son parte del contrato visual, no adornos. Esta espina la convierte
en contrato de comportamiento con reglas verificables por superficie.

**Fuera de alcance de este documento.** El mecanismo concreto de descubrimiento (R3/OQ-2), la raíz
de confianza y el formato de firma (R5/OQ-1) y el intercambio P2P (OQ-3) **no se deciden aquí**.
La UX diseña el comportamiento observable de esos casos y marca cada límite.

## Arquitectura de información

### Cliente

| Superficie | Se alcanza desde | Propósito |
|---|---|---|
| Conexión | Apertura de la app | Descubrir el servidor o introducir su dirección; comprobar compatibilidad de versión |
| Catálogo | Navegación lateral / catálogo | Explorar juegos sincronizados; buscar, filtrar y categorizar |
| Ficha de videojuego | Tarjeta del catálogo | Datos completos del juego; punto de partida de descarga e instalación |
| Descargas | Navegación lateral / descargas | Descargas e instalaciones en curso; historial de operaciones |
| Biblioteca | Navegación lateral / biblioteca | Juegos instalados y listos para lanzar; versiones desactualizadas |
| Configuración y diagnóstico | Navegación lateral / configuración | Directorio de instalación, conexión, tema y registro de diagnóstico |
| Panel de estado | Panel derecho, persistente | Servidor conectado y operación en curso; nunca oculto |

Tres reglas estructurales:

- **El panel de estado es persistente.** Es la promesa del producto: el estado siempre está a la
  vista. No se esconde en un menú ni en un modal.
- **La navegación es una columna, no una barra inferior.** El flujo principal es biblioteca →
  descarga → biblioteca, no un conjunto de tareas paralelas.
- **Un solo nivel de modal.** Un diálogo nunca se apila sobre otro diálogo.

### Servidor

| Superficie | Se alcanza desde | Propósito |
|---|---|---|
| Catálogo | Navegación lateral / catálogo | Registrar y modificar videojuegos y sus metadatos |
| Clientes | Navegación lateral / clientes | Tabla densa de equipos: conexión, instalaciones, descargas, errores |
| Paquetes | Navegación lateral / paquetes | Publicar y controlar los paquetes del catálogo |

El panel de clientes del servidor es una **tabla densa**, no una colección de tarjetas: el organizador
necesita comparar equipos, y comparar requiere alineación.

### Cierre de superficies

Cada necesidad declarada en el PRD y en el Brief tiene una superficie que la entrega, y cada
superficie tiene al menos un flujo clave que desemboca en ella. El flujo de Ana recorre conexión,
catálogo, ficha, descarga, instalación, biblioteca y lanzamiento; el de Luis, clientes y diagnóstico
del servidor; el de Marta, administración del catálogo y publicación de paquetes.

## Voz y tono

Microcopia. La voz de marca vive en `DESIGN.md.Brand & Style`: **consola de operaciones silenciosa**,
sobria, técnica, sin coletilla comercial. Regla derivada: el texto describe el estado y el siguiente
paso, nunca contabiliza el esfuerzo ni celebra.

| Haz | No hagas |
|---|---|
| «Descarga en curso · 42 % · 3,2 GB de 7,6 GB» | «¡Casi lo tienes! ¡Sigue así!» |
| «No se pudo validar el paquete: el hash no coincide» | «Error 500» · «Fallo desconocido» |
| «Este juego requiere la versión 1.4 o posterior del servidor» | «Versión no válida» |
| «Pausada. Puedes reanudarla cuando quieras.» | «¿Seguro que quieres cancelar?» sin decir qué se pierde |
| «Falta el ejecutable en C:\Juegos\...\juego.exe» | «No se pudo iniciar el juego» |
| «Sin resultados. Prueba con otro nombre o quita un filtro.» | «Sin resultados» a secas |
| «Ana está preparando el equipo 3» | «El usuario Ana está connecté» |

Reglas transversales:

- **Segunda persona y verbo en primera.** «Pulsa», «Elige», «Indica», «Comprueba». Nunca «el usuario».
- **El error dice causa y siguiente paso.** Estructura obligatoria en tres partes: qué falló, por
  qué, qué hacer ahora. Sin códigos internos como texto principal.
- **Los datos literales van en monoespaciada y en su forma exacta.** Rutas, hashes y versiones no
  se redondean ni se traducen. Los **tamaños** tienen su propia regla de presentación, más abajo:
  un tamaño es un dato derivado, no un literal, y se expresa siempre en la misma unidad con la
  misma precisión.
- **Nada de interrogación por culpa.** La interfaz no pregunta «¿estás seguro?» salvo que la
  acción destruya trabajo no recuperable; en ese caso nombra qué se pierde.
- **Sin emojis, sin superlativos, sin gamificación.** Nada de «¡Listo!», «¡Felicidades!», «¡Nuevo!».
- **El tiempo es un dato, no una promesa.** Se dice «hace 2 min», nunca «en 30 segundos» para una
  descarga: la velocidad no está validada (NFR-2).

### Regla única de tamaños y progreso

Esta regla es la **única** fuente de verdad de cómo la interfaz representa tamaños y progreso.
Todo lo demás —microcopia, componentes, ejemplos, accesibilidad— la referencia en lugar de
inventar variantes. No cambia ningún dato subyacente: el catálogo, el paquete y la instalación
siguen almacenando el tamaño exacto en bytes; lo que se fija aquí es **cómo se muestra**.

**Unidad.** Todos los tamaños se expresan en **GB decimales (10⁹ bytes)**, con el separador
decimal español (coma). No se mezclan unidades dentro de la misma interfaz: no hay MiB ni GiB, ni
«kB» junto a «GB».

**Precisión.** Un tamaño se muestra con **exactamente un decimal**: «7,6 GB», «3,2 GB», «0,4 GB».
El valor es el tamaño exacto en bytes convertido a GB y redondeado a un decimal por la misma
regla en todas partes. No hay dos decimales, no hay «7 GB» sin decimal y no hay «7,60 GB».

**Par.** Cuando se muestran dos magnitudes (descargado y total), se expresan en la **misma
unidad**, con la **misma precisión** y en el **mismo orden**: «3,2 GB de 7,6 GB». El total es
siempre el segundo. La barra y el texto usan ese par.

**Progreso.** El progreso es siempre un **porcentaje entero**, derivado de los bytes transferidos
sobre los bytes totales: «42 %». Nunca hay decimales en el porcentaje («42,3 %»), nunca se
muestra una fracción alternativa («42/100»), y el porcentaje nunca se redondea a decenas.

**Fuente de verdad y correspondencia.** El porcentaje y el par de tamaños son coherentes entre sí
por construcción: ambos se derivan del mismo par (bytes transferidos, bytes totales). Si el texto
muestra «3,2 GB de 7,6 GB», el porcentaje mostrado corresponde a la misma fracción. La barra, el
texto de porcentaje y el texto de tamaño nunca se contradicen.

**Formas admitidas y prohibidas.**

| Contexto | Forma | Ejemplo |
|---|---|---|
| Tamaño de un juego (catálogo, ficha, biblioteca) | `<n>,<d> GB` | «7,6 GB» |
| Progreso de descarga o instalación | `<n> %` | «42 %» |
| Descarga en curso con par | `<n> % · <a> GB de <t> GB` | «42 % · 3,2 GB de 7,6 GB» |
| Descarga completada | `<n> % · <t> GB` | «100 % · 7,6 GB» |
| Tamaño perdido al cancelar | `<n>,<d> GB` | «Se borrarán los 3,2 GB descargados» |
| Tamaño de un paquete rechazado | `<n>,<d> GB` | «Paquete dañado · 7,6 GB» |

Prohibido: «7,60 GB», «7 GB», «7,6 GiB», «42,3 %», «42/100», «~7,6 GB», «7,6 MB», «7.6 GB» (punto
decimal) y cualquier tamaño sin unidad explícita.

**Monoespaciada.** El **valor numérico** del tamaño y del porcentaje va en `size` y `percent`, que
comparten la familia monoespaciada de `mono` porque son cifras que el jugador compara entre sí y se
alinean en columna; la **unidad** («GB» o «%») es el texto de `meta` que acompaña al valor. La barra
de progreso se acompaña siempre del texto, y el texto es la fuente de verdad del porcentaje.

### Microcopia por estado crítico

| Situación | Texto |
|---|---|
| Descargando | «Descargando · 42 % · 3,2 GB de 7,6 GB» + barra real |
| Pausado | «Pausada · 42 % · 3,2 GB de 7,6 GB» + acción «Reanudar» |
| Reanudando tras reinicio | «Reanudando desde 42 % · 3,2 GB de 7,6 GB» |
| Descarga completada | «Descargado · 100 % · 7,6 GB» |
| Validación superada | «Paquete validado» |
| Paquete corrupto | «Paquete dañado: el hash no coincide · 7,6 GB» |
| Paquete no confiable | «Paquete no confiable: no se pudo validar contra la raíz de confianza · 7,6 GB» |
| Instalado | «Instalado · v1.2» |
| Instalación en curso | «Instalando · 68 %» |
| Instalación existente validada | «Ya instalado · v1.2» + acción «Jugar» habilitada |
| Instalación existente inválida | «No se puede jugar: <causa>.» + acción «Jugar» bloqueada y acción primaria «Reparar» o «Descargar de nuevo» |
| Error de operación | «No se pudo instalar: <causa>. <Siguiente paso>.» |
| Ejecutándose | «En ejecución» |
| Cliente incompatible | «Este cliente (1.2) no es compatible con el servidor (1.4). Actualiza el cliente.» |

Los porcentajes y los tamaños de esta tabla siguen la regla única de *Regla única de tamaños y
progreso*: GB decimales con un decimal, porcentaje entero, par «<n> % · <a> GB de <t> GB» cuando
coexisten descarga y total.

## Patrones de componente

Comportamiento. La especificación visual vive en `DESIGN.md.Components`. Ningún componente de esta
tabla introduce tokens que no existan en `DESIGN.md`.

| Componente | Uso | Reglas de comportamiento |
|---|---|---|
| `button-primary` | Acción principal de la superficie | Una sola acción primaria por superficie. Refleja el estado real: deshabilitado cuando el prerrequisito no se cumple (paquete no validado, cliente incompatible, sin servidor). El foco no cambia el alto ({spacing.4} estable) |
| `button-secondary` | Acción de apoyo | Actúa sin competir con la primaria. Nunca la única acción disponible en un estado vacío |
| `button-danger` | Destrucción inequívoca | Reservado a cancelar una descarga, eliminar una instalación o descartar un error. Exige confirmación solo si se pierde trabajo no recuperable |
| `button-ghost` | Acción terciaria | Descartar un aviso, cerrar un panel. Nunca la única vía a un resultado |
| `field-input` | Dirección del servidor, búsqueda, directorio de instalación | Altura fija de 36 px; el foco pasa el borde a 2 px {colors.accent-action} sin deformar la rejilla. Acepta `Enter` como confirmación. La búsqueda filtra al escribir, con 200 ms de retardo, sin botón «buscar» |
| `card-game` | Catálogo, biblioteca | Clic en la tarjeta abre la ficha. El nombre se trunca a una línea con elipsis. Muestra versión, tamaño y jugadores en una línea de {typography.meta}; el valor del tamaño usa {typography.size} y la unidad «GB» usa {typography.meta}, con la forma «<n>,<d> GB» de la regla única de tamaños. En hover la superficie pasa a {colors.surface-overlay}; el borde no se refuerza |
| `panel-connection` | Panel derecho del cliente | Persistente. Muestra punto de presencia, nombre del servidor, dirección en uso y versión del servidor. Abajo, la operación en curso con su barra. Nunca se colapsa |
| `status-badge` | Estado de un juego, descarga, cliente u operación | Cápsula de una línea. Un solo color con su significado canónico. El color nunca es la única señal: acompaña a texto. Estados: sincronizando, disponible, descargando, pausado, validado, instalado, instalado con versión desactualizada, en ejecución, error |
| `progress-bar` | Descarga, instalación, validación | Refleja el progreso real, derivado de los bytes transferidos sobre los bytes totales, igual que el porcentaje que lo acompaña. En error la barra conserva su longitud real y el estado lo comunica la insignia: una barra al 100 % en rojo mentiría. En operación completada verificada el relleno pasa a {colors.state-success}. Siempre va acompañada del texto «<n> %»; el valor entero usa {typography.percent} y «%» usa {typography.meta}; sin texto, la barra no es accesible por sí sola |
| `data-table-row` | Tabla de clientes del servidor | Alto de 40 px. Celdas de dato en {typography.body-strong} y unidades en {typography.meta}. Orden estable por nombre de equipo. Fila seleccionable para ver su detalle. La columna de nombre muestra siempre el **nombre efectivo** —el único bajo el que esa fila está registrada—, nunca el nombre propuesto que colisionó. Ninguna fila queda oculta por un rechazo de nombre: un equipo homónimo aparece con el nombre único que el servidor le asignó |
| `toast-error` | Aviso transitorio de error | Contenido: causa, acción intentada y siguiente paso. Sin iconografía; el texto es el aviso. No sustituye a la persistencia del error en el registro |
| `dialog-surface` | Confirmación de operación privilegiada, resumen de error | Un solo nivel. Encabezado `headline`, cuerpo `body`, pie con acciones alineadas a la derecha. `Esc` cancela la acción no destructiva y devuelve el foco al elemento invocador |

### Reglas de estado por componente

- **Barra de progreso**: el texto que la acompaña es la fuente de verdad del porcentaje; la barra es
  la fuente de verdad del avance. Cuando no hay texto, la barra no es accesible por sí sola. El
  porcentaje es un entero derivado de los bytes y su forma es la de la regla única de tamaños y
  progreso; la barra y el texto nunca muestran fracciones distintas.
- **Insignia de estado**: nunca dos colores en la misma insignia. Si un estado necesita dos
  dimensiones, son dos insignias con texto.
- **Tarjeta de juego**: en la Biblioteca, la acción primaria depende de la validez de la
  instalación, no de su mera presencia. Con instalación **validada**, la primaria es «Jugar»;
  con instalación **inválida**, «Jugar» aparece bloqueado con el motivo a la vista y la primaria
  es «Reparar» (o «Descargar de nuevo» si la instalación no es reparable). Con versión
  desactualizada, «Actualizar» sustituye a la primaria y «Jugar» permanece disponible solo si la
  instalación sigue siendo válida. La regla completa, con su tabla de situaciones, está en
  *Estados de instalación y acción primaria*.

### Nombre del equipo: propuesta, validación y adopción

El nombre del equipo es la **etiqueta humana** con la que el organizador lo distingue en la tabla de
clientes. El cliente **propone**; el **servidor valida y resuelve**. La interfaz no decide la
unicidad por su cuenta y nunca presenta un nombre rechazado por el servidor como si fuera el
definitivo.

| Situación observable | Comportamiento de la interfaz |
|---|---|
| Nombre propuesto admisible y libre | El cliente se registra y muestra ese nombre. La tabla del servidor lo muestra con ese mismo nombre |
| Nombre propuesto **colisiona** con el de otro equipo ya registrado | El servidor **no rechaza** al cliente ni lo deja invisible: le asigna un **nombre único derivado** del propuesto y se lo notifica. El cliente **adopta** ese nombre efectivo y lo muestra en el panel de estado. La tabla del servidor muestra el nombre efectivo, y el cliente que ya usaba el nombre propuesto conserva el suyo |
| Nombre propuesto **inválido** | El registro se **rechaza** con un error que dice causa y siguiente paso (qué no cumple y qué hacer: elegir otro nombre). El cliente **queda sin registrar y no opera**; el estado de conexión lo refleja y el motivo es visible, no oculto en el log |
| El equipo cambia su nombre en el cliente | El cliente propone el nuevo nombre en su siguiente registro. Si está libre, se adopta; si colisiona, el servidor aplica la misma regla y asigna el nombre derivado. El **identificador técnico del cliente no cambia** en ningún caso |

Reglas transversales:

- **La causa y el siguiente paso son parte del mensaje.** Un rechazo por nombre no dice solo
  «nombre inválido»: nombra qué no se cumple y qué hacer ahora. El texto es el del catálogo de
  errores del servidor, sin reescribirlo.
- **El nombre efectivo es el único que se muestra.** Una vez resuelto, la interfaz muestra el
  nombre bajo el que el equipo está registrado. El nombre propuesto que colisionó **no** se
  muestra en ningún sitio como si fuera el vigente.
- **La colisión no es un error del cliente.** Un equipo homónimo se registra y aparece en la tabla:
  el problema resuelto es la ambigüedad de la vista, no el acceso del equipo.
- **La unicidad del nombre no autentica nada.** No hay campo de credencial, ni pantalla de
  contraseña, ni estado de sesión: el nombre del equipo es una etiqueta de la vista de
  organización, y el modelo de acceso sigue siendo **LAN administrada sin autenticación ni
  autorización de clientes** (FR-4).

### Estados de instalación y acción primaria

La distinción entre instalación **validada** e **inválida** es un contrato de comportamiento, no
un matiz de redacción. Se aplica por igual en la Biblioteca y en la Ficha del videojuego, y
ninguna de las dos superficies puede abrir un camino de lanzamiento distinto.

| Situación | Acción «Jugar» | Acción primaria | Acciones secundarias |
|---|---|---|---|
| Instalación validada | Habilitada | «Jugar» | «Actualizar» si la versión está desactualizada; «Reparar» solo bajo petición expresa («Verificar instalación») |
| Instalación inválida y reparable | Bloqueada, con el motivo visible junto a la etiqueta | «Reparar» | «Diagnosticar» abre el error registrado |
| Instalación inválida y no reparable | Bloqueada, con el motivo visible junto a la etiqueta | «Descargar de nuevo» | «Diagnosticar» abre el error registrado |

Reglas transversales:

- **El motivo del bloqueo es visible sin interactuar.** No está en un tooltip, no está solo en el
  log de Diagnóstico y no requiere hover ni foco: aparece en la propia tarjeta o ficha, junto a la
  etiqueta «Jugar». El tooltip puede repetirlo, nunca ser su único lugar.
- **«Jugar» bloqueada no es un error de estado; es un estado con su etiqueta.** La insignia
  {colors.state-danger} acompaña al motivo, y el texto del motivo es el que explica qué falta.
- **No hay segunda vía de lanzamiento.** Mientras la instalación no esté validada, ninguna otra
  superficie (panel de estado, ficha, error de lanzamiento) ofrece ejecutar el juego.
- **«Reparar» reutiliza el paquete ya validado.** No vuelve a descargar y no pide una confirmación
  que no tenga consecuencia: la consecuencia es rehacer una instalación inválida, y la interfaz la
  nombra («Se reparará la instalación; la descarga no se repite»).

### Cobertura de teclado de las acciones de instalación

El teclado llega a las mismas acciones que el ratón, con el mismo resultado y el mismo motivo a la
vista:

- **`Tab` / `Shift+Tab`** — recorren la Biblioteca y la Ficha en orden de lectura. «Jugar»
  bloqueada recibe foco como cualquier otro control: no se omite ni se deshabilita de forma
  invisible; al recibir foco, lee su motivo junto a la etiqueta.
- **`Enter`** — activa el control enfocado. En una instalación validada lanza el juego; en una
  instalación inválida lanza «Reparar» o «Descargar de nuevo». `Enter` sobre «Jugar» bloqueada no
  hace nada y no dispara una descarga ni una reparación implícita.
- **`Espacio`** — alterna pausa y reanudación de la descarga enfocada; no actúa sobre la
  instalación. No es una vía para reparar.
- **`Ctrl+4`** — salta a la Biblioteca; desde la Biblioteca, `Tab` alcanza «Jugar», «Reparar» o
  «Descargar de nuevo» sin salir de la superficie.
- **Foco visible** — el control bloqueado conserva un indicador de foco visible
  ({colors.accent-action}) y un estado de foco que no depende del color de la insignia, para que
  un usuario de teclado sepa que hay una acción y por qué no puede pulsarse.

Criterio de aceptación verificable: con el foco en «Jugar» de una instalación inválida, un lector
de pantalla anuncia el nombre de la acción, su estado bloqueado y el motivo, y la tecla `Enter`
no produce ningún cambio de estado.

## Patrones de estado

| Estado | Superficie | Tratamiento |
|---|---|---|
| Carga inicial | Catálogo | Esqueletos de tarjetas con la proporción real de la rejilla; no texto «cargando» giratorio |
| Catálogo vacío | Catálogo | «El catálogo está vacío.» + una acción: «Sincronizar ahora», si el servidor está conectado; si no, «Conectar al servidor» |
| Sin resultados de búsqueda | Catálogo | «Sin resultados para «<consulta>».» + acción «Quitar filtros» si hay filtros activos; la búsqueda es reversible sin dejar la superficie |
| Servidor no disponible | Global | Panel derecho en {colors.state-warning}: «Servidor no disponible. Se conserva el último catálogo sincronizado.» Las acciones que requieren servidor se deshabilitan con su motivo visible en el tooltip |
| Reconectando | Global | Punto de presencia en {colors.state-warning}; reintento automático con contador visible «Reintentando · 3 s». No hay modal que interrumpa |
| Descubrimiento sin resultado | Conexión | «No se encontró ningún servidor en esta red.» + acción primaria «Introducir dirección manualmente». El descubrimiento es una vía, no una puerta única |
| Cliente incompatible | Conexión, Global | Estado de {colors.state-danger} con la versión del cliente y la del servidor, y el siguiente paso. No hay formulario de credenciales: no las hay |
| Catálogo desactualizado | Catálogo | Aviso discreto «Catálogo desactualizado · sincronizado hace 12 min» con «Sincronizar ahora». No bloquea la navegación |
| Paquete en descarga | Ficha, Descargas | La acción primaria pasa a «Pausar»; la descarga se puede reanudar desde la misma posición. El progreso persiste ante reinicio |
| Paquete pausado | Descargas | Estado {colors.state-warning}. Acciones: «Reanudar» (primaria) y «Cancelar» (peligro). Cancelar pide confirmación nombrando lo que se pierde: «Se borrarán los 3,2 GB descargados» |
| Validación fallida | Ficha, Diagnóstico | Estado {colors.state-danger} con la causa diferenciada: «dañado» (corrupción) o «no confiable» (no validable contra la raíz de confianza). El alta de instalación queda bloqueada y el motivo es visible, no oculto en el log |
| Instalación existente validada | Ficha, Biblioteca | «Ya instalado · v1.2». La instalación pasó la validación: la acción primaria es «Jugar» y es la única forma de lanzar el juego desde la Biblioteca o la Ficha |
| Instalación existente inválida o reparable | Ficha, Biblioteca | «Jugar» aparece **bloqueado** con el motivo visible («No se puede jugar: instalación incompleta», «No se puede jugar: falta el ejecutable», «No se puede jugar: el paquete no coincide con la validación») y la acción primaria pasa a «Reparar», que rehace la instalación desde el paquete ya validado sin volver a descargar. Si la instalación es inválida **y no reparable**, el motivo lo dice y la única acción es «Descargar de nuevo» |
| Instalación en curso | Descargas | Barra de progreso con fase («Extrayendo», «Aplicando manifiesto») y porcentaje |
| Instalación fallida | Diagnóstico | Error persistido y reintentable sin reinstalar el cliente. La acción «Reintentar» está disponible |
| Versión desactualizada | Biblioteca, Ficha | Insignia {colors.state-warning} «Actualización disponible» + acción «Actualizar». La versión instalada y la disponible se muestran en monoespaciada |
| Error de lanzamiento | Biblioteca | «No se pudo iniciar: <causa>.» con el ejecutable y su ruta exacta cuando el problema es de ruta |
| Sin juegos instalados | Biblioteca | «No tienes juegos instalados todavía.» + acción «Ver catálogo» |
| Panel de estado sin operación | Panel derecho | Muestra la última operación terminada con su resultado, no un espacio vacío |
| Cliente desconectado (servidor) | Clientes | Fila con estado {colors.state-danger}; el error asociado se muestra en el detalle de la fila |
| Registro rechazado por nombre de equipo | Conexión, Diagnóstico | El cliente queda **sin registrar y no opera**. El motivo es visible con causa y siguiente paso («qué no cumple» y «qué hacer ahora»), no oculto en el log. No se pide credencial ni contraseña: el nombre es una etiqueta, no un permiso |
| Nombre de equipo colisionante | Conexión, Clientes | El servidor asigna un **nombre único derivado** y lo notifica; el cliente lo adopta y lo muestra. No es un estado de error: el equipo está registrado y visible. La tabla del servidor muestra el nombre efectivo |

## Primitivas de interacción

**Ratón primero, teclado siempre disponible.** El jugador de LAN sostiene el portátil y puede estar
en una sala a oscuras con las manos ocupadas; el ratón es la vía primaria, el teclado es la vía
completa.

- `Enter` — confirma el campo enfocado (dirección del servidor, búsqueda, directorio).
- `Esc` — cierra el diálogo superior; si no hay diálogo, limpia la búsqueda.
- `Ctrl+F` — enfoca la búsqueda del catálogo desde cualquier superficie.
- `Ctrl+,` — abre Configuración y diagnóstico.
- `Ctrl+1`…`Ctrl+4` — salta a Catálogo, Descargas, Biblioteca y Configuración.
- `Espacio` — pausa o reanuda la descarga enfocada.
- `Tab` / `Shift+Tab` — recorre la superficie en orden de lectura; el foco es siempre visible.

Reglas de puntero:

- **Un clic en la tarjeta abre la ficha.** Las acciones secundarias viven en la ficha, no en la
  tarjeta, para que el clic significa siempre lo mismo.
- **Hover no es la única vía.** Las acciones que solo aparecen en hover (descartar aviso, acciones
  de fila) tienen equivalente de teclado o están siempre visibles en el detalle.
- **Un nivel de modal.** Un diálogo nunca se apila sobre otro.
- **Nada de desplazamiento infinito.** El catálogo pagina por scrolls de tamaño fijo con paginación
  explícita, para que el jugador pueda volver a la misma posición.
- **Progreso no bloquea.** While una descarga está en curso, la navegación entre superficies es
  libre; el progreso vive en el panel derecho.

## Piso de accesibilidad

Comportamiento. El contraste visual vive en `DESIGN.md`.

- **WCAG 2.2 AA** en todas las superficies del cliente y del servidor.
- **Teclado completo.** Todo estado alcanzable con el ratón es alcanzable con teclado, incluidos
  diagonales, tabla de clientes, filas de historial y acciones de error. El orden de tabulación
  sigue el orden de lectura en cada superficie.
- **Foco visible siempre.** El foco usa {colors.accent-action}; nunca se suprime. En superficies
  oscuras, el foco no depende del cambio de tono de la superficie.
- **Anuncios de lector de pantalla.** Al cambiar de superficie se anuncia «Catálogo, catálogo de
  videojuegos, 24 juegos». El progreso de descarga se anuncia por cambio de porcentaje significativo
  (10 %, 25 %, 50 %, 75 %, 100 %), no en cada tick. Los errores se anuncian como texto, no solo
  visualmente.
- **El color nunca es la única señal.** Todo estado con color lleva además texto; las insignias
  includen su estado en el nombre accesible.
- **Objetivo de tamaño.** Los objetivos interactivos cumplen el mínimo de 24×24 px; los botones
  primarios y los campos, 36 px de alto.
- **Escalado del sistema.** El escalado de texto y de tamaño de ventana lo gobierna el sistema
  operativo; la interfaz no reimplanta el escalado con factores propios. La rejilla del catálogo
  recalcula columnas al redimensionar sin truncar el contenido.
- **Sin dependencia del color en la barra de progreso.** El porcentaje va en texto junto a la barra.
- **Errores identificables.** Cada error tiene un nombre estable en texto, para que un jugador
  pueda comunicarlo al organizador (FR-10, NFR-7).
- **Modo claro.** El visor de diagnóstico y la ficha de juego disponen de variante clara con los
  tokens `-light` de `DESIGN.md`. El modo oscuro es el predeterminado.

## Escritorio y plataforma

| Condición | Comportamiento |
|---|---|
| Ventana redimensionada | La columna de navegación conserva su ancho; la rejilla del catálogo recalcula columnas: mínimo 3 a 1280 px, hasta 6; por debajo de 3 columnas cabe a 2; por debajo de 2, a lista de una columna. La última fila se alinea a la izquierda y las tarjetas no se estiran |
| Ventana estrecha | El panel derecho de estado se reduce a la línea de presencia y la operación en curso, sin desaparecer |
| Modo claro | Solo el visor de diagnóstico y la ficha de juego. El resto de la app mantiene el modo oscuro |
| Sin Internet | Ninguna superficie muestra un error de red global. El producto opera íntegro en LAN (NFR-1). Un icono de red ausente no se confunde con «sin servidor» |
| Cliente y servidor en el mismo equipo | Ambos pueden ejecutarse a la vez; la instancia del servidor no ocupa el panel de estado de un cliente |
| Ejecución de un juego | El juego se lanza fuera de la ventana de LAN Party Hub; el cliente queda en segundo plano y el panel de estado sigue mostrando «En ejecución» |
| Teclado en juego | LAN Party Hub no captura teclas mientras el juego está en ejecución |
| Múltiples instancias de cliente | Cada equipo tiene una instancia; el servidor los distingue por identificador y nombre de equipo |

## Flujos clave

Los protagonistas se toman literalmente del PRD §2.2: Ana (jugadora de LAN, UJ-1), Luis
(organizador del evento, UJ-2) y Marta (administradora del servidor, UJ-3).

### Flujo 1 — Ana llega con su portátil (UJ-1)

1. Ana abre LAN Party Hub en su portátil. La aplicación muestra la superficie de Conexión con dos
   vías equivalentes: **Descubrir en la red** y **Introducir la dirección manualmente**.
2. El servidor del evento está encendido. El descubrimiento encuentra un servidor; Ana lo elige. El
   panel derecho muestra el punto de presencia en {colors.accent-lan} y el nombre del servidor.
3. La comprobación de compatibilidad de versión se ejecuta. La versión del cliente es compatible: la
   app continúa. Si no lo fuera, Ana vería el diagnóstico explícito con su versión y la del servidor,
   y ninguna operación de catálogo, descarga o instalación se habilitaría.
4. El catálogo se sincroniza. Ana ve la rejilla de tarjetas.
5. Ana busca por nombre y abre la ficha del juego. Comprueba versión, tamaño, jugadores, sistema
   operativo y requisitos. Si la instalación existente es inválida y reparable, «Jugar» aparece
   bloqueada con el motivo visible y «Reparar» es la acción primaria; si no es reparable,
   «Descargar de nuevo» es la acción primaria. «Jugar» no ofrece una vía alternativa.
6. Pulsa «Descargar». La descarga arranca desde el servidor; la barra del panel derecho avanza con
   el porcentaje en texto.
7. Pausa la descarga para atender a otra cosa y la reanuda. Cierra el cliente y lo vuelve a abrir: la
   descarga **se recupera desde el progreso persistido**, sin empezar de cero.
8. La descarga se completa. El cliente valida el paquete: integridad verificada. Si la validación
   fallara, Ana vería la causa diferenciada —paquete dañado o no confiable— y la instalación quedaría
   bloqueada con el motivo visible.
9. Ana inicia la instalación portable en el directorio configurado. Si la instalación existente es
   inválida, «Reparar» rehace la instalación usando el paquete ya validado, sin repetir la descarga;
   si no es reparable, «Descargar de nuevo» vuelve a obtener el paquete. En ambos casos, «Jugar»
   permanece bloqueada hasta validar la instalación. Las operaciones privilegiadas, si el manifiesto
   las incluye, piden confirmación explícita antes de ejecutarse.
10. La instalación termina. El cliente valida la instalación y la muestra en la Biblioteca con la
    insignia «Instalado · v1.2».
11. **Clímax:** Ana pulsa «Jugar». El proceso se lanza con sus argumentos y su directorio de
    trabajo, el panel de estado pasa a «En ejecución» y, en la pantalla del servidor, el equipo de Ana
    aparece con la instalación registrada y la operación cerrada. Ana no ha tenido que preguntar en
    ningún momento en qué estado estaba nada.

**Fallo:** el servidor se cae a mitad de descarga → el panel derecho pasa a {colors.state-warning}
«Servidor no disponible», la barra conserva el último porcentaje real, el reintento automático
reanuda cuando el servidor vuelve y Ana no pierde lo descargado. Un paquete alterado → validación
fallida, instalación bloqueada, error con causa y siguiente paso en Diagnóstico. Una instalación
inválida → «Jugar» bloqueada con el motivo visible; «Reparar» es la acción primaria si es reparable
y «Descargar de nuevo» si no lo es.

### Flujo 2 — Luis diagnostica un equipo (UJ-2)

1. Luis abre LAN Party Hub Server en el portátil del organizador. El panel de clientes muestra la
   tabla densa de equipos: identificador, nombre, estado de conexión, juegos y versiones
   instalados, descargas activas y errores.
2. Uno de los equipos aparece desconectado, en {colors.state-danger}. Luis selecciona la fila y abre
   su detalle.
3. El detalle muestra el error registrado con su causa: «No se pudo validar el paquete: el hash no
   coincide», y la descarga activa con su progreso real.
4. Luis ve que el error es de validación, no de red, y decide: ese equipo necesita volver a
   descargar el paquete.
5. **Clímax:** Luis no necesita preguntar nada a nadie. La fila del equipo ya dice qué pasó, con
   quéSeverity y qué corresponde hacer; el tiempo de la LAN deja de consumirse en rondas de
   preguntas por el pasillo.

**Fallo:** si un cliente incompatible se conecta, Luis lo ve con su versión y su diagnóstico, y la
fila indica que no opera —sin iniciar operaciones de juego—, sin que el estado tenga que deducirse.

### Flujo 3 — Marta publica el catálogo (UJ-3)

1. Marta abre la sección Catálogo del servidor y registra un videojuego: identificador único,
   nombre, descripción, categorías, etiquetas, carátula, versión, tamaño, jugadores, sistema
   operativo, tipo de distribución, tipo de instalación, configuración de lanzamiento,
   dependencias, licencia y restricciones de distribución.
2. La ruta de lanzamiento y la versión se muestran en monoespaciada. El tamaño se introduce en
   bytes exactos y se presenta en la interfaz como un valor decimal derivado: «7,6» en
   {typography.size} y «GB» en {typography.meta}. El formulario marca como obligatorio lo que
   FR-1 exige.
3. Marta adjunta el paquete. Un paquete no puede publicarse si falta la información de licencia o
   restricciones necesaria para comprobar la publicación: el formulario lo impide y explica cuál
   falta.
4. Marta publica. El registro aparece en la lista del servidor y, tras la sincronización, en el
   catálogo de los clientes.
5. **Clímax:** Marta publica una vez y todos los clientes de la LAN lo ven. A partir de ese momento
   nadie depende de un USB, una carpeta compartida improvisada ni de que alguien recuerde qué
   juegos había.

**Fallo:** un manifiesto manipulado con una acción no permitida → se rechaza antes de instalar y
queda registrado; una extracción fuera del directorio autorizado → se bloquea. En ambos casos, el
error nombra la causa y el paso siguiente en Diagnóstico.

### Flujo 4 — Ana recupera una operación interrumpida (recuperación transversal)

1. Ana reinicia el equipo a mitad de una descarga de 7,6 GB. La app se abre con la operación
   recuperada: «Reanudando desde 42 % · 3,2 GB de 7,6 GB».
2. Si la instalación quedó incompleta, el cliente la detecta y la marca como reparable en lugar de
   repetir el paquete entero. En la Biblioteca y en la Ficha, «Jugar» permanece bloqueada con el
   motivo visible y «Reparar» es la acción primaria.
3. Si el manifiesto contenía una operación privilegiada, al reanudar se vuelve a pedir la
   confirmación explícita: la confirmación no se hereda de una sesión anterior.
4. **Clímax:** Ana vuelve al juego sin haber perdido ni la descarga ni la decisión sobre lo
   privilegiado. La resiliencia no es una promesa del manual: es el comportamiento por defecto.

## Inspiration y anti-patrones

- **Heredado — consola de operaciones silenciosa:** el estado siempre visible en un panel
  persistente, sin menús anidados ni accesos escondidos. Es la postura de un monitor de servidor, no la de una
  tienda de aplicaciones.
- **Heredado — la rejilla de carátulas:** la familiaridad de una biblioteca de juegos, con el
  catálogo como superficie de entrada y la ficha como lugar de la decisión.
- **Rechazado — gamificación del descargado:** nada de insignias de progreso social, sin carruseles
  de ofertas, sin confeti, sin insignias de «nuevo». LAN Party Hub no compite por el tiempo del
  jugador; le devuelve el tiempo que le costaba la logística.
- **Rechazado — tienda:** nada de precios, carritos, reseñas ni recomendaciones comerciales. La
  biblioteca es local y el catálogo lo define la organización.
- **Rechazado — el modal como sistema de estado:** el estado no se comunica abriendo ventanas
  sucesivas; vive en el panel derecho y en las insignias de estado.
- **Rechazado — los canales de descarga peer-to-peer en la interfaz:** el MVP muestra descarga desde el
  servidor. El intercambio entre peers no aparece en la interfaz hasta que la PoC lo valide
  (OQ-3, sin cerrar).

## Preguntas abiertas y supuestos

- **[ASSUMPTION: OQ-2 / R3]** La UX define dos vías de conexión equivalentes y visibles. El
  mecanismo concreto de descubrimiento y su comportamiento en redes segmentadas los decide Fase D;
  esta espina no los fija.
- **[ASSUMPTION: OQ-1 / R5]** La UX distingue visualmente «paquete dañado» (corrupción) de
  «paquete no confiable» (no validable contra la raíz de confianza) sin nombrar raíz, algoritmo ni
  formato de firma. La definición del ancla es de Fase D.
- **[ASSUMPTION: OQ-3 / P2P]** La interfaz no expone intercambio entre peers. La ausencia de esa
  capacidad en la UI es una decisión de alcance del MVP, no un descuido.
- **[ASSUMPTION: OQ-4 / NFR-2]** Ninguna microcopia promete velocidad de transferencia, tasa de
  refresco ni tiempo restante. El progreso se expresa como porcentaje entero y los tamaños como
  valores decimales en GB, ambos derivados de los bytes reales.
- **[NOTE FOR UX]** La matriz de versiones compatibles y el formato exacto del diagnóstico de
  incompatibilidad se concretan en Fase D (FR-4). Esta espina define el comportamiento observable:
  diagnóstico explícito, con la versión del cliente y la del servidor, y sin habilitar operaciones.
- **[NOTE FOR UX]** El comportamiento de la barra de progreso durante una descarga con velocidad
  variable no se promete: se muestra el porcentaje real, que es lo único verificable.
- **[NOTE FOR UX]** El modo claro se limita al visor de diagnóstico y a la ficha de juego, según
  `DESIGN.md.Colors`. La extensión a otras superficies es una decisión posterior.

## Sobre este documento

- **Pares:** `DESIGN.md` (identidad visual) y `EXPERIENCE.md` (experiencia). Gana la espina ante
  conflicto con cualquier mock, wireframe o importación.
- **Fuentes:** PRD aprobado en el Checkpoint B (`prds/prd-lan-party-hub-2026-10-26/prd.md`),
  Product Brief aprobado en el Checkpoint A (`briefs/brief-lan-party-hub-2026-10-26/brief.md`) y
  `docs/plan.md`. Ninguna decisión técnica abierta (R3, R5, P2P) se cierra aquí.
- **Fase:** C — Experiencia de usuario. Estado `final`.
- **Checkpoint:** Checkpoint C — validación de los flujos UX, **aprobado el 2026-10-26**; la
  aprobación humana es explícita y no queda implícita en el estado `final` del documento.
- **Siguiente checkpoint:** Checkpoint D — validación de arquitectura y riesgos (Fase D).
