---
name: LAN Party Hub
status: final
updated: 2026-10-26
checkpoint: "Checkpoint C"
checkpoint_status: approved
checkpoint_approved: 2026-10-26
next_checkpoint: "Checkpoint D — validación de arquitectura y riesgos (Fase D)"
description: "Biblioteca de videojuegos de escritorio para una LAN party local: descubre el servidor, baja el paquete, instálalo en modo portable y lánzalo, sin Internet."
colors:
  surface-base: '#12141A'
  surface-raised: '#1B1E26'
  surface-overlay: '#232733'
  surface-sunken: '#0D0F13'
  ink-primary: '#F2F4F8'
  ink-secondary: '#A7AEBD'
  ink-tertiary: '#6E7688'
  ink-disabled: '#4A5060'
  accent-lan: '#4DD8A4'
  accent-lan-ink: '#06231A'
  accent-action: '#5B8CFF'
  accent-action-ink: '#050D24'
  state-progress: '#5B8CFF'
  state-success: '#4DD8A4'
  state-warning: '#F2B441'
  state-danger: '#F2545B'
  border-hairline: '#282C37'
  border-strong: '#3A3F4D'
  surface-base-light: '#F7F8FA'
  surface-raised-light: '#FFFFFF'
  ink-primary-light: '#14161C'
  ink-secondary-light: '#5A6172'
  accent-lan-light: '#127A57'
  accent-action-light: '#2F5FE0'
  border-hairline-light: '#E2E5EB'
  state-success-light: '#127A57'
  state-warning-light: '#8A5A00'
  state-danger-light: '#C0262E'
typography:
  display:
    fontFamily: 'Inter'
    fontSize: 32px
    fontWeight: '700'
    lineHeight: '1.15'
    letterSpacing: -0.02em
  headline:
    fontFamily: 'Inter'
    fontSize: 22px
    fontWeight: '600'
    lineHeight: '1.25'
    letterSpacing: -0.01em
  title:
    fontFamily: 'Inter'
    fontSize: 17px
    fontWeight: '600'
    lineHeight: '1.3'
  body:
    fontFamily: 'Inter'
    fontSize: 14px
    fontWeight: '400'
    lineHeight: '1.5'
  body-strong:
    fontFamily: 'Inter'
    fontSize: 14px
    fontWeight: '600'
    lineHeight: '1.5'
  label:
    fontFamily: 'Inter'
    fontSize: 12px
    fontWeight: '500'
    lineHeight: '1.35'
  meta:
    fontFamily: 'Inter'
    fontSize: 11px
    fontWeight: '500'
    lineHeight: '1.35'
  mono:
    fontFamily: 'Cascadia Mono'
    fontSize: 12px
    fontWeight: '400'
    lineHeight: '1.45'
  size:
    fontFamily: 'Cascadia Mono'
    fontSize: 12px
    fontWeight: '400'
    lineHeight: '1.45'
  percent:
    fontFamily: 'Cascadia Mono'
    fontSize: 12px
    fontWeight: '400'
    lineHeight: '1.45'
rounded:
  sm: 4px
  md: 8px
  lg: 12px
  xl: 16px
  full: 9999px
spacing:
  '1': 4px
  '2': 8px
  '3': 12px
  '4': 16px
  '5': 24px
  '6': 32px
  '7': 48px
  gutter: 24px
  panel: 16px
  card: 20px
components:
  button-primary:
    background: '{colors.accent-action}'
    foreground: '{colors.accent-action-ink}'
    radius: '{rounded.md}'
    height: 36px
    paddingHorizontal: '{spacing.4}'
  button-secondary:
    background: '{colors.surface-overlay}'
    foreground: '{colors.ink-primary}'
    radius: '{rounded.md}'
    height: 36px
    paddingHorizontal: '{spacing.4}'
    border: '1px solid {colors.border-strong}'
  button-danger:
    background: '{colors.state-danger}'
    foreground: '{colors.surface-base}'
    radius: '{rounded.md}'
    height: 36px
    paddingHorizontal: '{spacing.4}'
  button-ghost:
    background: 'transparent'
    foreground: '{colors.ink-secondary}'
    radius: '{rounded.md}'
    height: 32px
  card-game:
    background: '{colors.surface-raised}'
    radius: '{rounded.lg}'
    padding: '{spacing.card}'
    border: '1px solid {colors.border-hairline}'
  panel-connection:
    background: '{colors.surface-raised}'
    radius: '{rounded.lg}'
    padding: '{spacing.panel}'
    border: '1px solid {colors.border-hairline}'
  status-badge:
    radius: '{rounded.full}'
    paddingHorizontal: '{spacing.2}'
    paddingVertical: '{spacing.1}'
  progress-bar:
    background: '{colors.surface-sunken}'
    radius: '{rounded.full}'
    height: 6px
    fill: '{colors.state-progress}'
  progress-bar-success:
    background: '{colors.surface-sunken}'
    radius: '{rounded.full}'
    height: 6px
    fill: '{colors.state-success}'
  field-input:
    background: '{colors.surface-sunken}'
    foreground: '{colors.ink-primary}'
    radius: '{rounded.md}'
    border: '1px solid {colors.border-strong}'
    height: 36px
    paddingHorizontal: '{spacing.3}'
  field-input-focus:
    background: '{colors.surface-sunken}'
    foreground: '{colors.ink-primary}'
    radius: '{rounded.md}'
    border: '2px solid {colors.accent-action}'
    height: 36px
    paddingHorizontal: '{spacing.3}'
  data-table-row:
    background: '{colors.surface-raised}'
    foreground: '{colors.ink-primary}'
    borderBottom: '1px solid {colors.border-hairline}'
  toast-error:
    background: '{colors.state-danger}'
    foreground: '{colors.surface-base}'
    radius: '{rounded.md}'
  dialog-surface:
    background: '{colors.surface-overlay}'
    radius: '{rounded.lg}'
    padding: '{spacing.card}'
    border: '1px solid {colors.border-strong}'
---

## Marca y estilo

LAN Party Hub es una herramienta de escritorio, no una tienda. El jugador llega a una LAN con su
portátil, quiere un juego íntegro y ejecutable en el menor tiempo posible, y todo lo que se le pide
hacer debe ser evidente sin instrucciones. Esa urgencia —no la coletilla comercial— fija el tono visual:
sobrio, técnico, con la información de estado siempre a la vista y sin nada que compita por la
atención.

La postura estética es **consola de operaciones silenciosa**. Fondo oscuro por defecto, porque la
aplicación convive con una partida en marcha y un brillo alto en una sala a oscuras resulta molesto.
Dos colores con significado y ninguno decorativo: el verde **verde LAN** marca "esto va bien y está
listo" —servidor conectado, descarga completa, instalación verificada, juego en ejecución— y el azul
**azul acción** marca "esto es lo que puedes pulsar ahora". Todo lo demás es neutral tipográfico.

El error es el único momento en el que el color grita, y lo hace una vez, con nombre y causa. La
regla de oro del producto: **el jugador nunca debería tener que preguntar en qué estado está una
operación**. Barra de progreso, etiqueta de estado y registro de errores son parte del contrato
visual, no adornos.

Anti-referencia explícita: nada de carruseles de ofertas, nada de insignias de progreso social, nada
de gamificación del descargado, nada de confeti. LAN Party Hub no compite por el tiempo del
jugador; le devuelve el tiempo que le costaba la logística.

## Colores

La paleta es deliberadamente corta: dos superficies oscuras, tres niveles de tinta, dos acentos con
función y cuatro colores de estado. Todo lo demás se resuelve con escala de grises.

- **Superficie base (`#12141A`)** — el lienzo de la aplicación. Anclaje visual de toda la app.
- **Superficie elevada (`#1B1E26`)** — tarjetas, paneles y filas de tabla. Es donde vive el
  contenido; la diferencia con la base es tonal, nunca una sombra.
- **Superficie superpuesta (`#232733`)** — menús, diálogos y tooltips. Un peldaño por encima de la
  elevada.
- **Superficie hundida (`#0D0F13`)** — campos de texto, barras de progreso vacías y zonas de
  lectura densa. El reverso del contenido.
- **Tinta primaria (`#F2F4F8`)** — texto de título y contenido principal.
- **Tinta secundaria (`#A7AEBD`)** — metadatos, unidades, texto de apoyo. Legible sobre la base en
  contraste AA.
- **Tinta terciaria (`#6E7688`)** — etiquetas deshabilitadas y texto decorativo. Nunca transmite
  información por sí solo.
- **Verde LAN (`#4DD8A4` / `#127A57` en claro)** — el color de "sistema sano". Conexión activa,
  validación superada, descarga completada, instalación verificada, proceso del juego en marcha.
  Significa *disponible y verificado*, nunca "nuevo" ni "recomendado".
- **Azul acción (`#5B8CFF` / `#2F5FE0` en claro)** — el color de la acción principal del contexto:
  descargar, instalar, lanzar, reconectar. Nunca como fondo decorativo ni como estado.
- **Azul progreso (`#5B8CFF`)** — el relleno de la barra de descarga e instalación. Comparte tono con
  la acción a propósito: el progreso es la acción en curso.
- **Verde éxito (`#4DD8A4`)** — fin de operación verificado.
- **Ámbar advertencia (`#F2B441` / `#8A5A00` en claro)** — estado recuperable que exige decisión:
  descarga pausada, servidor temporalmente inalcanzable, versión desactualizada, operación
  privilegiada pendiente de confirmación.
- **Rojo error (`#F2545B` / `#C0262E` en claro)** — fallo consumado. Aparece en la etiqueta del
  elemento fallido y en el aviso, nunca tiñe superficies enteras.
- **Línea de pelo (`#282C37`)** — separador de baja presencia. Un píxel, sin sombra.
- **Borde fuerte (`#3A3F4D`)** — límite de campos de entrada y diálogos, donde sí hace falta separar.

Los colores de estado son **semánticos y cerrados**: cada uno tiene una sola lectura. No se usan
como decoración, ni se mezclan dos meanings en un mismo badge, ni se introduce un tono
intermedio para "casi bien".

Existe además un juego de tokens **-light** para las pocas superficies que lo necesitan (el visor
de logs y el detalle de un juego en modo claro). El modo oscuro es el predeterminado; el claro es una
opción, no una variante de marca.

Evitar: degradados, sombras coloreadas, colores de categoría aplicados a tarjetas, iconos con
significado doble, cualquier tinte que no sea un estado del sistema.

## Tipografía

Una sola familia: **Inter**, en el rango habitual de interfaz de escritorio, con **Cascadia Mono**
reservada para datos que deben alinearse en columna — rutas de instalación, hashes, versiones y
valores numéricos derivados como tamaños y porcentajes. Los tamaños en bytes permanecen en su
forma exacta en los datos literales; la presentación decimal definida en `EXPERIENCE.md` convierte
el valor derivado a GB. La monoespaciada no es un adorno técnico: es una promesa de que el dato es
literal.

- **`display` (32/700)** — el titular de una sola vez por superficie: el nombre del juego en la
  pantalla de detalle, el nombre del servidor en el panel de conexión. Su rareza es intencionada.
- **`headline` (22/600)** — títulos de sección y encabezados de panel.
- **`title` (17/600)** — nombre de un videojuego en una fila o tarjeta.
- **`body` (14/400)** — texto de lectura: sinopsis, mensajes de error, descripciones de estado.
- **`body-strong` (14/600)** — el dato que el usuario vino a ver: tamaño del paquete, versión
  requerida, nombre del directorio.
- **`label` (12/500)** — etiquetas de campo, encabezados de columna, texto de botón.
- **`meta` (11/500)** — unidades y metadatos: "GB", "de 4", "hace 2 min".
- **`mono` (12/400)** — rutas, hashes, versiones exactas, identificadores de paquete.
- **`size` (12/400)** — valor numérico decimal derivado del tamaño, como «7,6»; la unidad «GB»
  permanece en `meta`.
- **`percent` (12/400)** — valor entero derivado del progreso, como «42»; el signo «%» permanece en
  `meta`.

Reglas: el interlineado es cerrado y sin adornos; nunca se usa texto justificado; el tamaño de
etiqueta nunca baja de 11 px; los números que se comparan entre sí (tamaños, versiones) se alinean
con la familia monoespaciada; los títulos de tarjeta se truncan a una línea con elipsis, nunca se
envuelven a tres.

## Disposición y espaciado

Escala base de 4 px con pasos en 8, 12, 16, 24, 32 y 48. `{spacing.gutter}` (24 px) separa las
columnas de la aplicación; `{spacing.panel}` (16 px) el contenido interno de un panel; `{spacing.card}`
(20 px) el contenido de una tarjeta o diálogo.

**Cliente.** Columna izquierda fija de navegación (Catálogo, Descargas, Biblioteca, Configuración),
columna central de contenido y panel derecho contextual que muestra el estado de conexión con el
servidor y el estado de la operación en curso. El panel derecho es persistente: es donde vive la
promesa del producto, que el estado siempre está a la vista. La navegación no es una barra inferior
—el flujo principal es biblioteca → descarga → biblioteca, no un conjunto de tareas paralelas— sino una
columna que deja el ancho completo a las carátulas.

**Servidor.** Columna izquierda de navegación (Catálogo, Clientes, Paquetes) y área principal. El
panel de estado de clientes es una tabla densa, no una colección de tarjetas: el organizador necesita
comparar equipos, y comparar requiere alineación.

**Rejilla de catálogo.** Rejilla fluida de tarjetas de carátula, mínimo de 3 columnas a 1280 px y
hasta 6, con la última fila alineada a la izquierda. El ancho de tarjeta se recalcula con la ventana;
las columnas nunca se estiran para rellenar. Cuando el ancho no permite 3 columnas, la rejilla pasa
a 2; por debajo de 2, a lista de una columna.

**Capa modal.** Un nivel. Un diálogo nunca se apila sobre otro diálogo. Las hojas de detalle
(confirmación de operación privilegiada, resumen de error) son modales de una sola capa.

## Elevación y profundidad

La profundidad se expresa **tonalmente, no con sombras**. Tres niveles de superficie (base,
elevada, superpuesta) y una línea de pelo para separar. Ninguna sombra proyectada salvo dos
excepciones acotadas, ambas de la familia `surface-sunken` al 40 % de opacidad: el diálogo modal
sobre el contenido y el menú desplegable. Sirven para indicar "esto flota por encima", no para
crear jerarquía.

**Nunca** se usa sombra como indicador de hover. El hover se marca con un cambio de tono de la
superficie elevada a la superpuesta, o un cambio de tono del texto.

## Formas

El radio de esquina es un acto de contención: 8 px para controles, 12 px para tarjetas y paneles,
16 px para diálogos, 4 px para campos de texto pequeños y etiquetas. `full` queda reservado para
píldoras de estado —cápsulas compactas de una línea, alto 20 px— y para el punto de presencia de
conexión, que es un círculo de 8 px.

Nada de superficies en cápsula. Una tarjeta de juego con esquinas redondeadas a 40 px lee como
juguete; el producto es una herramienta y las herramientas no son redondeadas hasta el exceso.
La imagen de carátula respeta exactamente las esquinas de su contenedor.

## Componentes

Especificación visual. El comportamiento de cada componente vive en `EXPERIENCE.md`
(Patrones de componente).

- **Botón primario** — relleno `{colors.accent-action}` sobre `{colors.accent-action-ink}`, radio
  `{rounded.md}`, alto 36 px, padding horizontal 16 px. Una sola acción primaria por superficie. En
  estado deshabilitado baja a `{colors.ink-disabled}` sobre `{colors.surface-overlay}` y conserva
  el contraste del texto pero pierde la marca de acción.
- **Botón secundario** — relleno `{colors.surface-overlay}`, texto `{colors.ink-primary}`, borde
  1 px `{colors.border-strong}`. Supporta la acción principal sin competir.
- **Botón de peligro** — relleno `{colors.state-danger}`. Reservado a acciones destructivas
  inequívocas: cancelar una descarga, eliminar una instalación, descartar un error. Nunca para
  navegación.
- **Botón fantasma** — sin relleno, texto `{colors.ink-secondary}`. Acciones terciarias: descartar
  un aviso, cerrar un panel.
- **Tarjeta de juego** — `{colors.surface-raised}`, radio 12 px, padding 20 px, borde de 1 px
  `{colors.border-hairline}`. Contiene carátula 3:2, `title`, y una línea de `meta` con versión,
  tamaño y número de jugadores. El borde no se refuerza en hover; la superficie sí pasa a
  `{colors.surface-overlay}`.
- **Panel de conexión** — `{colors.surface-raised}`, radio 12 px, padding 16 px. Cabecera con nombre
  del servidor y punto de presencia; cuerpo con el estado de la operación en curso.
- **Insignia de estado** — cápsula de 20 px de alto, radio `full`, padding 8 px horizontal. Fondo
  `surface-overlay` con texto `{colors.ink-secondary}` para estados neutros; fondo del color de
  estado al 18 % de opacidad con texto del color de estado para activo, correcto, advertencia y
  error. El color solo se usa con su significado canónico.
- **Barra de progreso** — pista `{colors.surface-sunken}` de 6 px, radio `full`, relleno
  `{colors.state-progress}`. En operación completada y verificada el relleno pasa a
  `{colors.state-success}`. En error, el relleno se conserva en su longitud real y el estado lo
  comunica la insignia: una barra al 100 % en rojo mentiría sobre lo ocurrido. La forma del texto
  que acompaña a la barra (porcentaje entero y par de tamaños en GB decimales) no se decide aquí:
  es la regla única de tamaños y progreso de `EXPERIENCE.md`, a la que esta especificación
  remite. Este documento solo fija la forma visual de la barra.
- **Campo de entrada** — fondo `{colors.surface-sunken}`, texto `{colors.ink-primary}`, borde 1 px
  `{colors.border-strong}`, radio 8 px, alto 36 px. En foco, el borde pasa a 2 px
  `{colors.accent-action}` y el alto se mantiene en 36 px: el foco no deforma la rejilla.
- **Fila de tabla** — fondo `{colors.surface-raised}`, texto `{colors.ink-primary}`, separador
  inferior de 1 px `{colors.border-hairline}`. La tabla del servidor es densa: alto de fila 40 px,
  tipografía `body-strong` para las celdas de dato y `meta` para las unidades.
- **Aviso de error** — barra de superficie `{colors.state-danger}` con texto
  `{colors.surface-base}`, radio 8 px. Contenido: causa, acción intentada y siguiente paso. Sin
  iconografía; el texto es el aviso.
- **Superficie de diálogo** — `{colors.surface-overlay}`, radio 12 px, padding 20 px, borde 1 px
  `{colors.border-strong}`. Encabezado `headline`, cuerpo `body`, pie con acciones alineadas a la
  derecha.

## Qué hacer y qué no hacer

| Haz | No hagas |
|---|---|
| Usa el verde LAN solo para estado sano y verificado | Uses el verde como color de marca decorativo, de etiqueta o de fondo de sección |
| Usa el azul acción solo para la acción disponible en ese momento | Uses el azul acción en etiquetas de estado o en cabeceras |
| Muestra el progreso real, aunque la operación haya fallado al final | Pintas la barra al 100 % en rojo o la ocultas en error |
| Almacena y alinea en monoespaciada rutas, hashes, versiones y valores numéricos derivados | Uses la monoespaciada para frases o titulares |
| Diferencia superficies por tono, con línea de pelo para separar | Uses sombras proyectadas para construir jerarquía |
| Muestra el estado del servidor y de la operación en un panel persistente | Escondas el estado de conexión en un menú o en un modal |
| Un nivel de modal, sin apilar diálogos | Apilas un diálogo sobre otro diálogo |
| Di el error con su causa y su siguiente paso en texto | Muestres el error solo con icono, con código o con un número |
| Trunca a una línea el nombre de un juego en la tarjeta | Envuelvas el nombre a tres líneas y rompas la rejilla |
| Deja espacio en blanco alrededor de la rejilla | Estires las tarjetas para rellenar la fila |
| Usa la tipografía de plataforma para tamaño de ventana, zoom y escalado del SO | Reimplementes el escalado del sistema con CSS o factores propios |
