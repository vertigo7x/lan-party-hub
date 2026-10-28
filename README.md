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
se aplican automáticamente al arrancar (AD-11). Las siete tablas del servidor son `GAME`,
`PACKAGE_DESCRIPTOR`, `INSTALLATION_MANIFEST`, `CLIENT_REGISTRATION`,
`CLIENT_INSTALLATION_VIEW`, `DOWNLOAD_INTENT` y `CLIENT_ERROR`.

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

### Métricas y diagnóstico

- El log del servidor es **JSON** desde el primer arranque (AD-15).
- Las métricas del host se exponen **localmente** en el mismo puerto HTTP, en `/metrics`:
  tasa de transferencia, operaciones activas y errores por código.
- **Los contadores están inertes en esta historia**: la forma es la que fija AD-15, pero los
  valores los instrumenta la Story 1.4. Hasta entonces `/metrics` devuelve ceros y el
  diccionario de errores vacío; no es un servidor roto.

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
      Domain/ Application/ Infrastructure/
      Host/                   # composición del host: configuración, métricas, arranque
    Client/                   # cliente de escritorio (Avalonia, MVVM)
      Domain/ Application/ Infrastructure/ Presentation/
      Host/                   # composición del cliente: configuración y arranque
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
- Los documentos de planificación viven en `_bmad-output/planning-artifacts/`; la espina de
  arquitectura (`ARCHITECTURE-SPINE.md`) es la referencia de las decisiones y se lee **antes**
  de tocar un módulo, un contrato o una entidad.

## Documentación relacionada

- Estrategia de pruebas y clasificación de casos: `docs/estrategia-pruebas.md`.
- Prompt maestro y checklist de casos: `docs/plan.md`.
- Espina de arquitectura (AD-1 a AD-34): `_bmad-output/planning-artifacts/architecture/`.
- Backlog ejecutable: `_bmad-output/planning-artifacts/epics/`.
