---
title: Product Brief — LAN Party Hub
status: approved
created: 2026-10-26
updated: 2026-10-26
phase: "Fase A — Descubrimiento y Product Brief"
checkpoint: "Checkpoint A"
checkpoint_status: approved
source: docs/plan.md
---

# Product Brief: LAN Party Hub

## Resumen ejecutivo

LAN Party Hub es una plataforma de escritorio que gestiona, distribuye, instala, actualiza y
ejecuta videojuegos dentro de una red local de LAN party. El producto se compone de dos
aplicaciones: un **servidor** que gobierna el catálogo de videojuegos, los paquetes y el estado
de la red, y un **cliente** que se instala en cada equipo del evento y consume ese catálogo.

El problema que resuelve no es técnico sino organizativo: en una LAN party de 8 a 20 personas,
el reparto de los juegos se resuelve hoy con USB, carpetas compartidas improvisadas o un
servidor de descargas del que todos tiran a la vez. Cada una de esas vías falla de forma
diferente, y ninguna deja registro de qué tiene instalado cada equipo ni permite reponer un
juego a medio descargar. El coste no es solo de tiempo: es el tiempo de configuración del evento
—la franja de la tarde en la que la LAN todavía no está jugable—.

La propuesta es convertir ese repositorio improvisado en un producto con la experiencia de una
biblioteca de juegos moderna, pero optimizada para el contexto LAN: descubrimiento automático
del servidor, catálogo sincronizado, descarga con verificación de integridad, instalación con
mínima intervención y ejecución desde la misma interfaz. Todo funciona dentro de la red local,
sin dependencia obligatoria de Internet y sin infraestructura cloud.

Este brief documenta el alcance del MVP, sus límites y los puntos todavía no resueltos. No
cierra decisiones técnicas: las ambigüedades vivas están registradas en la sección
[Preguntas abiertas, riesgos y decisiones pendientes](#preguntas-abiertas-riesgos-y-decisiones-pendientes)
con su fase o checkpoint propietario.

## El problema

En un evento LAN, el camino habitual entre "¿qué hay que jugar?" y "estamos jugando" es un
proceso manual y frágil:

- **El catálogo vive en la cabeza de una persona.** El organizador conoce qué juegos hay; el
  resto lo descubre preguntando o mirando un cartel.
- **El reparto de archivos no escala.** Un pendrive obliga a pasar equipo por equipo; una
  carpeta compartida satura el enlace y no permite pausar ni reanudar.
- **Nadie sabe qué hay instalado.** Cuando alguien falla al arrancar un juego, el primer
  diagnóstico es "¿lo tienes instalado?", y la respuesta suele ser "creo que sí".
- **Una descarga interrumpida se pierde.** Sin verificación de integridad ni persistencia del
  progreso, un corte de red o un reinicio del cliente significa empezar de cero con varios
  gigabytes.

El status quo no es gratuito: concentra el trabajo de logística en el organizador, consume la
ventana de tiempo previa al evento y produce fallos de última hora que no tienen forma de
diagnosticarse.

## La solución

Un servidor central y un cliente de escritorio que trabajan juntos sobre la red local:

- **Catálogo centralizado.** El servidor registra los juegos con sus metadatos completos
  (versión, tamaño, jugadores mínimos y máximos, sistema operativo, tipo de distribución e
  instalación, configuración de lanzamiento, dependencias, licencia y restricciones). El
  cliente lo sincroniza automáticamente y permite buscar y filtrar.
- **Descubrimiento y conexión.** El cliente localiza el servidor en la red local por sí solo y
  además admite la dirección introducida a mano, para redes donde el descubrimiento automático
  no funcione. Ante una desconexión, reconecta y sincroniza lo pendiente.
- **Distribución eficiente.** El servidor actúa como repositorio inicial y coordinador, y los
  clientes intercambian fragmentos entre sí para no saturarlo. Las descargas se pausan,
  reanudan y sobreviven a reinicios.
- **Instalación con mínima intervención.** Cada juego tiene un manifiesto de instalación
  versionado. Los manifiestos no ejecutan comandos arbitrarios: sólo operaciones tipadas, con
  confirmación explícita para lo privilegiado.
- **Ejecución y supervisión.** El cliente detecta el ejecutable, valida la instalación, aplica
  los argumentos de lanzamiento y muestra errores comprensibles. El servidor ve en tiempo real
  qué equipos están conectados, qué tienen instalado y en qué estado van sus descargas.

## A quién sirve

**El jugador de LAN.** Llega con su portátil y su disco con lo que ya tenía. Quiere que el
juego esté disponible, instalado y ejecutable con el mínimo de pasos, sin curva de aprendizaje y sin que le pidan buscar un USB. Para él, el producto se reduce a una biblioteca
familiar: busca, descarga, juega.

**El organizador del evento.** Es quien decide qué juegos se necesitan y quién entra. Necesita
saber en qué estado está cada equipo sin preguntar uno por uno, confiar en que los archivos
distribuidos no están corruptos y poder lanzar la sesión sin estar resolviendo bloqueos
técnicos durante la partida.

**El administrador del servidor.** Persona o equipo que mantiene el catálogo, publica los
paquetes y hace cumplir las licencias de distribución. Necesita control sobre qué se publica y
evidencia de que lo que se sirve es lo que se aprueba.

## Criterios de éxito

El MVP se considera exitoso si permite demostrar, con instrucciones reproducibles, un flujo
completo en una LAN real:

1. Arrancar el servidor.
2. Conectar dos o más clientes.
3. Sincronizar el catálogo.
4. Mostrar un videojuego disponible.
5. Descargar su paquete.
6. Validar su integridad.
7. Instalarlo en modo portable.
8. Detectar que ya está instalado.
9. Ejecutarlo desde la interfaz.
10. Mostrar su estado en el servidor.

Criterio transversal, tan importante como el flujo: **ningún jugador necesita Internet** para
cualquiera de los pasos anteriores, y **el mismo paquete debe llegar íntegro** a todos los
destinatarios.

## Alcance del MVP

**Entra en el MVP:**

- Servidor con catálogo de videojuegos y administración básica.
- Cliente de escritorio con descubrimiento del servidor (y dirección manual como alternativa),
  catálogo sincronizado, búsqueda y filtrado.
- Descarga desde el servidor con verificación de integridad, pausa, reanudación y persistencia
  del progreso ante reinicio.
- Instalación en modo **portable** únicamente, mediante manifiesto versionado con operaciones
  tipadas.
- Lanzamiento de juegos desde el cliente con detección del ejecutable y errores comprensibles.
- Panel de estado en el servidor: equipos conectados, juegos instalados, descargas en curso y
  errores.
- Funcionamiento íntegro en LAN local, sin dependencia de Internet.

**Queda fuera del MVP (planificado para fases posteriores):**

- Modo gestionado de preparación centralizada de sesiones (RF-08); en el MVP cada jugador
  elige sus propios juegos.
- Instalación en modos **Installer** y **External**.
- Actualizaciones diferenciales avanzadas (el MVP resuelve versiones completas).
- Intercambio P2P real entre clientes como camino principal de descarga; el MVP demuestra el
  flujo completo serviendo desde el servidor, y el intercambio entre peers se incorpora y
  verifica después.
- Soporte multiplataforma más allá de Windows 10/11.
- Conexión directa del juego a servidores de juego (el diseño debe dejarla abierta, no
  implementarla).

## Preguntas abiertas, riesgos y decisiones pendientes

Esta sección es deliberadamente viva: recoge lo que la Fase A **no** ha decidido y que sí
afectará al diseño. Cada asunto tiene un propietario explícito. Nada aquí presupone una
alternativa concreta salvo donde el plan maestro ya la propone como candidato.

### R3 — Mecanismo de descubrimiento del servidor

**Estado:** pregunta abierta · **Decisión requerida en:** Checkpoint A · **Validación técnica en:** Fase D

El plan maestro evalúa mDNS u otros mecanismos apropiados, y la interfaz debe permitir
configurar manualmente la dirección del servidor. Queda por decidir si el descubrimiento
automático es mDNS, un mecanismo distinto o una combinación con la dirección manual como
vía de respaldo permanente. Impacta el contrato de red, el comportamiento en redes con
políticas restrictivas y el modo en que el usuario recupera el control cuando el
descubrimiento falla.

### R4 — Autenticación de cliente

**Estado:** pregunta abierta · **Decisión requerida en:** Checkpoint A · **Detalle funcional en:** Fase B · **Mecanismo en:** Fase D

No está definido cómo se autentica un cliente ante el servidor, ni qué ocurre cuando un
equipo no autorizado se une a la LAN. Sin esta decisión, "supervisar la red" y "distribuir de
forma segura" quedan sin un sujeto identificado. Influye en el alta de equipos, en el
rendimiento del descubrimiento y en la experiencia de instalación del cliente.

### R5 — Raíz de confianza y firma de paquetes

**Estado:** pregunta abierta y riesgo abierto · **Decisión requerida en:** Checkpoint A ·
**Decisión de diseño en:** Fase D

Los requisitos de integridad y autenticidad de paquetes (RF-04, RF-07) no tienen aún un
ancla de confianza: no se ha definido la raíz desde la que se valida la firma de un paquete,
ni cómo se distribuye y actualiza esa raíz, ni qué ocurre si el servidor y los clientes no
comparten la misma. **Este brief no fija ninguna alternativa**: la elección se toma en el
Checkpoint A, con la decisión de mecanismo formalizada en la Fase D.

- Riesgo asociado: sin ancla de confianza definida, la verificación de integridad sólo puede
  ser un checksum —detecta corrupción, no autenticidad— y el requisito explícito de
  "integridad y autenticidad de paquetes" quedaría sin cobertura verificable. Impacta
  directamente en el significado del criterio de éxito "el mismo paquete llega íntegro a todos
  los destinatarios".

### P2P — Alcance de la prueba de concepto y ambigüedad de la distribución

**Estado:** riesgo y pregunta abierta · **Decisión requerida en:** Checkpoint A ·
**Ejecución de la PoC en:** Fase D

El plan maestro propone BitTorrent con tracker local, con MonoTorrent como biblioteca
candidata, y a la vez exige una prueba de concepto antes de asumir que esa es la solución
definitiva. Dos ambigüedades quedan sin resolver: **(a)** el contenido y los límites de esa
prueba de concepto —qué debe demostrar para que la decisión de arquitectura pueda tomarse — y
**(b)** si la PoC es bloqueante para el MVP o si el MVP puede cerrarse sobre el camino
servidor-cliente mientras el intercambio entre peers se valida en paralelo.

Como mitigación de riesgo de acoplamiento ya prevista en el plan maestro, la transferencia se
diseñará detrás de una abstracción que permita más de un mecanismo (por ejemplo, HTTP y
BitTorrent) sin atar el dominio a una biblioteca concreta. Esa abstracción es una decisión de
arquitectura y se formaliza en la Fase D, no aquí.

### Riesgos adicionales registrados en Fase A

| Riesgo | Descripción | Dueño |
| --- | --- | --- |
| R-01 | Objetivo de 8-20 jugadores concurrentes en 1 Gbps asumido, no validado; sin pruebas de rendimiento no se puede prometer velocidad de transferencia. | Fase D |
| R-02 | Paquetes de varios GB con reanudación: la persistencia del progreso y la reanudación son el punto más frágil del MVP. | Fase D / Fase F |
| R-03 | Descubrimiento y reconexión en redes con políticas restrictivas o segmentadas. | Fase D |
| R-04 | Modos de instalación Installer y External requieren control de procesos privilegiados y plataformas externas; sus exigencias se posponen, no se descartan. | Fase posterior al MVP |
| R-05 | Publicación de paquetes conforme a sus licencias: la distribución de material con licencia restrictiva puede limitar qué juegos se pueden publicar. | Fase B (restricciones) / Fase D |

## Visión

Si LAN Party Hub funciona, deja de ser "el servidor de la LAN de este evento" y pasa a ser
**la forma estándar de preparar una LAN party**: el organizador publica un catálogo una vez,
cada jugador llega y encuentra sus juegos listos, y el tiempo de la tarde deja de consumirse
en logística.

La visión a largo plazo es una capa de distribución local que rivalice en la experiencia con
las plataformas de lanzamiento que hoy resuelven parte de este problema, pero funcionando
completamente dentro de la red del evento y sin las ataduras de un servicio en la nube.
El siguiente paso natural, ya fuera del MVP, es la gestión centralizada de sesiones: el
organizador decide el reparto y el sistema lo propone a cada equipo respetando sus permisos y
confirmaciones.

## Sobre este documento

- **Fuente:** `docs/plan.md` (prompt maestro de desarrollo con BMAD), commit `ab9a3b7`.
- **Fase:** A — Descubrimiento y Product Brief. **Estado:** `approved` — Checkpoint A aprobado
  explícitamente por el usuario el 2026-10-26. La aprobación autoriza iniciar la Fase B (PRD)
  y no resuelve por sí sola las decisiones R3, R4, R5 y P2P: cada una queda registrada en el
  PRD con su responsable y fase de resolución.
- **Siguiente checkpoint:** Checkpoint B — validación del PRD. R3, R4, R5 y P2P deben
  resolverse o quedar explícitamente asignadas a una fase posterior y a un responsable en el
  PRD, sin inventar decisiones en Fase B.
- **Alcance de esta fase:** brief aprobado y Fase B (PRD). Quedan fuera de esta ejecución UX,
  arquitectura, ADRs, backlog de historias, scaffold y código. Este brief es la referencia de
  descubrimiento; el PRD será la referencia de requisitos de producto.
