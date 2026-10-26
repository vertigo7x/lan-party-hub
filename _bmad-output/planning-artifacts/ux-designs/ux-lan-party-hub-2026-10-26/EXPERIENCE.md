---
name: LAN Party Hub
status: final
description: "Contrato de experiencia de LAN Party Hub: arquitectura de información, comportamiento, estados, interacción, accesibilidad y flujos del cliente de escritorio y del servidor del evento."
sources:
  - {planning_artifacts}/prds/prd-lan-party-hub-2026-10-26/prd.md
  - {planning_artifacts}/briefs/brief-lan-party-hub-2026-10-26/brief.md
  - {project-root}/docs/plan.md
updated: 2026-10-26
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
sobria, técnica, sin laulación comercial. Regla derivada: el texto describe el estado y el siguiente
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
- **Los datos literales van en monoespaciada y en su forma exacta.** Rutas, hashes, versiones y
  tamaños no se redondean ni se traducen.
- **Nada de interrogación por culpa.** La interfaz no pregunta «¿estás seguro?» salvo que la
  acción destruya trabajo no recuperable; en ese caso nombra qué se pierde.
- **Sin emojis, sin superlativos, sin gamificación.** Nada de «¡Listo!», «¡Felicidades!», «¡Nuevo!».
- **El tiempo es un dato, no una promesa.** Se dice «hace 2 min», nunca «en 30 segundos» para una
  descarga: la velocidad no está validada (NFR-2).

### Microcopia por estado crítico

| Situación | Texto |
|---|---|
| Descargando | «Descargando · 42 %» + barra real |
| Pausado | «Pausada · 42 %» + acción «Reanudar» |
| Reanudando tras reinicio | «Reanudando desde 42 %» |
| Descarga completada | «Descargado · 7,6 GB» |
| Validación superada | «Paquete validado» |
| Paquete corrupto | «Paquete dañado: el hash no coincide» |
| Paquete no confiable | «Paquete no confiable: no se pudo validar contra la raíz de confianza» |
| Instalado | «Instalado · v1.2» |
| Instalación en curso | «Instalando · 68 %» |
| Error de operación | «No se pudo instalar: <causa>. <Siguiente paso>.» |
| Ejecutándose | «En ejecución» |
| Cliente incompatible | «Este cliente (1.2) no es compatible con el servidor (1.4). Actualiza el cliente.» |

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
| `card-game` | Catálogo, biblioteca | Clic en la tarjeta abre la ficha. El nombre se trunca a una línea con elipsis. Muestra versión, tamaño y jugadores en una línea de {typography.meta}. En hover la superficie pasa a {colors.surface-overlay}; el borde no se refuerza |
| `panel-connection` | Panel derecho del cliente | Persistente. Muestra punto de presencia, nombre del servidor, dirección en uso y versión del servidor. Abajo, la operación en curso con su barra. Nunca se colapsa |
| `status-badge` | Estado de un juego, descarga, cliente u operación | Cápsula de una línea. Un solo color con su significado canónico. El color nunca es la única señal: acompaña a texto. Estados: sincronizando, disponible, descargando, pausado, validado, instalado, instalado con versión desactualizada, en ejecución, error |
| `progress-bar` | Descarga, instalación, validación | Refleja el progreso real. En error la barra conserva su longitud real y el estado lo comunica la insignia: una barra al 100 % en rojo mentiría. En operación completada verificada el relleno pasa a {colors.state-success} |
| `data-table-row` | Tabla de clientes del servidor | Alto de 40 px. Celdas de dato en {typography.body-strong} y unidades en {typography.meta}. Orden estable por nombre de equipo. Fila seleccionable para ver su detalle |
| `toast-error` | Aviso transitorio de error | Contenido: causa, acción intentada y siguiente paso. Sin iconografía; el texto es el aviso. No sustituye a la persistencia del error en el registro |
| `dialog-surface` | Confirmación de operación privilegiada, resumen de error | Un solo nivel. Encabezado `headline`, cuerpo `body`, pie con acciones alineadas a la derecha. `Esc` cancela la acción no destructiva y devuelve el foco al elemento invocador |

### Reglas de estado por componente

- **Barra de progreso**: el texto que la acompaña es la fuente de verdad del porcentaje; la barra es
  la fuente de verdad del avance. Cuando no hay texto, la barra no es accesible por sí sola.
- **Insignia de estado**: nunca dos colores en la misma insignia. Si un estado necesita dos
  dimensiones, son dos insignias con texto.
- **Tarjeta de juego**: en la biblioteca, la acción primaria es «Jugar» y solo aparece cuando la
  instalación está validada; si la versión está desactualizada, aparece «Actualizar» en su lugar.

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
| Instalación existente | Ficha, Biblioteca | «Ya instalado · v1.2». Acción primaria «Jugar»; secundaria «Reparar» solo cuando la instalación es inválida y reparable |
| Instalación en curso | Descargas | Barra de progreso con fase («Extrayendo», «Aplicando manifiesto») y porcentaje |
| Instalación fallida | Diagnóstico | Error persistido y reintentable sin reinstalar el cliente. La acción «Reintentar» está disponible |
| Versión desactualizada | Biblioteca, Ficha | Insignia {colors.state-warning} «Actualización disponible» + acción «Actualizar». La versión instalada y la disponible se muestran en monoespaciada |
| Error de lanzamiento | Biblioteca | «No se pudo iniciar: <causa>.» con el ejecutable y su ruta exacta cuando el problema es de ruta |
| Sin games instalados | Biblioteca | «No tienes juegos instalados todavía.» + acción «Ver catálogo» |
| Panel de estado sin operación | Panel derecho | Muestra la última operación terminada con su resultado, no un espacio vacío |
| Cliente desconectado (servidor) | Clientes | Fila con estado {colors.state-danger}; el error asociado se muestra en el detalle de la fila |

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
5. Ana busca por nombre, abre la ficha del juego y comprueba versión, tamaño, jugadores, sistema
   operativo y requisitos.
6. Pulsa «Descargar». La descarga arranca desde el servidor; la barra del panel derecho avanza con
   el porcentaje en texto.
7. Pausa la descarga para atender a otra cosa y la reanuda. Cierra el cliente y lo vuelve a abrir: la
   descarga **se recupera desde el progreso persistido**, sin empezar de cero.
8. La descarga se completa. El cliente valida el paquete: integridad verificada. Si la validación
   fallara, Ana vería la causa diferenciada —paquete dañado o no confiable— y la instalación quedaría
   bloqueada con el motivo visible.
9. Ana inicia la instalación portable en el directorio configurado. Las operaciones privilegiadas,
   si el manifiesto las incluye, piden confirmación explícita antes de ejecutarse.
10. La instalación termina. El cliente detecta que el juego ya está instalado y lo muestra en la
    Biblioteca con la insignia «Instalado · v1.2».
11. **Clímax:** Ana pulsa «Jugar». El proceso se lanza con sus argumentos y su directorio de
    trabajo, el panel de estado pasa a «En ejecución» y, en la pantalla del servidor, el equipo de Ana
    aparece con la instalación registrada y la operación cerrada. Ana no ha tenido que preguntar en
    ningún momento en qué estado estaba nada.

**Fallo:** el servidor se cae a mitad de descarga → el panel derecho pasa a {colors.state-warning}
«Servidor no disponible», la barra conserva el último porcentaje real, el reintento automático
reanuda cuando el servidor vuelve y Ana no pierde lo descargado. Un paquete alterado → validación
fallida, instalación bloqueada, error con causa y siguiente paso en Diagnóstico.

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
2. Los campos literales —ruta de lanzamiento, tamaño, versión— se muestran en monoespaciada, y el
   formulario marca como obligatorio lo que FR-1 exige.
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
   recuperada: «Reanudando desde 42 %».
2. Si la instalación quedó incompleta, el cliente la detecta y la marca como reparable en lugar de
   repetir el paquete entero.
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
- **[ASSUMPTION: OQ-4 / NFR-2]** Ninguna microcopia promete velocidad de transferencia ni tasa de
  refresco; el progreso se expresa en porcentaje real y en tamaño, nunca en tiempo restante.
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
- **Siguiente checkpoint:** Checkpoint C — validación de los flujos UX.
