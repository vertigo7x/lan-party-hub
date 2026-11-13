# Traspaso · Lo que queda por medir en Windows

**Fecha:** 2026-11-12
**Estado del repo:** `main` en `af6f9f7` · 55 historias `done` · suite completa en verde
(1337 unitarias + 369 integraciones + 3 E2E) · los tres guards OK.

Este documento es el punto de partida para **ejecutar en la máquina Windows** lo que en la
máquina Linux no se pudo medir. No hay nada que descifrar: cada sección dice qué ejecutar,
qué se espera y a qué informe hay que pegarle el resultado.

---

## 1. Por qué en Windows

Cuatro spikes de la épica 12 quedaron **con su veredicto sin cerrar**, y no por dificultad
del producto: por límites del entorno de la máquina Linux de desarrollo.

| Historia | Spike | Qué falta | Informe |
|---|---|---|---|
| 12.2 | S-1 · mDNS | una **segunda red con política distinta** (con y sin *multicast*) | `docs/spike-s1-descubrimiento-mdns.md` |
| 12.3 | S-5 · reanudación | **N interrupciones** con el hash final, y la escala de **7+ GB** | `docs/spike-s5-reanudacion-paquetes.md` |
| 12.4 | S-4 · rendimiento | los escenarios de **2, 5, 10 y 20 clientes** con CPU y memoria por cliente | `docs/spike-s4-rendimiento-y-recursos.md` |
| 12.5 | S-3 · P2P | la **PoC** con tracker interno y tres peers | `docs/spike-s3-poc-p2p.md` |

Los cuatro son **NO BLOQUEANTES** (AD-21): su resultado es una decisión de fase posterior y
**no bloquea el cierre del MVP**. Aun así, cierran la evidencia que la espina exige.

Y por encima de ellos está la **13.1**, que es la que **sí cierra el MVP**: la
demostración reproducible de extremo a extremo en LAN real.

## 2. La 13.1 · el guion de aceptación

El guion **ya está declarado** como datos en
`tests/EndToEndTests/Sm1AcceptanceScenario.cs`: los diez pasos del PRD §9, la precondición
y el identificador del criterio de éxito. Lo que falta es **ejecutarlo** y dejar la
evidencia.

### 2.1 Requisitos

- Una **LAN real aislada de Internet**, con **dos o más clientes compatibles**.
- El **servidor del evento** arrancado y accesible.
- El **mismo paquete íntegro** para todos los clientes.
- La variable de entorno **`LAN_PARTY_HUB_E2E_SERVER`** apuntando al servidor (es la que
  lee el escenario).

### 2.2 Cómo ejecutar

```powershell
# 1. La suite completa, en la plataforma objetivo
dotnet test LANPartyHub.sln

# 2. Los pasos del guion, con el servidor ya arrancado
$env:LAN_PARTY_HUB_E2E_SERVER = "192.168.1.127:8080"
dotnet test tests/EndToEndTests/LANPartyHub.EndToEndTests.csproj
```

El servidor del evento se arranca desde su propio proyecto:

```powershell
dotnet run --project src/Server -- --data-dir .\datos
```

### 2.3 Los diez pasos y su evidencia

Cada paso necesita **instrucciones reproducibles, estado observable y resultado esperado**
(AC2). El guion ya los nombra; lo que hay que registrar en el informe es el **resultado
observado** de cada uno:

1. Arranque del servidor.
2. Conexión de los clientes.
3. Sincronización del catálogo.
4. Visualización de un videojuego disponible.
5. Descarga del paquete desde el servidor.
6. Validación de integridad y autenticidad.
7. Instalación portable.
8. Detección de la instalación existente.
9. Ejecución desde el cliente.
10. Visualización del estado en el servidor.

### 2.4 El criterio de no evasión

Si un cliente resulta **incompatible** o el servidor es **inaccesible** durante la
preparación, el guion **no se declara aprobado por evasión ni por omisión**: el resultado
registra el paso fallido, su diagnóstico y la condición de reintento (AC3). La evidencia
distingue **SM-1** (la cadena verificable) de **SM-5** (los diez pasos ejecutados), y no
sustituye una ejecución completa por resultados parciales.

## 3. Los cuatro spikes, uno a uno

### 3.1 S-1 · mDNS (12.2)

Ejecutar `MdnsDiscoveryTests` en una red **con** *multicast* y repetirlo en una red **sin**
*multicast* (un punto de acceso con el filtrado activado, o un equipo con el *multicast*
deshabilitado en la interfaz). Pegar ambas salidas al informe.

```powershell
dotnet test tests/IntegrationTests/LANPartyHub.IntegrationTests.csproj `
  --filter "FullyQualifiedName~MdnsDiscoveryTests"
```

### 3.2 S-5 · reanudación (12.3)

Extender `DownloadRestartRecoveryTests` para **N interrupciones** encadenadas, comprobar el
**hash final** y repetirlo con un paquete de **7+ GB**. El informe deja además una
**observación abierta**: en una sesión interrumpida posterior, la primera petición
registrada al servidor fue `bytes=0-`, mientras que en la sesión no interrumpida es
`bytes={offset}-`. Hay que determinar si es un artefacto del momento en que se registra la
petición o una divergencia real entre el registro y el parcial.

### 3.3 S-4 · rendimiento (12.4)

Medir con **2, 5, 10 y 20 clientes**: la tasa de transferencia agregada, el consumo de CPU
y de memoria **por equipo**, y el tiempo de reanudación. Los dos impedimentos del banco de
pruebas —el contenido estrangulado y el identificador de equipo fijo— están explicados en
el informe; en Windows, con clientes en equipos distintos, no aplican.

### 3.4 S-3 · P2P (12.5)

La PoC con **tracker interno en proceso**, **tres peers**, *seed* desde el servidor e
intercambio por fragmentos. La biblioteca candidata es **`MonoTorrent` 3.0.2** (la estable
que señala la espina; hoy está **sin fijar** a propósito). El criterio que realmente
distingue la PoC es el **S-3b**: que el segundo peer **reciba fragmentos de un peer** y no
solo del servidor. Los *peers* no deben salir a Internet: **DHT y LPD deshabilitados**.

## 4. Cómo se cierra cada uno

1. Se ejecuta la medición y se pega la salida **en su informe** (`docs/spike-s*.md`).
2. El informe se actualiza con el **resultado** (positivo, negativo o con reserva).
3. La historia pasa a `done` en `_bmad-output/implementation-artifacts/sprint-status.yaml`.
4. Un PR con el informe actualizado y el estado de la historia.

Los cuatro informes son de la **épica 12**, y el cambio de estado va en su propio PR, como
se ha hecho hasta ahora.

## 5. El bucle desatendido

`tools/build-lite.py` sigue siendo el planificador y el auditor. En esta sesión **no
planifica ni escribe** de forma útil para historias nuevas: su validador rechaza los planes
de más de 500 palabras y las rutas de escritura que no existan todavía, así que las
historias que crean ficheros nuevos hay que hacerlas a mano. Está anotado en la memoria del
proyecto.

```powershell
uv run tools/build-lite.py --self-test
```

## 6. Lo que NO hay que rehacer

- La **suite ya es agnóstica de plataforma** (rutas con forma Windows, `/proc` para
  sockets, `cmd.exe` real): en Windows debería ir verde sin tocar nada.
- Las **épicas 10 y 11 están completas** y verificadas.
- La **12.1 está cerrada**: el spike de la raíz de confianza dio resultado positivo (89
  pruebas de la cadena firma → manifiesto → paquete en verde), así que **el MVP ya no está
  bloqueado por la confianza de paquetes**.
- El **producto es Windows x64**: medir ahí no es solo más realista, es medir en la
  plataforma objetivo.
