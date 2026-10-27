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
`Microsoft.Data.Sqlite` 10.0.13, `xunit.v3` 3.2.2, `xunit.runner.visualstudio` 3.1.5 y
`Microsoft.NET.Test.Sdk` 18.11.0.

## Estructura

```text
LANPartyHub/
  global.json                 # SDK pineado
  LANPartyHub.sln             # solución con los seis proyectos
  src/
    Shared/                   # solo contratos: DTOs, enumeraciones, eventos, versión
    Server/                   # servidor del evento (ASP.NET Core, monolito modular)
      Domain/ Application/ Infrastructure/
    Client/                   # cliente de escritorio (Avalonia, MVVM)
      Domain/ Application/ Infrastructure/ Presentation/
  tests/
    UnitTests/                # dominio, máquinas de estado, estructura del repositorio
    IntegrationTests/         # SQLite real en archivo
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
