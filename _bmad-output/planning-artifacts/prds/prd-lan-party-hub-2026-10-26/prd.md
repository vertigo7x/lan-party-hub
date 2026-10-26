---
title: PRD — LAN Party Hub
status: final
created: 2026-10-26
updated: 2026-10-26
phase: "Fase B — Product Requirements Document"
checkpoint: "Checkpoint B"
checkpoint_status: ready-for-review
source: docs/plan.md
product_brief: ../briefs/brief-lan-party-hub-2026-10-26/brief.md
---

# PRD: LAN Party Hub

## 0. Propósito del documento

Este PRD define los requisitos de producto del MVP de LAN Party Hub para los equipos de producto,
UX, arquitectura e ingeniería y para los flujos posteriores de BMAD. Se basa en el Product Brief
aprobado (`../briefs/brief-lan-party-hub-2026-10-26/brief.md`) y en `docs/plan.md`, y traduce el
alcance-discovery en requisitos funcionales y no funcionales verificables sin cerrar decisiones
técnicas que corresponden a fases posteriores. Las decisiones aún abiertas se identifican como
tales y conservan responsable y fase de resolución.

## 1. Visión

LAN Party Hub es una plataforma de escritorio que permite preparar una LAN party sin depender de
Internet ni de la logística manual de repartir archivos. Un servidor de evento administra el
catálogo, los paquetes y el estado de los equipos; un cliente por equipo permite descubrir el
servidor, obtener los juegos, instalarlos en modo portable y ejecutarlos.

El MVP se centrará en demostrar de extremo a extremo el flujo principal con dos o más clientes
en una LAN real. La visión posterior incorpora intercambio P2P real entre clientes y gestión
centralizada de sesiones, pero el MVP no debe bloquearse por esas capacidades.

## 2. Usuarios objetivo

### 2.1 Trabajos por completar

- **Jugador de LAN:** encontrar un juego disponible, obtenerlo íntegro, instalarlo con una
  intervención mínima y ejecutarlo sin depender de Internet.
- **Organizador del evento:** saber qué está instalado y qué está fallando sin preguntar
  individualmente a cada equipo.
- **Administrador del servidor:** publicar únicamente contenido permitido y controlar qué juegos
  forman parte del catálogo.

### 2.2 Recorridos clave

- **UJ-1. La jugadora llega con su portátil.** Ana entra en la LAN, el cliente encuentra o
  acepta la dirección del servidor, sincroniza el catálogo, descarga un paquete, lo instala en
  modo portable y lo ejecuta; el servidor refleja el estado final.
- **UJ-2. El organizador diagnostica una incidencia.** Luis ve en el servidor un equipo
  desconectado o una descarga con error, identifica el estado y decide si debe pedir una
  reintención o asistencia.
- **UJ-3. El administrador publica el catálogo.** Marta registra un videojuego, sus metadatos,
  su paquete y las restricciones de distribución antes de ofrecerlo a los clientes.

## 3. Glosario

- **Servidor** — Aplicación central del evento que administra el catálogo, los paquetes y el estado
  de los clientes.
- **Cliente** — Aplicación de escritorio instalada en cada equipo participante.
- **Catálogo** — Conjunto administrable de videojuegos y sus metadatos visibles para el cliente.
- **Paquete** — Archivo o conjunto de archivos distribuibles de una versión de videojuego. Un
  paquete representa una versión o distribución de un videojuego del catálogo.
- **Manifiesto de instalación** — Documento versionado que declara operaciones de instalación
  tipadas para un paquete.
- **Operación tipada** — Acción cerrada y validada permitida por un manifiesto; no equivale a un
  comando arbitrario.
- **Modo portable** — Modalidad única de instalación del MVP: extracción de archivos en un
  directorio configurable, sin instalador.
- **Estado de cliente** — Información que el servidor presenta sobre conexión, versiones,
  instalaciones, descargas y errores de un cliente.
- **Raíz de confianza** — Ancla usada para validar la firma de un paquete y su cadena de confianza.
- **Prueba de concepto P2P** — Spike técnico para validar intercambio entre peers antes de
  confirmar una solución de distribución P2P.

## 4. Funcionalidades

### 4.1 Catálogo y administración

**Descripción:** El servidor administra el catálogo y el cliente lo sincroniza y consulta. El
organizador puede buscar y filtrar juegos por los criterios disponibles. La publicación exige
conocer las restricciones de distribución.

#### FR-1: Administración del catálogo

El administrador del servidor puede registrar y modificar videojuegos con identificador único,
nombre, descripción, categorías, etiquetas, carátula, imágenes, versión, tamaño, jugadores
mínimo y máximo, sistema operativo, tipo de distribución, tipo de instalación, configuración de
lanzamiento, dependencias, licencia y restricciones de distribución.

**Consecuencias verificables:**
- Cada videogameo persistido conserva un identificador único y todos los metadatos obligatorios.
- Un cliente puede distinguir dos registros con el mismo nombre por su identificador y versión.
- Un paquete no puede publicarse si el registro carece de información de licencia o restricciones
  necesaria para comprobar la publicación.

#### FR-2: Sincronización, búsqueda y filtrado del catálogo

El cliente sincroniza automáticamente los cambios del catálogo y permite buscar, filtrar y
categorizar los videogameos disponibles.

**Consecuencias verificables:**
- Tras una sincronización correcta, el cliente refleja el catálogo publicado por el servidor.
- Una búsqueda por nombre y un filtro por categoría devuelven resultados consistentes con el
  catálogo sincronizado.
- Si el servidor no está disponible, el cliente conserva el último catálogo válido y comunica el
  estado de sincronización pendiente.

### 4.2 Descubrimiento, conexión y autenticación

**Descripción:** El cliente se une al servidor de la LAN y sólo puede usar las capacidades
protegidas cuando su identidad ha sido reconocida por el servidor. La conexión debe degradar de
forma segura y permitir recuperar el control si falla el descubrimiento.

#### FR-3: Descubrimiento y conexión

El cliente puede descubrir automáticamente el servidor dentro de la red local y también aceptar
una dirección introducida manualmente. Ante una desconexión, el cliente intenta reconectar y
sincroniza los cambios pendientes.

**Consecuencias verificables:**
- En una LAN donde el mecanismo de descubrimiento esté operativo, el cliente puede localizar el
  servidor sin introducir su dirección.
- El usuario puede establecer una conexión mediante dirección manual cuando el descubrimiento no
  esté disponible o no funcione.
- Tras una interrupción temporal del servidor, el cliente recupera la conexión sin reinstalarse y
  vuelve a sincronizar el catálogo.

**Restricción abierta R3:** el mecanismo concreto (mDNS u otro) y su combinación con la
dirección manual no se decide en este PRD. **Responsable:** arquitectura/producto. **Fase de
resolución:** Fase D, con validación técnica. La dirección manual es obligatoria como alternativa
del MVP; el descubrimiento automático también lo es, pero su implementación queda sujeta a la
decisión técnica documentada.

#### FR-4: Autenticación y autorización de clientes

El servidor debe identificar a los clientes autorizados antes de conceder acceso a las
capacidades protegidas y definir una respuesta segura ante equipos no autorizados.

**Consecuencias verificables:**
- Un cliente no reconocido no puede consultar el catálogo, descargar, instalar ni reportar estado
  como cliente autorizado.
- Un cliente autorizado puede realizar las operaciones del MVP.
- El servidor puede distinguir en su estado de cliente un equipo autorizado de uno no autorizado.

**Restricción abierta R4:** el tipo de credencial, el alta y la revocación de clientes, la
expiración de sesión y la política ante un equipo no autorizado no se inventan en este PRD.
**Responsable:** producto para el contrato funcional y arquitectura para el mecanismo. **Fase de
resolución:** detalle funcional en Fase B/C y mecanismo en Fase D.

### 4.3 Distribución y descargas

**Descripción:** En el MVP, el servidor es el repositorio inicial y coordinador de las descargas.
Los clientes descargan paquetes completos con verificación de integridad, pausa, reanudación y
persistencia del progreso. El intercambio P2P real se valida después mediante una prueba de
concepto.

#### FR-5: Descarga desde el servidor

Un cliente autorizado puede descargar un paquete del servidor, con seguimiento de progreso,
pausa, reanudación y recuperación tras desconexiones o reinicios.

**Consecuencias verificables:**
- El cliente informa progreso de descarga y puede pausar y reanudar la misma descarga.
- Tras reiniciar el cliente durante una descarga, la operación se recupera desde el progreso
  persistido y no obliga a reiniciar el paquete desde cero.
- El servidor no transfiere un paquete a un cliente no autenticado.
- Las descargas simultáneas de varios clientes avanzan de forma independiente y no comparten
  estado de progreso.

#### FR-6: Verificación de integridad y autenticidad de paquetes

El cliente valida la integridad del paquete antes de instalarlo y no continúa la instalación si la
validación falla. El sistema debe distinguir corrupción de autenticidad cuando la raíz de confianza
esté disponible.

**Consecuencias verificables:**
- Un paquete alterado durante la transferencia es rechazado antes de la instalación.
- Un paquete que no puede validarse contra la raíz de confianza configurada se marca como no
  confiable y no se instala.
- El resultado de validación —incluido el fallo y su causa— queda disponible para diagnóstico.

**Restricción abierta R5:** el PRD no fija la raíz de confianza, su distribución o actualización, ni
el algoritmo o formato de firma. **Responsable:** arquitectura/producto y seguridad. **Fase de
resolución:** decisión de diseño en Fase D. Un checksum aislado no satisface la autenticidad y no
puede cerrar esta historia sin una decisión formal de raíz y firma.

#### FR-7: Límites del MVP y prueba P2P

El camino principal de descarga del MVP es servidor-cliente. El dominio de distribución debe
permitir más de un mecanismo sin acoplar los requisitos a una biblioteca o protocolo concreto.

**Consecuencias verificables:**
- La documentación de Fase D puede sustituir el mecanismo de transferencia del MVP sin cambiar los
  contratos funcionales de FR-5 y FR-6.
- El intercambio real entre clientes no se presenta como capacidad disponible hasta que la PoC
  demuestra seed, descubrimiento de peers, transferencia por fragmentos, integridad y recuperación.

**Restricción abierta P2P:** BitTorrent con tracker local y MonoTorrent son candidatos del plan,
no decisiones. Tampoco se decide en el PRD si la PoC bloquea el MVP. **Responsable:** arquitectura.
**Fase de resolución:** ejecución de la PoC en Fase D. La abstracción de transferencia es un
requisito de diseño para Fase D; su diseño no se fija aquí.

### 4.4 Instalación portable

**Descripción:** El cliente instala exclusivamente en modo portable mediante un manifiesto
versionado. Las operaciones son tipadas; las privilegiadas requieren confirmación explícita.

#### FR-8: Instalación portable

Un cliente puede extraer un paquete validado en un directorio configurable, detectar una
instalación existente, reparar cuando sea viable y comunicar errores y reintentos.

**Consecuencias verificables:**
- La instalación crea los archivos del paquete dentro del directorio autorizado y no fuera de él.
- Un manifiesto manipulado que contenga una acción no permitida es rechazado.
- Una operación privilegiada requiere confirmación explícita antes de ejecutarse.
- El cliente detecta una instalación existente y evita repetir operaciones ya completadas cuando
  la instalación es válida.
- Un error de instalación queda persistido y puede reintentarse sin reinstalar el cliente.

**Fuera de alcance de esta FR:** modos Installer y External.

### 4.5 Lanzamiento y supervisión

**Descripción:** El cliente valida la instalación y ejecuta el juego con sus argumentos y
directorio de trabajo. El servidor presenta el estado de los equipos en tiempo real.

#### FR-9: Ejecución de videojuegos

Un cliente puede detectar el ejecutable de un juego instalado, aplicar sus argumentos de
lanzamiento, establecer el directorio de trabajo, detectar el proceso en ejecución y mostrar
errores comprensibles.

**Consecuencias verificables:**
- Un juego instalado y validado se inicia desde el cliente con los argumentos y el directorio de
  trabajo definidos en el catálogo.
- Un ejecutable ausente o una instalación inválida impiden el lanzamiento y producen un error
  identificable.
- El cliente puede indicar si el proceso está en ejecución.

**Fuera de alcance de esta FR:** conexión directa del juego a servidores de juego y launchers
externos.

#### FR-10: Panel de estado de clientes

El servidor muestra equipos conectados, identificador y nombre, estado de conexión, juegos y
versiones instalados, descargas activas, progreso de instalaciones y errores registrados.

**Consecuencias verificables:**
- El servidor refleja la conexión y desconexión de un cliente autorizado.
- El servidor muestra el progreso de una descarga o instalación activa.
- El servidor registra un error de cliente y lo presenta asociado al equipo.
- Un cliente no autorizado no aparece como cliente autorizado ni transmite estado de juego.

### 4.6 Actualizaciones de versiones completas

**Descripción:** El sistema detecta nuevas versiones de juegos y configuraciones. El MVP resuelve
versiones completas; no incluye actualizaciones diferenciales avanzadas.

#### FR-11: Detección y distribución de versiones completas

El cliente detecta cuando dispone de una versión antigua y puede obtener una versión completa
válida del catálogo.

**Consecuencias verificables:**
- Tras cambiar la versión disponible, el cliente identifica que su versión instalada está
  desactualizada.
- El cliente puede descargar e instalar la versión completa sustituyendo la anterior conforme al
  manifiesto.

**Fuera de alcance de esta FR:** actualizaciones diferenciales avanzadas.

## 5. Requisitos no funcionales transversales

- **NFR-1. Red local autónoma.** El flujo completo del MVP debe funcionar sin conexión a
  Internet; la ausencia de Internet no invalida el catálogo ya sincronizado ni las operaciones
  locales.
- **NFR-2. Escala inicial.** El diseño debe admitir el objetivo de 8 a 20 jugadores concurrentes
  en una red Ethernet de 1 Gbps. Este objetivo es una hipótesis de producto: no se promete una
  velocidad de transferencia hasta validar rendimiento en Fase D.
- **NFR-3. Paquetes de varios gigabytes.** Descargas e instalaciones deben soportar paquetes de
  varios GB y persistir el progreso ante reinicios.
- **NFR-4. Plataforma inicial.** El entorno inicial es Windows 10/11; la evolución
  multiplataforma se conserva como posibilidad, no como alcance del MVP.
- **NFR-5. Seguridad de contenido.** El cliente no instala paquetes cuya integridad o
  autenticidad no ha sido validada; los manifiestos restringen las acciones a operaciones tipadas y
  protegen el sistema frente a extracción fuera de los directorios autorizados.
- **NFR-6. Uso de recursos.** El servidor y el cliente mantienen un consumo moderado de CPU y
  memoria; los límites concretos se fijarán con pruebas, sin inventar umbrales en este PRD.
- **NFR-7. Diagnóstico.** El sistema produce logs estructurados y permite asociar errores a
  cliente, descarga, instalación o lanzamiento.
- **NFR-8. Licencias.** La publicación de paquetes respeta la información de licencia y las
  restricciones de distribución registradas para cada videojuego.
- **NFR-9. Resiliencia operativa.** El producto contempla desconexiones de clientes, servidor
  temporalmente inaccesible, instalación incompleta y reinicios durante operaciones, con
  recuperación o error explícito y recuperable.
- **NFR-10. Sin cloud obligatorio.** El servidor opera sin infraestructura cloud y el MVP no
  depende de trackers, DHT ni servicios externos públicos.

## 6. No objetivos del MVP

- Gestión centralizada de sesiones (RF-08); el MVP sólo incluye el modo autónomo.
- Instalación en modos Installer y External.
- Actualizaciones diferenciales avanzadas.
- Intercambio P2P real entre clientes como camino principal de descarga.
- Soporte multiplataforma más allá de Windows 10/11.
- Conexión directa del juego a servidores de juego.
- Desarrollo de un servicio cloud, una identidad corporativa federada o integración con
  plataformas de launcher externas.

## 7. Alcance del MVP

### 7.1 Incluido

- Servidor con catálogo, administración básica, paquetes y estado de clientes.
- Cliente con descubrimiento, dirección manual alternativa, sincronización, búsqueda y filtrado.
- Descarga servidor-cliente con integridad, pausa, reanudación y persistencia.
- Autenticación y respuesta segura ante clientes no autorizados, con mecanismo por resolver en
  Fase D.
- Validación de integridad y autenticidad con raíz/firma por resolver en Fase D.
- Instalación portable mediante manifiesto versionado y operaciones tipadas.
- Lanzamiento de juegos y panel de estado.
- Demostración reproducible con al menos dos clientes y contenido de prueba o redistribuible.

## 8. Criterios de éxito

### Primarios

- **SM-1:** El flujo de §9 se completa en una LAN real con dos o más clientes, sin Internet y con
  el mismo paquete íntegro para todos. Valida FR-2 a FR-11.
- **SM-2:** El 100% de las descargas de prueba interrumpidas se reanudan desde el progreso
  persistido sin reiniciar el paquete desde cero. Valida FR-5.
- **SM-3:** Ningún paquete alterado, manipulado o que no supera la validación de confianza llega
  a fase de instalación. Valida FR-6 y FR-8.

### Secundarios

- **SM-4:** El servidor muestra el estado de cada cliente autorizado y sus errores sin que el
  organizador solicite información por separado. Valida FR-10.
- **SM-5:** Cada paso de demostración tiene instrucciones reproducibles. Valida FR-1 a FR-11.

### Contrav métricas

- **SM-C1:** No se optimiza la velocidad de transferencia por encima de la recuperación y la
  integridad de las operaciones; el rendimiento queda sin promesa hasta las pruebas de Fase D.
- **SM-C2:** No se optimiza la cobertura P2P a costa de bloquear el flujo servidor-cliente del MVP
  o de acoplar prematuramente el dominio a una biblioteca.

## 9. Escenario de aceptación del MVP

1. Arrancar el servidor.
2. Conectar y autorizar dos o más clientes.
3. Sincronizar el catálogo.
4. Mostrar un videojuego disponible.
5. Descargar su paquete desde el servidor.
6. Validar su integridad y autenticidad.
7. Instalarlo en modo portable.
8. Detectar que ya está instalado.
9. Ejecutarlo desde el cliente.
10. Mostrar el estado en el servidor.

Cada paso debe ser reproducible sin Internet. La PoC P2P se ejecuta y valida después como
riesgo técnico de Fase D, sin presentarse como capacidad del MVP en este escenario.

## 10. Riesgos, dependencias y restricciones

| ID | Riesgo o dependencia | Impacto | Responsable / fase |
| --- | --- | --- | --- |
| R3 | Mecanismo de descubrimiento sin decisión cerrada (mDNS candidato) | Riesgo de comportamiento en redes restrictivas o segmentadas | Fase D |
| R4 | Autenticación sin mecanismo ni contrato cerrado | Acceso no autorizado o experiencias inconsistentes | Producto: Fase B/C; mecanismo: Fase D |
| R5 | Raíz de confianza y firma sin decisión | La autenticidad no es verificable | Fase D |
| P2P | Alcance y carácter bloqueante de la PoC sin decisión | Posible acoplamiento o validación tardía | Fase D |
| R-01 | Objetivo de 8-20 jugadores y 1 Gbps sin validar | No se puede prometer rendimiento | Fase D |
| R-02 | Reanudación de paquetes de varios GB | Pérdida de tiempo de preparación | Fase D / Fase F |
| R-03 | Descubrimiento y reconexión en redes restrictivas | Falla de incorporación | Fase D |
| R-05 | Restricciones de licencia de paquetes | Publicación no permitida | Restricciones: Fase B; mecanismo: Fase D |

Dependencias de fases posteriores: UX, arquitectura, ADRs, épicas e historias no forman parte de
este documento ni se adelantan aquí.

## 11. Trazabilidad

### 11.1 Plan maestro → PRD

| Fuente `docs/plan.md` | Destino PRD |
| --- | --- |
| RF-01 | FR-1, FR-2 |
| RF-02 | FR-3 (R3 abierta) |
| RF-03 | FR-5, FR-7 (P2P abierta) |
| RF-04 | FR-6, FR-8 (R5 abierta) |
| RF-05 | FR-9 |
| RF-06 | FR-10 |
| RF-07 | FR-11 |
| RF-08 | FR-11 no aplica; modo gestionado excluido (§6) |
| NFR del plan | NFR-1 a NFR-10 |

### 11.2 Product Brief → PRD

| Brief | PRD |
| --- | --- |
| Alcance y criterios de éxito | §7, §8, §9 |
| R3 | FR-3, NFR-9, R3 en §10 |
| R4 | FR-4, NFR-5, R4 en §10 |
| R5 | FR-6, NFR-5, R5 en §10 |
| P2P | FR-7, SM-C2, P2P en §10 |
| Riesgos adicionales | §10 |

### 11.3 FR → NFR, SM y escenario

| FR | NFR relacionados | SM |
| --- | --- | --- |
| FR-1 | NFR-8, NFR-10 | SM-1, SM-5 |
| FR-2 | NFR-1 | SM-1, SM-5 |
| FR-3 | NFR-1, NFR-4, NFR-9 | SM-1, SM-5 |
| FR-4 | NFR-1, NFR-5 | SM-1, SM-5 |
| FR-5 | NFR-1, NFR-3, NFR-9 | SM-1, SM-2, SM-5 |
| FR-6 | NFR-5, NFR-8 | SM-1, SM-3, SM-5 |
| FR-7 | NFR-10 | SM-1, SM-5 |
| FR-8 | NFR-3, NFR-5, NFR-9 | SM-1, SM-3, SM-5 |
| FR-9 | NFR-1, NFR-9 | SM-1, SM-5 |
| FR-10 | NFR-6, NFR-7, NFR-9 | SM-4, SM-5 |
| FR-11 | NFR-3, NFR-5 | SM-1, SM-5 |

## 12. Preguntas abiertas

- **OQ-1 (R4):** ¿Qué identidad, alta, revocación y expiración usan los clientes y qué ocurre
  exactamente con un equipo no autorizado? **Responsable:** producto/arquitectura. **Fase:** B/C para
  contrato funcional; D para mecanismo.
- **OQ-2 (R5):** ¿Cuál es la raíz de confianza, cómo se distribuye y actualiza, y qué formato de
  firma se validará? **Responsable:** arquitectura/seguridad. **Fase:** D.
- **OQ-3 (R3):** ¿mDNS puro, híbrido con dirección manual u otro mecanismo, y cómo se comportará en
  redes segmentadas? **Responsable:** arquitectura. **Fase:** D.
- **OQ-4 (P2P):** ¿Qué debe demostrar exactamente la PoC y es bloqueante para el MVP? **Responsable:**
  arquitectura. **Fase:** D.
- **OQ-5 (R-01/NFR-6):** ¿Qué objetivos medibles de rendimiento y recursos se fijan tras las
  pruebas? **Responsable:** arquitectura. **Fase:** D.

## 13. Índice de supuestos

- **[ASSUMPTION: OQ-1]** Los clientes se autentican de forma explícita antes de usar las
  capacidades protegidas; el mecanismo exacto está pendiente de Fase D.
- **[ASSUMPTION: OQ-2]** La autenticidad de paquetes requiere una decisión formal de raíz de
  confianza y firma; un checksum no es suficiente.
- **[ASSUMPTION: OQ-3]** El descubrimiento automático permanece en el MVP, mientras la dirección
  manual es una vía obligatoria de respaldo.
- **[ASSUMPTION: OQ-4]** La PoC P2P puede validarse después del flujo servidor-cliente sin
  bloquear el MVP; esta condición requiere confirmación en Fase D.
- **[ASSUMPTION: OQ-5]** Los objetivos iniciales de escala y recursos son hipótesis a validar, no
  compromisos de rendimiento.

## 14. Sobre este documento

- **Fuente:** `docs/plan.md` (prompt maestro de desarrollo con BMAD), commit `ab9a3b7`.
- **Product Brief:** `../briefs/brief-lan-party-hub-2026-10-26/brief.md`, aprobado en el
  Checkpoint A.
- **Fase:** B — Product Requirements Document. **Estado:** `final` — PRD completado, revisado y
  listo para el Checkpoint B; su aprobación no está implícita en este documento.
- **Decisiones no cerradas:** R3, R4, R5 y P2P quedan explícitamente abiertas con responsable y
  fase de resolución (§4, §10, §12). Este PRD no las decide y no inventa mecanismos técnicos.
- **Siguiente checkpoint:** Checkpoint B — validación del PRD.
- **Alcance de esta fase:** brief aprobado y PRD. Quedan fuera de esta ejecución UX,
  arquitectura, ADRs, backlog de épicas e historias, scaffold y código.
