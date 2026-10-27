# Epic 1 Context: Fundamentos del sistema y arranque del evento

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Épica fundacional: no entrega valor de usuario por sí sola, pero fija la forma del sistema para el resto del MVP. Deja la solución en servidor, cliente y contratos compartidos bajo Clean Architecture, con el dominio aislado de la infraestructura, y hace que ambos binarios arranquen en LAN real y sin Internet: el servidor con la mínima superficie de red y datos en carpeta configurable; el cliente con layout en disco, persistencia propia e identidad estable.

## Stories

- Story 1.1: Estructura de solución y contratos compartidos
- Story 1.2: Arranque del servidor con superficie de red mínima y datos configurables
- Story 1.3: Arranque del cliente con layout en disco y persistencia propia
- Story 1.4: Observabilidad correlacionada desde el primer arranque

## Requirements & Constraints

- **Solución y contratos.** Tres proyectos (`src/Server`, `src/Client`, `src/Shared`) y tres de prueba. `Shared` no referencia ningún otro proyecto: solo contratos (DTOs, identificadores, enumeraciones, eventos de SignalR, versión de protocolo), sin lógica ni dependencias de EF Core, Avalonia o red. `Server` y `Client` lo referencian, sin duplicar contratos.
- **Dependencias.** Hacia adentro: `Infrastructure → Application → Domain`. Ningún tipo de `Domain` importa infraestructura, EF Core, Avalonia, HTTP ni librerías P2P. Los módulos se comunican por contratos de `Shared` y casos de uso publicados, nunca por implementación interna de otro.
- **El MVP no es P2P.** Sin motor P2P, tracker, endpoint `announce`, swarm, seeding ni mapa de fragmentos; sin flags, puertos o endpoints de P2P ni referencia a MonoTorrent. El vocabulario es «servidor y cliente», no «peer».
- **Red y configuración.** El servidor escucha solo en interfaces privadas y abre solo el puerto HTTP (API + SignalR) y el UDP 5353 de mDNS; sin TLS en la LAN del MVP (la autenticidad la aporta la firma). Precedencia `flags > env > archivo` en servidor y cliente; sin selector de motor de transferencia, porque solo existe HTTP.
- **Persistencia, layout y despliegue.** Un SQLite por rol, con migraciones de EF Core versionadas aplicadas al arrancar; cada esquema es propiedad de su rol y el cliente no replica tablas del servidor. El servidor crea `catalog.sqlite` y su repositorio de paquetes en su carpeta de datos. El cliente, en `%LOCALAPPDATA%\LanPartyHub`: `install/` (raíz de instalación configurable, único directorio confinado), `downloads/` (parciales, fuera de esa raíz), `manifests/` (caché de manifiestos) y `state.sqlite`. Publicación self-contained por rol, sin instalador ni servicio; datos portables.
- **Identidad.** El `clientId` (UUID v7) se emite una vez, se persiste y nunca se regenera: clave técnica de deduplicación y correlación de logs. El `machineName` es la etiqueta humana del equipo, validada y uniquificada por el servidor, no un control de acceso.
- **Modelo operativo.** Sin autenticación ni autorización de cliente: la LAN es confiable por diseño y el único filtrado es la compatibilidad de versión. Plataforma inicial Windows 10/11 x64; el multiplataforma es posibilidad, no alcance.
- **Errores y observabilidad.** Un único tipo de error de API en todo el sistema: `code` de catálogo, mensaje, causa y siguiente paso; un `code` desconocido se muestra con texto genérico, nunca como texto principal. Logging JSON con `operationId` y `clientId` cruzando la frontera servidor-cliente; métricas del host (tasa de transferencia, operaciones activas, errores por código) locales; sin credenciales en los logs.
- **Pruebas.** Tres niveles con contenido declarado: unitarias, integración (SQLite real en archivo, endpoints con `WebApplicationFactory`) y E2E en LAN real con dos o más clientes, el escenario de aceptación del MVP. Compartir fragmentos entre clientes es caso de la PoC P2P y queda fuera.

## Technical Decisions

- **Capas y módulos.** Dominio, Aplicación e Infraestructura, más Presentación (Avalonia, MVVM) en el cliente, en un monolito modular por deployable; los puertos se declaran en Aplicación. Servidor: `Catalog`, `PackageDistribution`, `ClientManagement`, `Synchronization`, `Administration`; no existe `Tracking`. Cliente: `Connection`, `CatalogSync`, `Download`, `Installation`, `GameLauncher`, `Diagnostics`.
- **Contenido de `Shared`.** Declarar exactamente: `ApiError`, `ErrorCatalog`, `DownloadState`, `InstallationState`, `OperationOutcome`, `StateDisplayMap`, `CatalogViewState`, `ClientStateChanged`, `DownloadProgressUpdated`, `InstallationProgressUpdated`, `ClientErrorReported`, `CatalogChanged`, `ClientRegistrationView`, `TrustAnchor`, `PackageDescriptor`, `GameDescriptor`, `InstallationManifest` con su sobre firmado, `VersionInfo`, `ServerEndpoint` y `ConnectionState`.
- **Estados.** `DownloadState` e `InstallationState`: enumeraciones cerradas, transiciones explícitas y estado terminal por operación, idénticas en cliente y servidor. El cliente es la única fuente de verdad del estado local; el servidor lo proyecta. La tabla compartida de microcopia es la única fuente de textos de estado.

## UX & Interaction Patterns

- El error visible al usuario sigue tres partes (qué falló, por qué, qué hacer ahora) y el `code` del catálogo nunca es el texto principal.

## Cross-Story Dependencies

- 1.2 y 1.3 consumen los contratos de `Shared` de 1.1; 1.4 necesita el logging de 1.1 y el host con su endpoint de métricas de 1.2. La épica no depende de ninguna otra: es la base del backlog.
- Fija la forma que consume el resto (contratos, máquinas de estado, layout, identidad, niveles de prueba): cambiar un contrato compartido obliga a revisar a sus consumidores.
