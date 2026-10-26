# LAN Party Hub — Prompt maestro de desarrollo con BMAD

## 1. Rol y misión

Actúa como un equipo multidisciplinar de ingeniería de software coordinado mediante **BMAD Method**, utilizando los agentes y workflows disponibles en la instalación actual.

Tu misión es planificar, diseñar y posteriormente desarrollar **LAN Party Hub**, una plataforma de escritorio para gestionar, distribuir, instalar, actualizar y ejecutar videojuegos en una red local (LAN Party).

El proyecto comienza desde cero (greenfield).

Debes aplicar la metodología BMAD, asegurando la trazabilidad entre requisitos de negocio, arquitectura, experiencia de usuario, épicos, historias, implementación y pruebas.

**No comiences escribiendo código de producción.** Primero deben definirse y validarse los requisitos y las principales decisiones de arquitectura.

## 2. Visión del producto

LAN Party Hub será una plataforma que permita que un conjunto de jugadores conectados a la misma red local puedan disponer de un catálogo compartido de videojuegos.

El sistema estará compuesto por dos aplicaciones principales:

- **LAN Party Hub Server:** servidor central encargado de gestionar el catálogo de videojuegos, paquetes, configuraciones, versiones, distribución de archivos y estado de los clientes.
- **LAN Party Hub Client:** aplicación de escritorio instalada en cada ordenador que permita descubrir juegos, descargarlos, instalarlos, actualizarlos y ejecutarlos.

La experiencia debe ser semejante a una biblioteca de juegos moderna, pero optimizada para eventos LAN, con funcionamiento completamente local y sin dependencias obligatorias de Internet.

### Principios del producto

1. Simplicidad para los jugadores.
2. Instalación y ejecución de juegos con la mínima intervención posible.
3. Distribución eficiente mediante P2P.
4. Gestión centralizada y supervisión en tiempo real.
5. Arquitectura modular, mantenible y extensible.
6. Seguridad en la instalación y ejecución de software.
7. Funcionamiento offline dentro de la red local.
8. Compatibilidad inicial con Windows, sin impedir una futura evolución multiplataforma.

## 3. Requisitos funcionales

### RF-01. Catálogo centralizado

El servidor permitirá registrar y administrar videojuegos.

Cada videojuego tendrá:

- Identificador único.
- Nombre y descripción.
- Categorías y etiquetas.
- Carátula e imágenes.
- Versión disponible.
- Tamaño del paquete.
- Número mínimo y máximo de jugadores.
- Sistema operativo compatible.
- Tipo de distribución.
- Tipo de instalación.
- Configuración de lanzamiento.
- Requisitos y dependencias.
- Información sobre licencia y restricciones de distribución.

El cliente sincronizará automáticamente el catálogo.

La interfaz permitirá buscar, filtrar y categorizar los videojuegos.

### RF-02. Descubrimiento y conexión

Los clientes deberán poder descubrir automáticamente el servidor dentro de la red local.

Evaluar mDNS u otros mecanismos apropiados.

También debe existir una opción para configurar manualmente la dirección del servidor.

El cliente deberá detectar desconexiones, recuperar la conexión y sincronizar los cambios pendientes.

### RF-03. Distribución P2P de archivos

El servidor actuará como repositorio inicial y como coordinador del sistema de distribución.

Los clientes deberán intercambiar los fragmentos de los videojuegos descargados para reducir la carga sobre el servidor.

Se propone estudiar **BitTorrent con tracker local**, evaluando MonoTorrent como biblioteca de implementación en .NET.

Requisitos:

- Seed inicial desde el servidor.
- Descarga simultánea desde varios peers.
- Descarga parcial por fragmentos.
- Verificación de integridad.
- Pausa y reanudación.
- Recuperación ante desconexiones.
- Control de velocidad y concurrencia.
- Priorización del tráfico LAN.
- Seguimiento del progreso de descarga.
- Evitar dependencias de trackers públicos, DHT público o servicios externos.
- Gestión de versiones de los paquetes.
- Posibilidad de compartir archivos después de terminar la descarga.

Diseñar una abstracción que permita implementar distintos mecanismos de transferencia, por ejemplo HTTP y BitTorrent, sin acoplar el dominio a una biblioteca concreta.

Realizar una prueba de concepto técnica de P2P antes de asumir que MonoTorrent es la solución definitiva.

### RF-04. Instalación automática

Cada videojuego dispondrá de un manifiesto de instalación versionado.

Se contemplarán tres modalidades:

**Portable:** extracción de archivos en un directorio configurable, sin instalador.

**Installer:** ejecución controlada de instaladores oficiales con parámetros previamente definidos.

**External:** detección y ejecución mediante plataformas existentes, como Steam, cuando corresponda.

El sistema deberá gestionar:

- Directorio de instalación.
- Archivos de configuración.
- Dependencias.
- Argumentos de lanzamiento.
- Detección de instalaciones existentes.
- Validación de integridad.
- Reparación de instalaciones cuando sea viable.
- Errores y reintentos.
- Limpieza de archivos temporales.

Por seguridad, los manifiestos no podrán ejecutar comandos arbitrarios. Se deberán definir operaciones tipadas, mecanismos de confianza y confirmación explícita para operaciones privilegiadas.

### RF-05. Lanzador de videojuegos

El cliente deberá permitir iniciar directamente los juegos instalados.

Funcionalidades:

- Detectar el ejecutable.
- Validar la instalación.
- Aplicar los argumentos de lanzamiento.
- Establecer el directorio de trabajo.
- Detectar si el proceso está ejecutándose.
- Mostrar errores comprensibles.
- Soportar launchers externos.

Diseñar el sistema para incorporar posteriormente funcionalidades de conexión directa a servidores de juego.

### RF-06. Gestión de clientes

El servidor permitirá visualizar:

- Equipos conectados.
- Identificador y nombre del equipo.
- Estado de conexión.
- Juegos instalados.
- Versiones disponibles e instaladas.
- Descargas activas.
- Progreso de las instalaciones.
- Errores registrados.

La comunicación de estado deberá actualizarse en tiempo real mediante SignalR o una solución equivalente debidamente justificada.

### RF-07. Actualizaciones

El sistema detectará nuevas versiones de videojuegos y configuraciones.

Debe poder identificar cuándo un cliente dispone de una versión antigua.

Evaluar actualizaciones completas frente a diferenciales, prestando atención al coste de implementación y a la integridad de los paquetes.

Las actualizaciones diferenciales avanzadas podrán quedar fuera del MVP.

### RF-08. Preparación centralizada de sesiones

Diseñar la arquitectura para admitir dos modos:

**Modo autónomo:** cada jugador elige los juegos que desea descargar e instalar.

**Modo gestionado:** el administrador selecciona los juegos necesarios para una sesión y propone su instalación a todos los clientes.

Las instalaciones centralizadas deben respetar permisos, políticas locales y confirmaciones necesarias.

Para el MVP se priorizará el modo autónomo. El modo gestionado se planificará para una fase posterior.

## 4. Requisitos no funcionales

Considerar las siguientes condiciones iniciales:

- Entre 8 y 20 jugadores concurrentes como objetivo inicial.
- Red Ethernet de 1 Gbps, contemplando futuras redes de 2,5 Gbps.
- Entorno Windows 10/11.
- Paquetes de videojuegos de varios GB.
- Recuperación ante desconexiones.
- Distribución sin conexión a Internet.
- Persistencia de descargas e instalaciones.
- Consumo moderado de memoria y CPU.
- Operación del servidor sin infraestructura cloud.
- Logs estructurados y diagnóstico de errores.
- Intercambio P2P limitado a la red autorizada.
- Integridad y autenticidad de paquetes.
- Protección contra extracción de archivos fuera de los directorios permitidos.
- Publicación de paquetes conforme a sus licencias.

Estos valores son hipótesis iniciales y deberán validarse durante el análisis.

Definir objetivos de rendimiento medibles, sin prometer velocidades de transferencia antes de realizar pruebas.

## 5. Stack tecnológico propuesto

El stack inicial será:

**Backend**
- .NET 10 LTS.
- ASP.NET Core Web API.
- SignalR.
- SQLite.
- Inyección de dependencias nativa.
- Logging estructurado.

**Cliente**
- .NET 10.
- Avalonia UI.
- Patrón MVVM.
- SQLite para persistencia local.
- HttpClient para comunicación REST.
- SignalR Client para sincronización.
- Motor de transferencias desacoplado.

**Distribución**
- Investigar MonoTorrent.
- BitTorrent con tracker local como candidato principal.
- HTTP como mecanismo alternativo.

**Pruebas**
- xUnit.
- Pruebas unitarias.
- Pruebas de integración.
- Pruebas de extremo a extremo cuando sean factibles.
- Simulaciones de múltiples clientes y fallos de red.

Este stack es una propuesta inicial, no una decisión irrevocable.

El arquitecto deberá validar las tecnologías e identificar incompatibilidades, riesgos y alternativas antes de cerrar las decisiones.

## 6. Criterios de arquitectura

Aplicar una arquitectura modular y pragmática, inspirada en Clean Architecture.

Identificar límites claros entre:

- Catalog
- Distribution
- Installation
- GameLauncher
- ClientManagement
- Synchronization
- Administration

Evaluar una estructura de solución como:

```text
LANPartyHub/
  src/
    Server/
    Client/
    Shared/
  tests/
    UnitTests/
    IntegrationTests/
    EndToEndTests/
  docs/
```

La estructura definitiva será responsabilidad del arquitecto BMAD.

Evitar microservicios y complejidad de infraestructura innecesaria.

Preferir un monolito modular para el servidor.

Los componentes cliente, servidor y distribución deberán poder evolucionar independientemente sin duplicar reglas de negocio ni introducir acoplamientos excesivos.

Definir explícitamente:

- Contratos API.
- Eventos de SignalR.
- Versionado de contratos.
- Modelo de datos.
- Estados de instalación y descarga.
- Persistencia de operaciones.
- Ciclo de vida de los paquetes.
- Protocolos de comunicación.
- Tratamiento de errores.
- Estrategia de migraciones.
- Límites de concurrencia.
- Políticas de seguridad.

Registrar las decisiones arquitectónicas relevantes mediante ADR.

## 7. Metodología BMAD

Utiliza los workflows y agentes oficiales realmente instalados.

No inventes comandos ni presupongas que todos los workflows están disponibles con el mismo nombre en todas las versiones.

Antes de comenzar:

1. Detecta la instalación y versión de BMAD.
2. Identifica los agentes, skills y workflows disponibles.
3. Revisa si ya existen artefactos del proyecto.
4. Selecciona el flujo de planificación apropiado para un producto greenfield con varios épicos.
5. Informa del proceso que se seguirá.

### Fase A. Descubrimiento y Product Brief

Utiliza el agente Analyst y el workflow Product Brief correspondiente.

Objetivos:

- Definir el problema y los usuarios.
- Identificar los escenarios principales.
- Definir el alcance funcional.
- Distinguir requisitos obligatorios y opcionales.
- Evaluar riesgos técnicos.
- Identificar restricciones legales.
- Formular las preguntas que realmente afecten al diseño.
- Definir una primera visión del MVP.

Generar el Product Brief.

**Checkpoint A:** solicitar aprobación antes de continuar.

### Fase B. Product Requirements Document

Utiliza el agente Product Manager y el workflow PRD.

El PRD deberá contener:

- Visión del producto.
- Perfiles de usuario.
- Casos de uso.
- Requisitos funcionales.
- Requisitos no funcionales.
- Historias de usuario de alto nivel.
- Criterios de éxito.
- Restricciones.
- Riesgos y dependencias.
- Requisitos excluidos del MVP.
- Trazabilidad de requisitos.

No incorporar requisitos nuevos sin identificarlos como propuestas o decisiones pendientes.

**Checkpoint B:** validar el PRD.

### Fase C. Experiencia de usuario

Utiliza el agente UX y sus workflows.

Diseña las principales pantallas del cliente:

1. Conexión y descubrimiento del servidor.
2. Catálogo de videojuegos.
3. Ficha de videojuego.
4. Descargas.
5. Instalación y progreso.
6. Biblioteca de juegos instalados.
7. Configuración y diagnóstico.

Diseña también los flujos mínimos de administración del servidor.

La interfaz deberá ser moderna, visual, sencilla y consistente.

Priorizar una experiencia similar a una biblioteca de videojuegos, sin copiar elementos protegidos de terceros.

Documentar navegación, estados vacíos, errores, feedback, accesibilidad y flujos de usuario.

**Checkpoint C:** validar los flujos UX.

### Fase D. Arquitectura

Utiliza el agente Architect y el workflow Architecture.

Genera una arquitectura que incluya:

- Diagrama C4 de contexto y contenedores.
- Módulos del servidor.
- Módulos del cliente.
- Modelo de dominio.
- Modelo de persistencia.
- Contratos API.
- Contratos de eventos.
- Diseño del tracker y red P2P.
- Arquitectura de instalación.
- Arquitectura de lanzamiento.
- Seguridad.
- Despliegue y empaquetado.
- Observabilidad.
- Estrategia de pruebas.
- ADRs.

Para la distribución P2P, documentar al menos:

1. Inicialización de torrents.
2. Descubrimiento de peers.
3. Gestión de fragmentos.
4. Integridad.
5. Persistencia de progreso.
6. Gestión de seeders.
7. Priorización y límites.
8. Recuperación ante errores.

Identifica los riesgos que necesitan una prueba técnica o spike antes de implementar el sistema completo.

**Checkpoint D:** validar arquitectura y riesgos.

### Fase E. Épicos e historias

Utiliza el agente Product Owner y el workflow de planificación de tickets o historias disponible en BMAD.

Como propuesta inicial, estudiar los siguientes épicos:

- EPIC-01: Bootstrap y fundamentos del sistema.
- EPIC-02: Servidor y administración del catálogo.
- EPIC-03: Cliente y descubrimiento del servidor.
- EPIC-04: Biblioteca visual y sincronización.
- EPIC-05: Distribución y descargas.
- EPIC-06: Instalación y gestión de paquetes.
- EPIC-07: Lanzamiento de videojuegos.
- EPIC-08: Monitorización de clientes.
- EPIC-09: Actualizaciones y reparación.
- EPIC-10: Gestión avanzada de sesiones LAN.

Ajusta, divide o fusiona estos épicos según las dependencias reales.

Cada historia deberá contener:

- Identificador.
- Objetivo.
- Descripción.
- Dependencias.
- Alcance.
- Criterios de aceptación verificables.
- Casos de error.
- Pruebas necesarias.
- Condiciones de finalización.

Las historias deben ser suficientemente pequeñas para completarse y verificarse en una sesión de desarrollo cuando sea razonable.

Identifica historias que proporcionen funcionalidad vertical y demostrable, no únicamente capas técnicas aisladas.

**Checkpoint E:** aprobar el backlog inicial.

### Fase F. Implementación

Utiliza el workflow BMAD Build para implementar cada historia aprobada.

Por cada historia:

1. Leer los requisitos y la arquitectura.
2. Identificar dependencias.
3. Proponer el plan técnico.
4. Implementar exclusivamente el alcance aprobado.
5. Escribir las pruebas pertinentes.
6. Ejecutar las verificaciones.
7. Revisar el código.
8. Corregir defectos importantes.
9. Documentar el resultado y actualizar el estado.

No implementar varios épicos completos simultáneamente.

No modificar contratos compartidos sin analizar sus consumidores.

Mantener el repositorio compilable y los tests existentes operativos.

## 8. Proceso de revisión y control de agentes

El desarrollo utilizará revisión técnica, pero debe evitar bucles interminables entre agentes implementadores y revisores.

Clasificar todos los hallazgos según su severidad:

- **S0 — Bloqueante:** fallos de seguridad críticos, pérdida de datos o imposibilidad de ejecutar la funcionalidad esencial.
- **S1 — Alta:** incumplimiento de criterios de aceptación, errores importantes de comportamiento o regresiones.
- **S2 — Media:** problemas de mantenibilidad, rendimiento no crítico o casos secundarios.
- **S3 — Baja:** mejoras menores de calidad y estilo.

No limitar artificialmente el número de hallazgos.

Priorizar su resolución según severidad e impacto.

Las revisiones deben cumplir:

- Cada hallazgo debe incluir evidencia concreta.
- No reabrir hallazgos resueltos sin nueva evidencia.
- No realizar revisiones completas repetidas sin cambios relevantes.
- Evitar alternancia infinita entre implementador y revisor.
- Limitar a dos ciclos automáticos de corrección y revisión por historia; después, escalar bloqueantes persistentes para decisión humana.
- Registrar los hallazgos S2/S3 no resueltos como deuda técnica cuando corresponda.
- Una historia no puede declararse completada con hallazgos S0/S1 abiertos.

El agente orquestador debe minimizar el consumo innecesario de tokens y la repetición de análisis ya aprobados.

## 9. Estrategia de MVP

El MVP deberá demostrar un flujo completo:

1. Arrancar el servidor.
2. Conectar dos o más clientes.
3. Sincronizar un catálogo.
4. Mostrar un videojuego disponible.
5. Descargar su paquete.
6. Validar su integridad.
7. Instalarlo en modo portable.
8. Detectar que está instalado.
9. Ejecutarlo desde la interfaz.
10. Mostrar su estado en el servidor.

Posteriormente, incorporar y verificar intercambio P2P real entre clientes, sin depender exclusivamente de descargas desde el servidor.

Utilizar un ejecutable de prueba o contenido redistribuible para validar todo el proceso.

El MVP deberá permitir demostrar cada escenario con instrucciones de ejecución reproducibles.

## 10. Testing y Definition of Done

Definir pruebas que incluyan:

- Descarga interrumpida y reanudación.
- Archivo corrupto.
- Cliente desconectado.
- Servidor temporalmente inaccesible.
- Varios clientes descargando simultáneamente.
- Cliente compartiendo fragmentos con otros.
- Instalación incompleta.
- Intento de extracción fuera del directorio autorizado.
- Manifiesto manipulado o no confiable.
- Juego ya instalado.
- Versión desactualizada.
- Lanzamiento fallido.
- Reinicio del cliente durante una operación.

Toda historia deberá cumplir su Definition of Done acordada, con evidencia de verificación y sin errores de severidad bloqueante o alta pendientes.

## 11. Artefactos y documentación

Mantén los artefactos oficiales en las ubicaciones convencionales de la versión BMAD instalada.

El proyecto deberá disponer, como mínimo, de:

- Product Brief.
- PRD.
- Documentación UX.
- Arquitectura.
- ADRs relevantes.
- Backlog de épicos e historias.
- Contratos técnicos.
- Estrategia de pruebas.
- Guías de instalación y ejecución.
- Registro de decisiones y riesgos.

Evita duplicar documentación sin necesidad.

El PRD será la referencia de requisitos de producto; la arquitectura será la referencia para las decisiones técnicas.

Cuando existan contradicciones, identifícalas y solicita resolución antes de propagar los cambios.

## 12. Instrucción de arranque

Comienza ahora exclusivamente por la **Fase A: Descubrimiento y Product Brief**.

Primero, revisa el estado de BMAD en el repositorio y los workflows disponibles.

Después:

1. Resume la visión inicial de LAN Party Hub.
2. Detecta las principales incertidumbres.
3. Propón el alcance del MVP.
4. Identifica riesgos y pruebas de concepto necesarias.
5. Utiliza el workflow oficial apropiado para crear el Product Brief.
6. Presenta el resultado para validación.

No inicialices todavía toda la solución .NET ni implementes código de producción.

No avances a PRD, arquitectura o historias sin haber completado el checkpoint correspondiente.

Trabaja en español para la documentación y las explicaciones. Utiliza inglés para los identificadores de código, nombres de clases, interfaces, métodos y contratos técnicos.

**Objetivo final:** construir una plataforma LAN Party funcional, mantenible y extensible, aplicando BMAD de manera disciplinada, con entregas incrementales verificables y sin sobreingeniería.