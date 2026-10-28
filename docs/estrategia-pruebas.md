# Estrategia de pruebas

> Nivel de prueba mínimo de cada caso obligatorio del MVP, el contenido declarado de los tres
> niveles y el estado de la estrategia en las historias 1.1, 1.2 y 1.3.
>
> Fuentes: `docs/plan.md` §10 (checklist de casos), AD-16 (niveles y clasificación),
> AD-27 (caso derivado del bootstrap del ancla), AD-28 (el MVP no es P2P) y
> `_bmad-output/planning-artifacts/architecture/architecture-lan-party-hub-2026-10-26/ARCHITECTURE-SPINE.md`.

## 1. Propósito

La estrategia gobierna las pruebas **obligatorias** del MVP. No elimina ningún caso de la
checklist de `docs/plan.md` §10: fija, para cada caso, el **nivel mínimo** en el que el fallo
que ese caso busca es detectable. Un caso puede tener pruebas en más de un nivel, pero el
nivel declarado es el mínimo en el que la ausencia de una prueba es un defecto.

Reglas transversales:

- **El MVP no es P2P** (AD-28). Ningún caso de la checklist presupone intercambio de
  fragmentos entre clientes; el único caso que sí lo presupone está reetiquetado como caso de
  la PoC y sale del conjunto obligatorio del MVP.
- **El ancla de confianza no se valida en la LAN** (AD-27). El caso de la sustitución del ancla
  por un intermediario es unitario o de integración: no hace falta una LAN para demostrar que
  la comprobación precede a la escritura.
- **La red real es un nivel, no un detalle** (AD-16). Un E2E que se sustituya por pruebas de
  single host no detecta fallos de red, descubrimiento ni concurrencia, y por tanto no es un E2E.

## 2. Los tres niveles y su contenido declarado

| Nivel | Contenido declarado (AD-16) | Proyecto |
|---|---|---|
| **Unitarias** | Dominio, máquinas de estado, validación de manifiesto, verificación del ancla de confianza y confines de ruta, incluida la extracción fuera del directorio autorizado | `tests/UnitTests` |
| **Integración** | Persistencia contra **SQLite real en archivo**, endpoints con `WebApplicationFactory`, motor de transferencia HTTP contra servidor de prueba | `tests/IntegrationTests` |
| **E2E en LAN real** | Escenario de aceptación del MVP (PRD §9, SM-1) con **dos o más clientes** sobre una LAN real y sin Internet | `tests/EndToEndTests` |

Los tres proyectos comparten el mismo runner: `xunit.v3` 3.2.2, `xunit.runner.visualstudio`
3.1.5 y `Microsoft.NET.Test.Sdk` 18.11.0, con versiones fijadas (sin versiones flotantes).
Solo `tests/IntegrationTests` añade `Microsoft.Data.Sqlite` 10.0.13, porque es el único nivel
que abre una base de datos real.

### 2.1 Contenido declarado de cada nivel y la historia que lo implementa

Cada ítem del contenido declarado de AD-16 tiene una historia propietaria. La columna «Estado»
declara lo que existe hoy, con las historias 1.1, 1.2 y 1.3 como base de los tres niveles.

| Nivel | Ítem del contenido declarado (AD-16) | Historia propietaria | Estado |
|---|---|---|---|
| Unitarias | Dominio (reglas de los módulos) | 2.1 | Pendiente: la historia 1.1 solo declara la estructura y los contratos |
| Unitarias | Máquinas de estado | 1.1 | **Hecho**: tablas de transición de descarga e instalación, con estado terminal y rechazo de toda transición inválida |
| Unitarias | Validación de manifiesto | 6.1, 7.3 | Pendiente: la historia 1.1 declara la forma del manifiesto y de sus operaciones cerradas |
| Unitarias | Verificación del ancla de confianza | 4.4, 12.1 (S-2) | Pendiente: la historia 1.1 declara el contrato `TrustAnchor` y los códigos del catálogo |
| Unitarias | Confines de ruta, incluida la extracción fuera del directorio autorizado | 7.2 | Pendiente |
| Unitarias | Estructura del repositorio (grafo de dependencias, inventario de contratos, ausencia de P2P) | 1.1 | **Hecho** |
| Unitarias | Configuración efectiva del servidor (parser estricto de la línea de comandos y precedencia `flags > env > archivo`) | 1.2 | **Hecho**: `ServerCommandLineTests` cubre el flag válido, el flag desconocido, el valor ausente, el valor que parece otro flag, el puerto inválido y la precedencia de las tres fuentes |
| Unitarias | Configuración efectiva del cliente (parser estricto de `--server-address`, `--install-directory` y `--theme`, formato `host:puerto` y precedencia `flags > env > archivo`) | 1.3 | **Hecho**: `ClientCommandLineTests` cubre el flag válido, el flag desconocido, el valor ausente, el valor vacío, el formato de la dirección del servidor y la precedencia de las tres fuentes |
| Unitarias | Identidad del cliente (`clientId` como UUID v7 y forma canónica) | 1.3 | **Hecho**: `ClientIdentityTests` comprueba la versión 7 y la variante RFC 4122, la marca de tiempo de la emisión, la forma canónica de 32 hexadecimales en minúsculas y que dos emisiones son distintas |
| Unitarias | Layout del cliente (la carpeta de descargas como único sitio del parcial y el identificador de paquete que no puede ser un nombre de archivo) | 1.3 | **Hecho**: `ClientDataLayoutTests` comprueba la ruta del parcial, el rechazo del identificador que lleva un separador y el cambio de la raíz de instalación |
| Integración | Persistencia contra **SQLite real en archivo** | 1.2 (servidor), 1.3 (cliente), 5.3 (progreso) | **Parcial**: la historia 1.1 persiste el progreso contra un archivo SQLite real en `tests/IntegrationTests`; la historia 1.2 añade el esquema del servidor (`catalog.sqlite`, siete tablas del ERD y `__EFMigrationsHistory`) con migraciones versionadas aplicadas al arranque; la historia 1.3 añade el esquema del cliente (`state.sqlite`, cuatro tablas del ERD y `__EFMigrationsHistory`), la identidad estable entre arranques y la reconciliación del offset persistido contra el parcial |
| Integración | Layout en disco del cliente (`install/`, `downloads/`, `manifests/`, `state.sqlite`) | 1.3 | **Hecho**: `ClientStartupTests` arranca el cliente real contra una carpeta de datos temporal y comprueba las cuatro entradas del layout, el esquema que le pertenece, la identidad que sobrevive al segundo arranque, el rechazo de un motor de transferencia desconocido y la corrección del offset persistido |
| Integración | Arranque real del cliente (layout en disco, base de datos propia e identidad estable) | 1.3 | **Hecho**: `ClientStartupTests` arranca el cliente real contra una carpeta de datos temporal, comprueba la raíz de instalación configurada como único directorio confinado, el rechazo de un registro con `transferEngine` desconocido, la operación con un identificador de paquete que no puede ser un nombre de archivo y el arranque abortado cuando la carpeta de datos no se puede escribir |
| Integración | Persistencia de los ajustes del cliente (la raíz de instalación sobrevive al reinicio) | 1.3 | **Hecho**: `ClientStartupTests` guarda la raíz de instalación, comprueba que queda en `client.appsettings.json`, que el arranque siguiente la lee y que un flag sigue ganando sobre el valor persistido |
| Integración | Arranque real del host (Kestrel real, sockets reales y SQLite real) | 1.2 | **Hecho**: la historia 1.2 arranca el host real contra un `--data-dir` temporal y comprueba el enlace, el esquema y el endpoint local de métricas |
| Integración | Endpoints con `WebApplicationFactory` | 2.x, 4.x, 5.x (endpoints REST) | Pendiente: el arnés `WebApplicationFactory` llega con las historias que traen los endpoints REST del catálogo, los paquetes y los clientes. La historia 1.2 no lo usa: arranca el host real |
| Integración | Superficie de red mínima (solo interfaces privadas, solo el puerto HTTP y el UDP 5353 de mDNS) | 1.2 | **Hecho**: `ServerStartupTests` compara los sockets TCP y UDP en escucha antes y después del arranque, comprueba que cada dirección en escucha pertenece a RFC1918 o a ULA, que el único puerto TCP nuevo es el HTTP, que el único socket UDP nuevo es el 5353 y que el enlace no se mueve por `ASPNETCORE_URLS`. Los sockets se leen de la tabla del **proceso** que hospeda al servidor y no de la tabla de la máquina: esta máquina tiene procesos de fondo que abren sockets y un diff de la tabla completa los atribuía al servidor |
| Integración | Motor de transferencia HTTP contra servidor de prueba | 5.1, 5.2 | Pendiente: el puerto `ITransferEngine` llega con la story 5.1 |
| E2E en LAN real | Escenario de aceptación del MVP con dos o más clientes | 13.1 | **Declarado**: la historia 1.1 declara el escenario, su nivel y su precondición; la ejecución es de la story 13.1 |

## 3. Clasificación de la checklist de `docs/plan.md` §10

Los buckets son los de AD-16, usados literalmente: **unitarias/integración** y **E2E en LAN real**.

| # | Caso | Nivel mínimo de detección | Historia que lo posee | Nota |
|---|---|---|---|---|
| 1 | Descarga interrumpida y reanudación | **E2E en LAN real** | 5.2, 5.3, 12.3 | La reanudación desde el offset persistido solo se demuestra con el cliente real reiniciando. |
| 2 | Archivo corrupto | **E2E en LAN real** | 6.1, 6.2 | El paquete dañado se detecta al final del camino completo servidor-cliente. |
| 3 | Cliente desconectado | **E2E en LAN real** | 4.5 | La desconexión es un hecho de la red del evento. |
| 4 | Servidor temporalmente inaccesible | **E2E en LAN real** | 4.5 | El servidor inaccesible se observa en la red real, no en un host único. |
| 5 | Varios clientes descargando simultáneamente | **E2E en LAN real** | 5.2, 12.4 | El reparto del enlace solo es observable con varios clientes reales. |
| 6 | Cliente compartiendo fragmentos con otros | **PoC P2P (S-3)** — **fuera del conjunto obligatorio del MVP** | 12.5 | Ver §4. En el MVP no hay fragmentos, así que el caso no tiene a qué aplicarse. |
| 7 | Instalación incompleta | **Unitarias/integración** | 7.3, 10.3 | Una instalación a medias se detecta contra el manifiesto y el archivo parcial. |
| 8 | Intento de extracción fuera del directorio autorizado | **Unitarias/integración** | 7.2 | El confine de ruta se comprueba sobre la ruta canónica resuelta, sin necesidad de una LAN. |
| 9 | Manifiesto manipulado o no confiable | **Unitarias/integración** | 6.1, 6.2, 7.1 | La firma y el `schemaVersion` se validan antes de escribir nada en disco. |
| 10 | Juego ya instalado | **Unitarias/integración** | 8.2, 8.3 | La detección de una instalación existente es una regla de dominio. |
| 11 | Versión desactualizada | **Unitarias/integración** | 10.1 | La comparación de versiones usa el orden semántico declarado en el catálogo. |
| 12 | Lanzamiento fallido | **Unitarias/integración** | 8.1, 8.2 | El fallo de lanzamiento (ruta del ejecutable, permiso) es un caso de dominio. |
| 13 | Reinicio del cliente durante una operación | **E2E en LAN real** | 5.3, 12.3 | La reconciliación del progreso tras el reinicio se observa con el proceso real. |
| 14 | **Ancla de confianza no coincidente rechazada antes de persistir** | **Unitarias/integración** | 4.4, 6.1 | AD-16 lo sitúa en el bucket de unitarias/integración: la comprobación precede a toda escritura y no necesita una LAN. |
| 15 | **Ancla de confianza sustituida por un intermediario, rechazada antes de persistir** (caso derivado de AD-27) | **Unitarias/integración** | 4.4, 12.1 (S-2) | Nunca E2E: no hace falta una LAN para demostrar que la comprobación precede a la escritura. Es el caso (e) del spike S-2. |

La clasificación no elimina ningún caso de la checklist: fija el nivel mínimo donde puede
detectarse cada fallo (AD-16). Los casos 1 a 13 son los trece de `docs/plan.md` §10, el caso 14
es el «ancla de confianza no coincidente rechazada antes de persistir» que AD-16 coloca en el
bucket de unitarias/integración, y el caso 15 es el que AD-16 añade a partir de AD-27.

### 3.1 Distinción obligatoria entre los rechazos

Los rechazos del camino de confianza tienen **códigos distintos** del `ErrorCatalog` y
ninguno se puede colapsar en otro (AD-15, AD-25, AD-27, AD-8):

| Rechazo | Qué es | `code` | Historia propietaria |
|---|---|---|---|
| El fingerprint del ancla no coincide | **Ataque o suplantación**: la clave que presenta el servidor no es la esperada | `ANCHOR_FINGERPRINT_MISMATCH` | 4.4, 6.1 |
| El manifiesto no viene firmado | **Manifiesto inválido**: falta la firma | `MANIFEST_SIGNATURE_INVALID` | 6.1 |
| La firma no valida contra el ancla fijada | **Manifiesto inválido**: el ancla es correcta, el manifiesto no | `MANIFEST_SIGNATURE_INVALID` | 6.1 |
| El `schemaVersion` no está soportada | **Manifiesto inválido**: el cliente no entiende el esquema | **todavía sin registrar** | 6.1 |
| El `sha256` del paquete no coincide | **Fallo de integridad**: la divergencia no invalida el ancla ni el manifiesto | `PACKAGE_INTEGRITY_MISMATCH` | 6.1 |

En el rechazo del ancla, de la firma y del `schemaVersion` **no se escribe nada** (ni ancla, ni
manifiesto en la caché `manifests/`, ni archivo parcial, ni operación registrada). En el del
`sha256` se descarta el archivo parcial y la operación queda fallida y no reparable por
reanudación.

El `code` del rechazo por `schemaVersion` desconocida **no está todavía en el `ErrorCatalog`**:
la espina no lo nombra, y lo registrará la Story 6.1, que es la historia propietaria del caso
(AD-8). El catálogo solo lleva los códigos que la espina ya nombra.

## 4. El caso «cliente compartiendo fragmentos con otros» no es un caso del MVP

El caso presupone el intercambio de fragmentos entre peers, que **no existe en el MVP**
(AD-28): el MVP tiene un único camino de descarga, HTTP servidor-cliente
(`HttpRangeTransferEngine`), y su vocabulario es «servidor y cliente», no «peer».

- El caso es de la **PoC P2P (S-3, Story 12.5)** y su cobertura queda **condicionada al
  resultado de S-3**, no al cierre del MVP.
- **Sale del conjunto obligatorio del MVP**: ninguna historia lo exige como parte de ese
  conjunto, y su ausencia no bloquea el cierre del MVP.
- Si S-3 no se ejecuta o fracasa, el MVP **sigue por HTTP** y ningún artefacto del MVP cambia.
- Ningún otro caso de la checklist presupone P2P, y ningún requisito, endpoint, puerto,
  esquema ni artefacto de despliegue del MVP depende de esta PoC.

## 5. Contenido declarado por las historias 1.1, 1.2 y 1.3

La historia 1.1 crea la estructura de los tres niveles y deja cada uno con contenido real y
ejecutable. La historia 1.2 añade el arranque real del servidor al nivel de integración y la
historia 1.3 añade el arranque real del cliente.

| Proyecto | Contenido declarado en la historia 1.1 | Contenido declarado en la historia 1.2 | Contenido declarado en la historia 1.3 |
|---|---|---|---|
| `tests/UnitTests` | Transiciones válidas, inválidas y terminales de las dos máquinas de estado; inventario exhaustivo de los 20 contratos de `Shared` y ausencia de duplicación en `Server` y `Client`; ausencia de referencias a MonoTorrent, BitTorrent o cualquier biblioteca P2P en los `.csproj`; grafo de dependencias; aislamiento de la capa `Domain`; microcopia de estado y catálogo de errores | Parser estricto de `--data-dir` y `--http-port` (flag válido, flag desconocido, valor ausente, valor que parece otro flag, puerto inválido) y precedencia de la configuración efectiva `flags > env > archivo` con sus valores por defecto | Identidad del cliente: `clientId` como UUID v7 (versión 7 y variante RFC 4122), forma canónica de 32 hexadecimales en minúsculas y dos emisiones distintas; parser estricto de `--server-address`, `--install-directory` y `--theme` (flag válido, flag desconocido, valor ausente, valor vacío, formato `host:puerto` inválido) y precedencia de las tres claves con sus valores por defecto; layout del cliente: el parcial vive solo en `downloads/` y el identificador de paquete que lleva un separador se rechaza | Identidad del cliente: `clientId` emitido como UUID v7 (versión 7 y variante RFC 4122), forma canónica de 32 hexadecimales en minúsculas y dos emisiones distintas; parser estricto de `--server-address`, `--install-directory` y `--theme` (flag válido, flag desconocido, valor ausente, valor vacío) y precedencia `flags > env > archivo` de las tres claves |
| `tests/IntegrationTests` | Persistencia real contra **SQLite en archivo** (no en memoria) con la tabla de progreso del camino HTTP (`operationId`, `bytesPersisted`, `totalBytes`), reconciliación del offset persistido contra el archivo parcial y ausencia de mapa de fragmentos | Arranque del **host real** contra un `--data-dir` temporal: creación de `catalog.sqlite` y de `packages/`, existencia de las siete tablas del ERD con el nombre y el orden exactos de sus columnas, registro en `__EFMigrationsHistory`, idempotencia sobre una base ya migrada, respuesta del endpoint local de métricas, único puerto TCP en escucha (el HTTP) con dirección privada RFC1918 o ULA, único socket UDP nuevo (el 5353 de mDNS), enlace explícito a las interfaces privadas pese a `ASPNETCORE_URLS`, ausencia de conexiones TCP salientes y negativa a arrancar sin interfaz privada | Arranque del **cliente real** contra una carpeta de datos temporal: creación de `install/`, `downloads/`, `manifests/` y `state.sqlite`, existencia de las cuatro tablas del ERD del cliente con el nombre y el orden exactos de sus columnas, ausencia de cualquier tabla del servidor, registro en `__EFMigrationsHistory`, idempotencia sobre una base ya migrada, mismo `clientId` en el segundo arranque, rechazo de un registro con `transferEngine` desconocido sin reinterpretarlo, corrección del offset persistido cuando el parcial es más corto, raíz de instalación configurada como único directorio confinado, operación con un identificador de paquete que no puede ser un nombre de archivo sin tumbar el arranque, persistencia de la raíz de instalación en `client.appsettings.json` y arranque abortado cuando la carpeta de datos no se puede escribir | Arranque del **cliente real** contra una carpeta de datos temporal: creación de `install/`, `downloads/`, `manifests/` y `state.sqlite`, existencia de las cuatro tablas del ERD del cliente con el nombre y el orden exactos de sus columnas, ausencia de cualquier tabla del servidor, registro en `__EFMigrationsHistory`, idempotencia sobre una base ya migrada, mismo `clientId` en el segundo arranque, rechazo de un registro con `transferEngine` desconocido sin reinterpretarlo y corrección del offset persistido cuando el parcial es más corto |
| `tests/EndToEndTests` | Escenario de aceptación SM-1 **declarado**: nivel, precondición y los diez pasos del PRD §9. La ejecución sobre una LAN real con dos o más clientes pertenece a la Story 13.1; mientras la precondición no se satisfaga, la prueba se omite con el motivo explícito | — | — |

El nivel de integración de la historia 1.2 arranca el host real y no un `TestServer`: la
superficie de red (AD-12) y el esquema de la base de datos (AD-11) solo son observables con
Kestrel real y con SQLite real en archivo. El de la historia 1.3 arranca el cliente real y
comprueba su layout en disco y el esquema de `state.sqlite`: el confine de la raíz de
instalación (AD-9) y la identidad estable (AD-23) solo son observables con el proceso real
sobre su carpeta de datos.

## 6. Comandos

```powershell
dotnet build LANPartyHub.sln
dotnet test LANPartyHub.sln
```

Para ejecutar el escenario E2E hace falta un servidor real del evento y, al menos, dos
clientes reales en la misma LAN. La precondición se anuncia con la variable de entorno
`LAN_PARTY_HUB_E2E_SERVER`, que apunta al servidor del evento; sin ella, la prueba del
escenario se omite y el motivo queda en el registro de la ejecución.

## 7. Trazabilidad

| Documento | Qué fija |
|---|---|
| `docs/plan.md` §10 | La checklist de casos del MVP |
| AD-16 | Los tres niveles, su contenido y la clasificación por nivel mínimo de detección |
| AD-27 | El caso derivado de la sustitución del ancla y su orden de rechazo previo a persistir |
| AD-28 | La separación entre el camino HTTP del MVP y la PoC P2P |
| Story 1.1 | La creación de los tres niveles con contenido declarado |
| Story 13.1 | La ejecución del escenario de aceptación SM-1 en LAN real |
