# LAN Party Hub

Plataforma de escritorio para distribuir, instalar y lanzar videojuegos sobre una LAN
administrada, sin depender de Internet.

El repositorio está en fase de **Fase F (implementación)** y arranca por la épica 1, que fija
la forma del sistema: la solución .NET 10 con tres proyectos de producción, tres proyectos de
prueba y los contratos compartidos que consumen el resto del backlog.

## Requisitos

- **SDK de .NET 10.0.403**, pineado en `global.json` con `rollForward: latestFeature`. Sin ese
  fichero, `dotnet` resuelve el SDK más reciente instalado y la compilación deja de ser
  reproducible.
- Runtime de .NET 10.0.x (el de ASP.NET Core incluido). No se fija ninguna versión de parche
  en ningún fichero de proyecto.
- Acceso a NuGet.org para restaurar los paquetes.

Paquetes fijados a versión exacta (sin versiones flotantes): `Avalonia` 12.1.4,
`Microsoft.Data.Sqlite` 10.0.13, `Microsoft.EntityFrameworkCore` 10.0.13,
`Microsoft.EntityFrameworkCore.Sqlite` 10.0.13, `Microsoft.EntityFrameworkCore.Design`
10.0.13, `Microsoft.Extensions.Configuration` 10.0.13,
`Microsoft.Extensions.Configuration.Json` 10.0.13,
`Microsoft.Extensions.Configuration.EnvironmentVariables` 10.0.13,
`Microsoft.Extensions.Logging` 10.0.13, `Microsoft.Extensions.Logging.Console` 10.0.13,
`Haukcode.Mdns` 1.0.22, `xunit.v3` 3.2.2, `xunit.runner.visualstudio` 3.1.5 y
`Microsoft.NET.Test.Sdk` 18.11.0.

## Arrancar el servidor

El servidor se arranca sin instalar nada y expone **solo** los puertos que necesita
(AD-12): el puerto HTTP de la API y del hub de SignalR, y el UDP 5353 del anuncio mDNS.
Escucha **únicamente en interfaces privadas** (RFC 1918 y ULA): si el equipo no tiene
ninguna, el servidor no arranca, porque enlazar a `0.0.0.0` expondría el hub fuera de la red
del evento.

```powershell
dotnet run --project src\Server\LANPartyHub.Server.csproj -- --data-dir D:\datos --http-port 8080
```

O, con el binario ya publicado:

```powershell
.\LANPartyHub.Server.exe --data-dir D:\datos --http-port 8080
```

Los **únicos** dos flags del servidor son `--data-dir` y `--http-port`. Un flag desconocido,
un valor ausente y un puerto fuera del rango 1-65535 abortan el arranque con un mensaje; no
se ignoran en silencio. El servidor no expone ningún flag, selector ni puerto de motor de
transferencia P2P, porque en el MVP solo existe el camino HTTP (AD-4, AD-28).

### Configuración efectiva

La precedencia es `flags > env > archivo` (AD-12, AR-23): los proveedores se registran en ese
orden y el último que tiene la clave gana.

| Clave | Variable de entorno | Valor por defecto | Qué es |
|---|---|---|---|
| `Server:dataDirectory` | `Server__dataDirectory` | `data` | Carpeta de datos del servidor |
| `Server:httpPort` | `Server__httpPort` | `8080` | Puerto TCP de la API y del hub de SignalR |

Las variables de entorno usan `__` como separador de sección. Una ruta relativa se resuelve
contra la carpeta del binario, de modo que la carpeta de datos acompaña al ejecutable. El
fichero opcional `appsettings.json`, junto al binario, es la fuente de menor prioridad.

`ASPNETCORE_URLS` se **ignora a propósito**: el servidor enlaza únicamente las interfaces
privadas de la máquina (AD-12) y Kestrel tiene `PreferHostingUrls` desactivado de forma
explícita, de modo que una URL de hosting no puede convertir el enlace en `0.0.0.0`. Si el
operador exporta esa variable, no tendrá efecto y el arranque no lo advierte.

### Carpeta de datos

La carpeta de datos se crea en el arranque y **sobrevive al reemplazo del binario**
(AD-17). Contiene:

```text
<data-dir>/
  catalog.sqlite      # base de datos del servidor: el catálogo y la vista de organización
  packages/           # repositorio de los paquetes publicados
```

El esquema de `catalog.sqlite` se materializa con **migraciones versionadas de EF Core** que
se aplican automáticamente al arrancar (AD-11). Las ocho tablas del servidor son `GAME`,
`PACKAGE_DESCRIPTOR`, `INSTALLATION_MANIFEST`, `CLIENT_REGISTRATION`,
`CLIENT_INSTALLATION_VIEW`, `DOWNLOAD_INTENT`, `CLIENT_ERROR` y `CATALOG_VERSION`.

`CATALOG_VERSION` tiene **una sola fila**: su columna `catalogVersion` es la versión
monótona creciente del catálogo, y crece **en la misma transacción** que la publicación del
paquete o la retirada del juego que la provocan (AD-26). La retirada de un juego
**marca** sus paquetes con `PACKAGE_DESCRIPTOR.withdrawnAt` y **no borra** ninguna fila
(AD-26, AD-34).

Cada historia que necesite más columnas añade su migración; la herramienta tiene que coincidir
con la versión de los paquetes de EF Core:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.13
dotnet ef migrations add <nombre> --project src\Server\LANPartyHub.Server.csproj `
    --startup-project src\Server\LANPartyHub.Server.csproj `
    --output-dir Infrastructure\Persistence\Migrations
```

El esquema se materializa **una sola vez**, desde el ERD de la espina. El servidor nunca usa
`EnsureCreated()`: esquiva el historial de migraciones y hace que `Migrate()` falle después.

### Observabilidad: correlación, log y métricas

El log de los **dos** binarios es **JSON estructurado** desde el primer arranque (AD-15).
Cada entrada lleva su marca de tiempo en UTC ISO-8601, su nivel, su categoría y su
mensaje. Las que se escriben dentro de una operación llevan además el **ámbito** de
correlación:

```json
{"Timestamp":"2026-10-28T09:15:03.1234567+00:00","EventId":0,"LogLevel":"Information","Category":"LANPartyHub.Server.Infrastructure.Http.CorrelationMiddleware","Message":"Petición de la superficie de cliente atendida con el estado 200.","State":{"StatusCode":200,"{OriginalFormat}":"..."},"Scopes":[{"Message":"CorrelationScope","OperationId":"…","ClientId":"…"}]}
```

El ámbito lleva dos campos y solo dos: `OperationId`, que identifica la **unidad de
trabajo**, y `ClientId`, que identifica al **equipo**. Los dos viajan en el ámbito y
nunca en el texto del mensaje.

Las entradas que se emiten antes de que exista un cliente al que correlacionar —el
arranque del host y la configuración efectiva del cliente— no llevan ámbito: no hay
identificador que correlacionar.

#### Cabeceras de correlación

Toda petición del cliente a la superficie REST `/api/v1` lleva dos cabeceras:

| Cabecera | Qué es | Qué pasa si falta o no es válida |
|---|---|---|
| `X-Client-Id` | Identificador técnico del equipo, en su forma canónica de 32 hexadecimales en minúsculas (AD-23) | La petición se rechaza con **400** y un `ApiError` del catálogo: `CLIENT_ID_MISSING` si no llega, `CLIENT_ID_INVALID` si no lleva la forma canónica |
| `X-Operation-Id` | Identificador de la unidad de trabajo en curso, en la misma forma canónica (AD-12) | El servidor genera uno por petición —un UUID v7— y lo correlaciona |

El identificador efectivo de la unidad de trabajo **vuelve al cliente** en la cabecera
`X-Operation-Id` de la respuesta, aunque el cliente no mandara ninguno: con él, el log del
servidor y el log del cliente se pueden unir.

Un `clientId` que el servidor no conoce **se acepta**: es un cliente nuevo (AD-23).

Hoy el cliente **todavía no habla con el servidor**: no tiene cliente HTTP. Lo que existe es
el *sender* que estampa las cabeceras (`CorrelationHeadersHandler`), que el futuro cliente
HTTP instalará en su `HttpClient` con la dirección base que le dé el jugador. Lo que el
servidor exige es que toda petición que llegue a `/api/v1` traiga las cabeceras.

El `clientId` es una **clave de correlación**, no una credencial: no autentica nada, no
habilita ningún control de acceso y no concede ningún permiso (AD-30). La superficie del
host, `/metrics`, **no** es superficie de cliente: se atiende sin cabeceras, para que el
diagnóstico del servidor nunca dependa del identificador de un equipo.

#### Métricas del host

Las tres métricas se exponen **localmente**, en el mismo puerto HTTP, en `/metrics`:

```json
{"transferRateBytesPerSecond":0,"activeOperations":0,"errorsByCode":{"CLIENT_ID_MISSING":1}}
```

- `transferRateBytesPerSecond`: los bytes que el host escribe divididos por el tiempo que la
  ventana cubre de verdad. La ventana mira los últimos cinco segundos, y el divisor es el
  tiempo transcurrido de verdad, así que la tasa **decae sola** cuando el host deja de
  transferir.
- `activeOperations`: las unidades de trabajo en curso.
- `errorsByCode`: los errores contados por código estable del catálogo.

**Ninguna telemetría sale a ningún servicio externo** (NFR-10): las métricas se publican
en la máquina del servidor y nada más.

#### Ver el log, el rechazo y las métricas

El log del servidor sale por la consola en JSON, así que se ve con el servidor en marcha:

```powershell
dotnet run --project src\Server\LANPartyHub.Server.csproj -- --data-dir D:\datos --http-port 8080
```

Una petición **sin** `X-Client-Id` se rechaza con **400** y con el `ApiError` del catálogo:

```powershell
curl.exe -i http://192.168.1.10:8080/api/v1/catalog
```

```json
{"code":"CLIENT_ID_MISSING","message":"La petición no lleva el identificador del cliente","cause":"La cabecera X-Client-Id no está en la petición o llega vacía.","nextStep":"Añade la cabecera X-Client-Id con el identificador del equipo y vuelve a intentarlo."}
```

Y las métricas se leen en el mismo puerto:

```powershell
curl.exe http://192.168.1.10:8080/metrics
```

## Arrancar el cliente

El cliente es una aplicación de escritorio de Avalonia. Arranca sin instalar nada y en el
primer arranque crea su carpeta de datos, aplica las migraciones de su base de datos y emite
su identificador de equipo (AD-9, AD-17, AD-23).

```powershell
dotnet run --project src\Client\LANPartyHub.Client.csproj -- --server-address 192.168.1.10:8080
```

O, con el binario ya publicado:

```powershell
.\LANPartyHub.Client.exe --install-directory D:\juegos
```

Los **únicos** tres flags del cliente son `--server-address`, `--install-directory` y
`--theme`. Un flag desconocido, un valor ausente y un valor vacío abortan el arranque con un
mensaje y con el código de salida 1; no se ignoran en silencio. El cliente no expone ningún
flag, selector ni puerto de motor de transferencia P2P, porque en el MVP solo existe el camino
HTTP (AD-4, AD-28).

### Configuración efectiva del cliente

La precedencia es `flags > env > archivo` (AD-12, AR-23): los proveedores se registran en ese
orden y el último que tiene la clave gana.

| Clave | Variable de entorno | Valor por defecto | Qué es |
|---|---|---|---|
| `Client:serverAddress` | `Client__serverAddress` | — | Dirección del servidor del evento; sin ella el cliente usa el descubrimiento o la dirección manual, que son vías equivalentes (AD-3) |
| `Client:installDirectory` | `Client__installDirectory` | `<carpeta de datos>\install` | Raíz de instalación: el único directorio confinado (AD-9) |
| `Client:theme` | `Client__theme` | `System` | Tema de la interfaz |

El fichero de configuración del cliente se llama **`client.appsettings.json`** y vive junto al
binario. No se llama `appsettings.json` como el del servidor porque los dos proyectos caen en
la misma carpeta de salida de los proyectos de prueba que los referencian, y un fichero
sustituiría al otro.

**El formato de `serverAddress` es `host:puerto`**: el host no puede estar vacío y el puerto es
un número entero entre 1 y 65535 (`192.168.1.10:8080`, `servidor:8080`, `[::1]:8080`). Un
valor con otro formato aborta el arranque con un mensaje que muestra el formato esperado. El
host y el puerto viajan por separado en el contrato `ServerEndpoint` de `Shared`.

Las variables de entorno usan `__` como separador de sección. Una ruta relativa de
`installDirectory` se resuelve contra la carpeta de datos del cliente. El fichero
`client.appsettings.json`, junto al binario, es la fuente de menor prioridad.

El tema se resuelve y se publica en el arranque; la capa de presentación lo aplica en las
historias siguientes.

### Cambiar el directorio de instalación

El directorio de instalación se cambia **en Configuración**, en el campo *Directorio de
instalación* de la ventana del cliente. Al guardar:

1. el valor se escribe en `client.appsettings.json`, junto al binario, y **se conservan las
   demás claves** del fichero;
2. el valor se aplica **en el acto**: la carpeta nueva se crea y pasa a ser el único
   directorio confinado por la regla de extract (AD-9). El `install/` de la carpeta de datos
   sigue existiendo y se queda sin usar;
3. el siguiente arranque lee el valor persistido.

El valor persistido vive en la capa de menor prioridad de la configuración, así que **un flag
o una variable de entorno siguen ganando sobre él** en el arranque. El resto de la superficie
de Configuración (la conexión, el tema y el log de diagnósticos) es de la épica 11 y no entra
en este cambio.

### Carpeta de datos del cliente

La carpeta de datos del cliente es `%LOCALAPPDATA%\LanPartyHub` y **sobrevive al reemplazo
del binario** (AD-17). Contiene:

```text
%LOCALAPPDATA%\LanPartyHub/
  install/             # raíz de instalación por defecto: se deja sin usar si se configura otra
  downloads/           # descargas parciales (*.part): ruta fija y fuera de la raíz
  manifests/           # caché de manifiestos firmados
  state.sqlite         # base de datos del cliente: su identidad y sus operaciones
```

El `install/` de la carpeta de datos es la raíz de instalación **por defecto** y **siempre
existe**: la carpeta de datos contiene exactamente esas cuatro entradas (AD-9, AD-17). La raíz
que confina la instalación es la **efectiva**, y la configuración puede apuntar a otra
carpeta; en ese caso `install/` se queda sin usar y el directorio nuevo es el único
confinado.

Los archivos parciales viven **solo** en `downloads/`, nunca en la raíz de instalación ni en
`%TEMP%`, para que el progreso persistido sobreviva al reinicio (AD-9, AD-11).

El esquema de `state.sqlite` se materializa con **migraciones versionadas de EF Core** que se
aplican automáticamente al arrancar (AD-11). Las cuatro tablas del cliente son
`CLIENT_IDENTITY`, `CLIENT_INSTALLATION`, `DOWNLOAD_OPERATION` y `DOWNLOAD_PROGRESS`; **ninguna
tabla del servidor se replica** en el archivo del cliente (AD-22).

La herramienta tiene que coincidir con la versión de los paquetes de EF Core:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.13
dotnet ef migrations add <nombre> --project src\Client\LANPartyHub.Client.csproj `
    --startup-project src\Client\LANPartyHub.Client.csproj `
    --output-dir Infrastructure\Persistence\Migrations
```

### Identidad del equipo

El `clientId` es un **UUID v7** que el cliente emite **una sola vez** y que persiste en
`CLIENT_IDENTITY`, en su forma canónica: los 32 dígitos hexadecimales en minúsculas de
`Guid.ToString("N")`, sin guiones (AD-23, AD-31). No se regenera al reiniciar ni al
reinstalar el binario mientras la carpeta de datos sobreviva; si se borra la carpeta de datos,
el equipo aparece como cliente nuevo, que es el comportamiento correcto de un identificador
persistente.

El identificador viaja en la cabecera `X-Client-Id` de **toda** petición a `/api/v1`, y el
del cliente lo pone también en el ámbito del log, de modo que cada entrada del cliente
lleva su `clientId` y el `operationId` de la operación que la escribió (AD-15, AD-23). El
*sender* que estampa la cabecera es `CorrelationHeadersHandler`; el cliente todavía no
habla con el servidor, así que hoy lo estampa quien construya su cliente HTTP.

### Log del cliente

El log del cliente es **JSON**, con los mismos campos que el del servidor, y su fábrica
vive **toda la sesión**: el log de un cliente que dura horas no se evapora al terminar el
arranque (AD-15).

El cliente es una aplicación de escritorio y no tiene consola cuando el jugador la lanza
desde el escritorio, así que el log se escribe **además** en un fichero JSON junto al
binario, al lado del `client.appsettings.json`:

```text
<carpeta del binario>/
  client.appsettings.json   # configuración del cliente
  client.log                # log del cliente, en JSON
  client.log.1              # la rotación anterior, cuando el log llega a su tamaño máximo
```

El fichero rota cuando llega a **1 MiB** y se queda con la rotación anterior, así que el
espacio que el log toma en el disco está acotado.

El log del cliente se ve cuando se arranca desde una terminal:

```powershell
dotnet run --project src\Client\LANPartyHub.Client.csproj -- --server-address 192.168.1.10:8080
```

### Publicar el artefacto del cliente

El artefacto es **self-contained single-file para Windows x64** (AD-17) y `win-x64.pubxml` es
el único perfil de publicación:

```powershell
dotnet publish src\Client\LANPartyHub.Client.csproj -p:PublishProfile=win-x64 -o .\publicacion
```

Sale un único `LANPartyHub.Client.exe` que no necesita runtime instalado, junto al
`appsettings.json` opcional. El MVP no declara soporte para ninguna otra plataforma (NFR-4).

## Estructura

```text
LANPartyHub/
  global.json                 # SDK pineado
  LANPartyHub.sln             # solución con los seis proyectos
  src/
    Shared/                   # solo contratos: DTOs, enumeraciones, eventos, versión
    Server/                   # servidor del evento (ASP.NET Core, monolito modular)
      Domain/
      Application/Ports/      # puertos: contadores del host
      Infrastructure/         # adaptadores: red y persistencia
        Http/                 # correlación de las peticiones y bytes de la respuesta
        Logging/              # ámbito de correlación del servidor
      Host/                   # composición del host: configuración, métricas, arranque
    Client/                   # cliente de escritorio (Avalonia, MVVM)
      Domain/
      Application/Ports/      # puertos: layout, estado, correlación y cabeceras
      Infrastructure/         # adaptadores: disco, base de datos, configuración y red
        Logging/              # log del cliente en fichero JSON
        Networking/           # cabeceras de correlación de las peticiones
      Presentation/           # ventana y view models
      Host/                   # composición del cliente: configuración, arranque y correlación
  tests/
    UnitTests/                # dominio, máquinas de estado, estructura del repositorio
    IntegrationTests/         # SQLite real en archivo, arranque real del servidor y del cliente
    EndToEndTests/            # escenario de aceptación SM-1 en LAN real
  docs/
    estrategia-pruebas.md     # niveles de prueba y clasificación de casos
```

Reglas estructurales que la propia suite verifica:

- `Shared` **no** referencia ningún otro proyecto, **ningún paquete** y **ninguna referencia de
  framework**: solo contiene contratos.
- `Server` y `Client` referencian `Shared` y **no duplican ninguno de sus contratos**, tampoco
  como tipo `internal`: la comprobación recorre todos los tipos declarados de cada ensamblado.
- Las dependencias apuntan hacia dentro, y la comprobación es **en las dos direcciones**: ningún
  tipo de `Domain` importa EF Core, Avalonia, HTTP ni un adaptador; ningún tipo de
  `Application` menciona `Infrastructure` o `Presentation`; ningún tipo de `Infrastructure`
  menciona `Presentation`.
- La capa `Application` no importa EF Core, SQLite, sockets ni el cliente HTTP: el log del
  caso de uso es el único framework que admite.
- El proyecto del MVP **no** referencia MonoTorrent ni ninguna biblioteca P2P (AD-28).
- El SDK está pineado en `global.json` (`10.0.403`, `rollForward: latestFeature`) y el fichero
  de solución declara los seis proyectos.

## Compilar y probar

```powershell
dotnet restore LANPartyHub.sln
dotnet build LANPartyHub.sln
dotnet test LANPartyHub.sln
```

`dotnet test` ejecuta los tres niveles de prueba. El escenario E2E se omite, con el motivo
explícito, mientras no exista un servidor real del evento en la LAN: la precondición se
anuncia con la variable de entorno `LAN_PARTY_HUB_E2E_SERVER`.

## Guardas del repositorio

```powershell
powershell -NoProfile -File tools/check-encoding.ps1   # sin CJK ni U+FFFD en el Markdown
powershell -NoProfile -File tools/check-epics.ps1      # integridad del backlog de épicas
```

Y, cuando se toca el esquema del servidor o el del cliente, la comprobación de que el modelo
y la última migración siguen de acuerdo:

```powershell
dotnet ef migrations has-pending-model-changes --project src\Server\LANPartyHub.Server.csproj `
    --startup-project src\Server\LANPartyHub.Server.csproj

dotnet ef migrations has-pending-model-changes --project src\Client\LANPartyHub.Client.csproj `
    --startup-project src\Client\LANPartyHub.Client.csproj
```

## Convenciones

- La documentación y las explicaciones están en español; los identificadores, los nombres de
  tipo, los miembros, los contratos técnicos y los comentarios del código están en inglés.
- Los identificadores son cadenas opacas UUID v7, las fechas son UTC y `sizeBytes` son bytes
  exactos.
- La microcopia de estado sale solo de `StateDisplayMap` y los errores visibles solo de
  `ErrorCatalog`, ambos en `Shared`.
- El log de los dos binarios es JSON y lleva el ámbito de correlación con el `operationId`
  y el `clientId`. Ningún log contiene credenciales, y el código de un error aparece
  siempre como `code` del catálogo, nunca como texto principal.
- Los documentos de planificación viven en `_bmad-output/planning-artifacts/`; la espina de
  arquitectura (`ARCHITECTURE-SPINE.md`) es la referencia de las decisiones y se lee **antes**
  de tocar un módulo, un contrato o una entidad.

## Documentación relacionada

- Estrategia de pruebas y clasificación de casos: `docs/estrategia-pruebas.md`.
- Prompt maestro y checklist de casos: `docs/plan.md`.
- Espina de arquitectura (AD-1 a AD-34): `_bmad-output/planning-artifacts/architecture/`.
- Backlog ejecutable: `_bmad-output/planning-artifacts/epics/`.
