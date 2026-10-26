# Reconciliación del Product Brief — LAN Party Hub

## Alcance

Se reconcilia el Product Brief aprobado (`briefs/brief-lan-party-hub-2026-10-26/brief.md`) con `DESIGN.md` y `EXPERIENCE.md`. El PRD aprobado prevalece cuando existe mayor precisión; el Brief aporta contexto de producto y restricciones que no contradicen el PRD.

## Problema y promesa de producto

| Necesidad del Brief | Respuesta UX |
|---|---|
| Sustituir USB y carpetas compartidas improvisadas | Catálogo centralizado, descarga con progreso y pausa/reanudación |
| Saber qué hay instalado sin preguntar | Panel persistente en cliente y tabla de estado en servidor |
| Evitar perder descargas interrumpidas | Progreso real persistido, reanudación y flujo transversal de recuperación |
| Evitar curva de aprendizaje | Superficies familiares de catálogo/biblioteca, microcopia directa, teclado completo |
| Devolver tiempo al jugador | Estado siempre visible, errores accionables y confirmación solo cuando hay trabajo que perder |

## Usuarios y necesidades

| Usuario del Brief | Superficie / flujo UX | Resultado verificable |
|---|---|---|
| Jugador de LAN | Ana; Conexión → Catálogo → Ficha → Descargas → Biblioteca | Encuentra, descarga, instala y lanza sin Internet |
| Organizador del evento | Luis; Clientes del servidor | Diagnostica estado y error sin preguntar individualmente |
| Administrador del servidor | Marta; Catálogo y Paquetes | Publica con metadatos, licencia y restricciones completas |

## Criterio de éxito del Brief

Los diez pasos del criterio de éxito están representados de forma explícita: arranque del servidor, conexión de dos o más clientes, sincronización, catálogo, descarga, validación, instalación portable, detección de instalación existente, ejecución y estado en servidor. El requisito transversal se cubre: la interfaz no depende de Internet y el cliente muestra estados de integridad y confianza antes de instalar.

## MVP

- Servidor con catálogo, paquetes y estado: cubierto.
- Cliente con descubrimiento, dirección manual, catálogo, búsqueda y filtrado: cubierto.
- Descarga servidor-cliente con integridad, pausa, reanudación y persistencia: cubierto.
- Instalación portable con manifiesto y operaciones tipadas: cubierto.
- Lanzamiento y errores comprensibles: cubierto.
- Funcionamiento íntegro en LAN local: cubierto.

## Fuera del MVP preservado

No aparecen en la interfaz modos Installer/External, gestión centralizada de sesiones, actualización diferencial, conexión directa del juego a servidores, launcher externo, cloud obligatorio, autenticación/autorización de clientes ni soporte multiplataforma adicional. El intercambio P2P no se presenta como capacidad disponible.

## Ajustes de cierre de Fase C (contratos UX-C-001/002/003)

Refinan la promesa de producto sin cambiar su alcance: el jugador ve por qué una instalación no
puede jugarse y siempre tiene una acción primaria; el organizador diagnostica sin preguntar.

| Contrato | Precisión introducida | Lectura desde el Brief |
|---|---|---|
| UX-C-001 | Estados de instalación validados/inválidos como contrato de comportamiento, con tabla propia en `EXPERIENCE.md`, aplicada por igual en Biblioteca y Ficha | «Saber qué hay instalado sin preguntar» — el estado es inequívoco en la propia superficie |
| UX-C-002 | Motivo del bloqueo visible junto a la etiqueta; acción primaria «Reparar» o «Descargar de nuevo»; sin segunda vía de lanzamiento | «Devolver tiempo al jugador» — sin callejones sin salida ni acciones ambiguas |
| UX-C-003 | Cobertura de teclado (`Tab`/`Shift+Tab`, `Enter`, `Espacio`, `Ctrl+4`) y criterio de aceptación verificable para lector de pantalla | «Evitar curva de aprendizaje» — teclado completo y foco visible en el control bloqueado |

Regla única de tamaños y progreso (canónica en `EXPERIENCE.md`, referenciada desde `DESIGN.md`): GB
decimales (10⁹ bytes), separador decimal español, exactamente un decimal, porcentaje entero y par
«<n> % · <a> GB de <t> GB». Unifica cómo se muestra el tamaño de los paquetes y el progreso de la
descarga, reinforcing la coherencia del estado visible.

## Preguntas abiertas y riesgos reconciliados

| ID | Brief | UX | Resultado |
|---|---|---|---|
| R3 | Mecanismo de descubrimiento pendiente | Dos vías visibles; mecanismo no elegido | Abierta, Fase D |
| R4 | Compatibilidad de versiones; sin autenticación | Diagnóstico de versión y bloqueo de operaciones | Cerrada en alcance |
| R5 | Raíz y firma pendientes | Estados diferenciados sin fijar mecanismo | Abierta, Fase D |
| P2P | PoC y abstracción pendientes | MVP solo desde servidor | Abierta, Fase D |
| R-01 | 8–20 jugadores no validados | Sin promesa de rendimiento | Hipótesis, Fase D |
| R-02 | Paquetes GB y reanudación frágil | Persistencia/reanudación observable | Validación Fase D/F |
| R-03 | Redes restrictivas | Dirección manual y reconexión degradada | Validación Fase D |
| R-04 | Installer/External pospuestos | No emitidos en UX | Fuera de MVP |
| R-05 | Licencias restrictivas | Publicación bloqueada sin datos | Restricción cerrada; mecanismo Fase D |

## Ideas cualitativas aceptadas u omitidas

Aceptadas y convertidas en decisiones: biblioteca familiar, foco en estado visible, mínima intervención, ausencia de gamificación, indoors sin dependencias externas. Omitidas por ser prospectivas y ajenas a un contrato de MVP: estandarización futura del producto, gestión centralizada de sesiones y visión competitiva a largo plazo.
