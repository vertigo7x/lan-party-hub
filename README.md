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
10.0.13, `Haukcode.Mdns` 1.0.22, `xunit.v3` 3.2.2, `xunit.runner.visualstudio` 3.1.5 y
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
  tests/
    UnitTests/                # dominio, máquinas de estado, estructura del repositorio
    IntegrationTests/         # SQLite real en archivo, arranque real del servidor
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

Y, cuando se toca el esquema del servidor, la comprobación de que el modelo y la última
migración siguen de acuerdo:

```powershell
dotnet ef migrations has-pending-model-changes --project src\Server\LANPartyHub.Server.csproj `
    --startup-project src\Server\LANPartyHub.Server.csproj
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
