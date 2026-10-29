---
title: 'Control de la publicación por parte del organizador'
type: 'feature'
created: '2026-10-29'
status: 'in-progress'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '468040c'
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-lan-party-hub-2026-10-26/ARCHITECTURE-SPINE.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** El organizador no puede ver el catálogo con la información que necesita (juego, versión, bytes exactos, licencia y restricciones) ni retirar un juego del catálogo, y el servidor no lleva el `catalogVersion` monótono creciente que el snapshot del cliente necesitará (AC1, AC2, AC3).

**Approach:** Cerrar el control de la publicación dentro del proceso del servidor: el listado enriquecido como tipo interno del servidor (sin tocar `Shared`), la retirada como una marca que no borra filas, y un contador `catalogVersion` que crece en la misma transacción que cada publicación y cada retirada.

## Boundaries & Constraints

**Always:**
- `Shared` **no se toca**: sus 20 contratos son fijos (AD-18). El listado enriquecido es un tipo interno de `Server`.
- La retirada **marca**, no borra: no se borra ninguna fila de `PACKAGE_DESCRIPTOR`, `GAME`, `INSTALLATION_MANIFEST` ni `CLIENT_INSTALLATION_VIEW` (AC2, AD-26).
- `withdrawnAt` vive en `PACKAGE_DESCRIPTOR`, **no** en `GAME`: la tabla `GAME` persiste exactamente los 18 campos de `GameDescriptor`, con el mismo nombre y el mismo orden (AD-34).
- El `catalogVersion` es monótono creciente y **crece en la misma transacción** que la escritura que lo provoca (AC3).
- `sizeBytes` en bytes exactos; la conversión a GB decimales es regla de presentación (AD-7).
- La etiqueta de la retirada reutiliza el texto de `StateDisplayMap.GetText(OperationOutcome.PackageWithdrawn)` (AD-25, AD-33).
- La acción «Retirar del catálogo» es visible y alcanzable por teclado (UX-DR-24, UX-DR-25).
- Código, identificadores y contratos en inglés; documentación y textos de UI en español.

**Never:**
- No se añade ruta HTTP: el organizador opera en proceso (AD-1, AD-5).
- No se implementa el snapshot del catálogo ni `CatalogChanged` (Story 3.1).
- No se toca la descarga, la instalación, la proyección del servidor ni la autenticación (AD-6).
- No se modifica la migración inicial `20261027133118_InitialCatalogSchema`.
- No se reescribe `PackageDescriptor` ni el manifiesto firmado (AD-2, AD-7).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| LISTADO | Paquetes publicados con su juego | Fila con juego, versión, bytes exactos, licencia, restricciones y estado de retirada | Sin error |
| PUBLICACIÓN | Paquete publicado | `catalogVersion` incrementa en la misma transacción | Sin error |
| RETIRADA | Juego con paquetes publicados | Los paquetes quedan marcados, el `catalogVersion` incrementa y no se borra ninguna fila | Sin error |
| RETIRADA_DE_UN_JUEGO_INEXISTENTE | `gameId` que no está en `GAME` | No se escribe nada y no se incrementa la versión | Excepción de dominio tipada |
| REINCORPORACIÓN | Juego retirado | El juego vuelve publicando una versión nueva del paquete | Sin error |

</frozen-after-approval>

## Code Map

- `src/Server/Application/Ports/IPackageRepository.cs` -- puerto: añadir la fila de listado enriquecido (`PublishedPackageRow`), la retirada y la lectura del `catalogVersion`.
- `src/Server/Application/Packages/ListPackages.cs` -- cambiar el tipo de retorno del listado a la fila enriquecida interna del servidor (deja de devolver `PackageDescriptor`).
- `src/Server/Application/Packages/WithdrawGame.cs` -- **nuevo** caso de uso de la retirada.
- `src/Server/Infrastructure/Persistence/PackageRepository.cs` -- `join` con `GAME`, marca de retirada, incremento del contador en la misma transacción.
- `src/Server/Infrastructure/Persistence/CatalogDbContext.cs` -- `DbSet<CatalogVersionEntity>` y mapeo de `withdrawnAt` y de `CATALOG_VERSION`.
- `src/Server/Infrastructure/Persistence/Entities/PackageDescriptorEntity.cs` -- añadir `WithdrawnAt`.
- `src/Server/Infrastructure/Persistence/Entities/CatalogVersionEntity.cs` -- **nuevo**, fila única con `catalogVersion`.
- `src/Server/Infrastructure/Persistence/Migrations/<nueva>_CatalogVersionAndWithdrawal.cs` -- tabla `CATALOG_VERSION` y columna `PACKAGE_DESCRIPTOR.withdrawnAt`.
- `src/Server/Domain/Packages/PackageDomainExceptions.cs` -- excepción de dominio de la retirada.
- `src/Server/Presentation/Packages/PackagesViewModel.cs` -- fila con juego, licencia, restricciones y estado de retirada.
- `src/Server/Presentation/Packages/PackagesWindow.cs` -- acción «Retirar del catálogo» por fila.
- `src/Server/Presentation/ServerApp.cs` -- resolver `WithdrawGame` desde el scope.
- `src/Server/Program.cs` -- registrar `WithdrawGame`.
- `tests/UnitTests/PackagePresentationTests.cs` -- AC1 de la fila enriquecida.
- `tests/UnitTests/PackagePublicationDomainTests.cs` -- AC3 del contador sobre el caso de uso real.
- `tests/UnitTests/CatalogWithdrawalTests.cs` -- **nuevo**, AC2 de la retirada sobre el caso de uso real.
- `tests/IntegrationTests/PackagePublicationTests.cs` -- AC3 sobre `catalog.sqlite` real.
- `tests/IntegrationTests/CatalogWithdrawalTests.cs` -- **nuevo**, AC2 sobre `catalog.sqlite` real.
- `tests/IntegrationTests/ServerStartupTests.cs` -- actualizar la lista de tablas y las columnas de `PACKAGE_DESCRIPTOR`.
- `_bmad-output/planning-artifacts/architecture/architecture-lan-party-hub-2026-10-26/ARCHITECTURE-SPINE.md` -- ERD, AD-26 y AD-34.
- `src/Server/Infrastructure/Persistence/Entities/GameEntity.cs`, `ClientInstallationView.cs` -- **no se tocan**: `GAME` y la proyección no cambian.
- `src/Shared/Contracts/**` -- **no se toca**: 20 contratos fijos (AD-18).

## Tasks & Acceptance

**Execution:**
- [x] `src/Server/Infrastructure/Persistence/Entities/CatalogVersionEntity.cs` -- fila única con `catalogVersion`.
- [x] `src/Server/Infrastructure/Persistence/Entities/PackageDescriptorEntity.cs` -- añadir `WithdrawnAt` (nullable, ISO-8601 UTC).
- [x] `src/Server/Infrastructure/Persistence/CatalogDbContext.cs` -- `DbSet<CatalogVersionEntity>`, tabla `CATALOG_VERSION` con la fila única y columna `withdrawnAt`.
- [x] `src/Server/Infrastructure/Persistence/Migrations/` -- migración nueva con `CATALOG_VERSION` y `PACKAGE_DESCRIPTOR.withdrawnAt`; actualizar el snapshot del modelo.
- [x] `src/Server/Application/Ports/IPackageRepository.cs` -- `PublishedPackageRow` (juego, versión, bytes, licencia, restricciones, retirada), `WithdrawGameAsync`, `GetCatalogVersionAsync`.
- [x] `src/Server/Infrastructure/Persistence/PackageRepository.cs` -- listado con `join` a `GAME`; retirada que marca sin borrar; incremento del `catalogVersion` en la misma transacción de la escritura.
- [x] `src/Server/Application/Packages/PublishPackage.cs` -- la publicación incrementa el `catalogVersion` en la misma transacción.
- [x] `src/Server/Application/Packages/WithdrawGame.cs` -- caso de uso que marca los paquetes publicados del juego e incrementa la versión.
- [x] `src/Server/Domain/Packages/PackageDomainExceptions.cs` -- excepción tipada de la retirada de un juego inexistente.
- [x] `src/Server/Presentation/Packages/PackagesViewModel.cs` -- fila enriquecida y estado de retirada.
- [x] `src/Server/Presentation/Packages/PackagesWindow.cs` -- acción «Retirar del catálogo» por fila, alcanzable por teclado.
- [x] `src/Server/Program.cs` y `src/Server/Presentation/ServerApp.cs` -- registrar y resolver `WithdrawGame`.
- [x] `tests/UnitTests/CatalogWithdrawalTests.cs`, `tests/UnitTests/PackagePublicationDomainTests.cs`, `tests/UnitTests/PackagePresentationTests.cs` -- pruebas unitarias de AC1, AC2 y AC3.
- [x] `tests/IntegrationTests/CatalogWithdrawalTests.cs`, `tests/IntegrationTests/PackagePublicationTests.cs`, `tests/IntegrationTests/ServerStartupTests.cs` -- pruebas de integración sobre `catalog.sqlite` real.
- [x] `_bmad-output/planning-artifacts/architecture/architecture-lan-party-hub-2026-10-26/ARCHITECTURE-SPINE.md` y `README.md` -- ERD, AD-26, AD-34 y el recuento de tablas.

**Acceptance Criteria:**
- Given la sección Paquetes del servidor, when el organizador lista los paquetes publicados, then ve por cada uno el juego asociado, la versión, el tamaño exacto en bytes y la información de licencia y restricciones (FR-1, UX-DR-24).
- Given un paquete publicado, when el organizador retira un juego del catálogo, then la retirada no borra la instalación existente de ningún cliente y el servidor tampoco borra la proyección de instalación de ese cliente (AR-16, AD-26).
- Given una publicación, when se completa, then el `catalogVersion` del servidor incrementa de forma monótona creciente (AR-16, AD-26).

## Implementation Notes

### Qué se implementó

- **Esquema.** Migración nueva `20261029195500_CatalogVersionAndWithdrawal` (generada con `dotnet ef` 10.0.13, ya instalado) que añade la columna anulable `PACKAGE_DESCRIPTOR.withdrawnAt` y crea la tabla `CATALOG_VERSION`. La migración inicial `20261027133118_InitialCatalogSchema` **no se ha tocado**. `GAME` conserva **exactamente** sus 18 columnas, con el mismo nombre y el mismo orden (AD-34).
- **Puerto.** `IPackageRepository` gana `PublishedPackageRow` (tipo **interno** del servidor: juego, versión, bytes exactos, licencia, restricciones de distribución y marca de retirada), `WithdrawalResult` y los miembros `ListPublishedAsync`, `WithdrawGameAsync` y `GetCatalogVersionAsync`. `PublishedPackageRow` **no** colisiona con ninguno de los 20 contratos de `Shared`, que quedan intactos (AD-18).
- **Repositorio.** `PackageRepository` lista con el `join` a `GAME` (a través de la propiedad de navegación `Game`), **marca** la retirada sin borrar filas (`ExecuteUpdateAsync` sobre `withdrawnAt`) y **incrementa `catalogVersion` dentro de la misma transacción EF** que la escritura que lo provoca: en `AddAsync` (publicación) y en `WithdrawGameAsync` (retirada). `PublishPackage` no cambia: el incremento vive en el adaptador, que es quien posee la transacción.
- **Casos de uso.** `ListPackages` devuelve ahora la fila enriquecida interna en lugar de `PackageDescriptor`. `WithdrawGame` (nuevo) rechaza el juego inexistente con `GameWithdrawalNotFoundException` **antes** de cualquier escritura y marca los paquetes publicados del juego.
- **Presentación.** `PackagesViewModel` construye una fila con juego, versión, **bytes exactos**, valor derivado en GB decimales, licencia, restricciones y el estado de retirada; la etiqueta de la retirada reutiliza `StateDisplayMap.GetText(OperationOutcome.PackageWithdrawn)` de `Shared`, sin duplicar la microcopia (AD-25, AD-33). `PackagesWindow` añade el botón por fila «Retirar del catálogo», que retira el **videogame** de la fila, y muestra el estado de la retirada.
- **Composición.** `WithdrawGame` registrado en `Program.cs` y resuelto en `ServerApp.cs`; la prueba de integración de la raíz de composición comprueba que se resuelve.
- **Pruebas.** `tests/UnitTests/CatalogWithdrawalTests.cs` (nuevo), `tests/IntegrationTests/CatalogWithdrawalTests.cs` (nuevo, sobre `catalog.sqlite` real), más las actualizaciones de `PackagePublicationDomainTests.cs`, `PackagePresentationTests.cs`, `PackagePublicationTests.cs` y `ServerStartupTests.cs`.

### Decisiones de diseño que conviene revisar

1. **`withdrawnAt` es una cadena anulable en UTC ISO-8601**, y no un `DateTimeOffset`. Se eligió por **consistencia** con el resto de los instantes persistidos del servidor (`CLIENT_REGISTRATION.lastSeenAt` y `CLIENT_ERROR.occurredAt` son texto en el mismo formato), y porque la convención de datos de la espina fija las fechas en UTC ISO-8601. El formato escrito es `yyyy-MM-ddTHH:mm:ss.fffffffZ`.
2. **`CATALOG_VERSION` tiene una columna de clave `id` además de `catalogVersion`.** Una tabla de **una sola fila** sigue necesitando clave primaria en EF Core, y sin ella el modelo no puede materializarse. La clave es un entero con el valor constante `1` (`CatalogVersionEntity.SingleRowKey`), de modo que la tabla no puede contener una segunda fila. La espina y el README documentan las dos columnas.

### Sorpresas y cosas que costaron

- **El `join` con `GAME` no se traducía a SQL** con la forma `Join(...)` sobre `DbSet`, ni con un `Select` que proyectara directamente al registro `PublishedPackageRow` ordenado por sus propiedades proyectadas: EF Core lanzaba `InvalidOperationException` al traducir la expresión. La solución fue añadir la **propiedad de navegación** `Game` a `PackageDescriptorEntity`, ordenar por las columnas de la entidad y proyectar en memoria.
- **La propiedad de navegación exigía reconfigurar la relación**, no duplicarla: declarar `HasOne<GameEntity>()` y además `HasOne(e => e.Game)` creaba una **segunda** relación con una columna de clave foránea sombra `GameId1` y una FK adicional en la tabla. Se dejó **una sola** configuración, `entity.HasOne(e => e.Game).WithMany().HasForeignKey(e => e.GameId)`, y se regeneró la migración para que no contenga esa columna.
- **La retirada usa `ExecuteUpdateAsync`, que ejecuta fuera del cambio de seguimiento de EF**, así que la transacción se abre **antes** de la sentencia; si se abriera después, la marca se confirmaría fuera de la transacción que hace crecer el contador.
- **`ListPackages` deja de devolver `PackageDescriptor`**, un contrato de `Shared`: el tipo de retorno pasa a ser la fila interna `PublishedPackageRow`, lo que obliga a actualizar la vista y sus pruebas. Es el punto de la decisión «el listado enriquecido es un tipo interno del servidor».
- **La regla de AD-22 (cada entidad tiene exactamente un dueño) obliga a registrar `CATALOG_VERSION`** en la tabla de propiedad de entidades de la espina, aunque no sea una entidad de dominio sino una fila de estado del catálogo.
- **La prueba `The_startup_is_idempotent_over_a_database_that_is_already_migrated`** afirmaba `Assert.Single(appliedMigrations)`; con la segunda migración ahora hay **dos**, así que se ajustó a `Assert.Equal(2, …)` comprobando que están las dos migraciones esperadas.

### Verificación ejecutada

Los cuatro comandos de la sección de verificación se ejecutaron sobre el árbol de trabajo final y **pasaron**: `dotnet build LANPartyHub.sln` (0 errores, 0 advertencias), `dotnet test LANPartyHub.sln` (391 unitarias, 157 de integración, 3 E2E superadas y 1 omitida), `tools/check-encoding.ps1` (121 ficheros Markdown, sin CJK ni U+FFFD) y `tools/check-epics.ps1` (13 épicas, 47 historias, 261 bloques GWT en paridad).

## Spec Change Log

## Review Triage Log

## Verification

**Commands:**
- `dotnet build LANPartyHub.sln` -- expected: SUCCESS
- `dotnet test LANPartyHub.sln` -- expected: SUCCESS
- `powershell -NoProfile -File tools/check-encoding.ps1` -- expected: SUCCESS
- `powershell -NoProfile -File tools/check-epics.ps1` -- expected: SUCCESS
