---
title: 'Publicación de un paquete con licencia y manifiesto firmado'
type: 'feature'
created: '2026-10-29'
status: 'in-progress'
review_loop_iteration: 0
baseline_revision: '21d3403'
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-lan-party-hub-2026-10-26/ARCHITECTURE-SPINE.md'
warnings:
  - 'oversized'
deferred: []
---

<intent-contract>

## Intent

**Problem:** El servidor del evento no tiene forma de publicar un paquete: la tabla `PACKAGE_DESCRIPTOR` y la tabla `INSTALLATION_MANIFEST` de `catalog.sqlite` existen con sus columnas exactas, pero ningún caso de uso las escribe, no hay forma de adjuntar los bytes del paquete, no se firma ningún manifiesto y no hay ninguna comprobación que impida publicar el contenido de un videojuego cuya licencia o cuyas restricciones de distribución no constan. El resultado es que el catálogo no puede publicar nada verificable.

**Approach:** Cerrar el módulo `PackageDistribution` del servidor de extremo a extremo: un caso de uso de aplicación que valida la licencia y las restricciones del videojuego, construye el manifiesto de instalación con sus operaciones tipadas y cerradas, lo firma con Ed25519 sobre su forma canónica, persiste el `PackageDescriptor` y el `InstallationManifest` con su sobre firmado, y expone la ruta de administración que publica el paquete y la que lo sirve.

## Boundaries & Constraints

**Always:**
- La firma es **Ed25519** sobre el **manifiesto canónico** (JSON canónico: sin espacios superfluos, claves ordenadas) y el sobre firmado contiene `{ manifest, signature, keyId }` (AD-2, AD-7).
- El manifiesto firmado incluye `packageId`, `packageVersion`, `sizeBytes` y `sha256` del paquete: la correspondencia entre manifiesto y paquete queda **firmada**, no supuesta (AD-2, AD-7).
- `PackageDescriptor` **no** transporta firma: la firma vive solo en el sobre firmado del manifiesto y `keyId` solo en ese sobre (AD-2, AD-7).
- Un paquete publicado es **inmutable**: cambiar su contenido exige publicar una versión nueva (AD-7).
- Las operaciones del manifiesto son **tipadas y cerradas**: `extract`, `createDirectory`, `copyFile`, `writeConfig`, `setLaunchConfig`, `verify`. **No existe ninguna operación de ejecución de comandos arbitrarios** (AD-7, AD-8).
- La publicación se **bloquea** si al registro del videojuego le falta la información de licencia **o** las restricciones de distribución, y el rechazo explica **cuál** de los dos falta (AC1, FR-1, NFR-8).
- `sizeBytes` en **bytes exactos**; la conversión a GB decimales es regla de presentación, no del modelo (AD-7).
- `PackageDescriptor` e `InstallationManifest` son **propiedad del servidor** y viven en `catalog.sqlite`; se sirven como artefactos **aparte**, no incrustados en el binario del paquete (AD-20, AD-22).
- Toda respuesta de error usa la forma única `ApiError` = `{ code, message, cause, nextStep }` con `code` estable tomado de `ErrorCatalog` (AD-15, AD-18).
- Documentación y textos de UI en español; identificadores, nombres de tipo, miembros y comentarios de código en inglés.

**Never:**
- No se implementa `GET /api/v1/catalog`, `GET /api/v1/games/{gameId}`, `GET /api/v1/packages/{packageId}/content` ni el snapshot del catálogo: pertenecen a historias posteriores (AD-5).
- No se implementa el control de la publicación por parte del organizador (listado de paquetes, retirada de un juego, incremento de `catalogVersion`): pertenece a Story 2.3.
- No se implementa la verificación de la firma en el cliente ni la descarga del paquete: pertenecen a las Stories 6.1 y 5.2.
- No se añade ningún tipo nuevo a `Shared`: AD-18 enumera exhaustivamente sus 20 contratos y `SharedContractInventoryTests` lo fija.
- No se crea un proyecto nuevo en la solución: son exactamente 6 y AD-1 exige un único artefacto desplegable para el servidor.
- No se modifica la migración inicial `20261027133118_InitialCatalogSchema`: el esquema ya cumple AD-22.
- No se fija ni se distribuye el ancla de confianza: es Story 12.1, y AD-27 exige que el ancla se fije fuera de banda.
- No se toca el vocabulario P2P: el MVP no es P2P (AD-13, AD-28).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| PUBLICACIÓN_VÁLIDA | Videojuego con licencia y restricciones, bytes del paquete y manifiesto con operaciones cerradas | `201` con el `PackageDescriptor` persistido; filas en `PACKAGE_DESCRIPTOR` e `INSTALLATION_MANIFEST`; el `sha256` del descriptor es el de los bytes; la firma verifica contra la clave del servidor | No error |
| PUBLICACIÓN_SIN_LICENCIA | Videojuego cuya licencia está vacía | `422` con `ApiError`; el `code` nombra el campo que falta; **no** se escribe ninguna fila ni ningún archivo | `code` estable de `ErrorCatalog` |
| PUBLICACIÓN_SIN_RESTRICCIONES | Videojuego cuyas restricciones de distribución están vacías | `422` con `ApiError`; el `code` nombra el campo que falta; **no** se escribe ninguna fila ni ningún archivo | `code` estable de `ErrorCatalog` |
| VIDEOJUEGO_INEXISTENTE | `gameId` que no está en `GAME` | `404` con `ApiError`; no se escribe ninguna fila | `code` estable de `ErrorCatalog` |
| PAQUETE_INMUTABLE | Publicación con un `packageId` ya publicado | `409` con `ApiError`; la fila existente **no** se modifica | `code` estable de `ErrorCatalog` |
| MANIFIESTO_CON_OPERACIÓN_DESCONOCIDA | Manifiesto con una operación fuera del conjunto cerrado | `422` con `ApiError`; no se escribe ninguna fila ni ningún archivo | `code` estable de `ErrorCatalog` |
| PAQUETE_SIN_BYTES | El archivo de bytes del paquete no existe o su tamaño no coincide con `sizeBytes` | `422` con `ApiError`; no se escribe ninguna fila ni ningún archivo | `code` estable de `ErrorCatalog` |
| CUERPO_SIN_SCHEMA_VERSION | Cuerpo sin `schemaVersion` | `400` con `ApiError`; no se escribe ninguna fila | `code` estable de `ErrorCatalog` |
| PERSISTENCIA_FALLIDA | Error de EF Core al guardar | `500` con `ApiError`; la excepción nunca alcanza el mensaje | `code` estable de `ErrorCatalog` |

</intent-contract>

## Code Map

### Contratos de `Shared` -- CONTRATOS DE LECTURA, NO SE TOCAN

- `src/Shared/Contracts/PackageDescriptor.cs` -- **NO SE TOCA.** Ya existe con sus 7 campos en el orden de AD-7: `PackageId`, `GameId`, `Version`, `SizeBytes` (long), `ChunkSizeBytes` (long), `Sha256`, `ManifestRef`. Deliberadamente **no** lleva firma (AD-2).
- `src/Shared/Contracts/InstallationManifest.cs` -- **NO SE TOCA.** Ya existe con `SchemaVersion`, `PackageId`, `PackageVersion`, `SizeBytes`, `Sha256`, `Launch`, `Operations`. Los tipos anidados `LaunchSettings`, `ManifestOperation` (abstracta, con `[JsonPolymorphic(TypeDiscriminatorPropertyName = "operation")]` y `[JsonDerivedType]` para las seis operaciones) y `SignedEnvelope(InstallationManifest Manifest, string Signature, string KeyId)` ya existen. **El comentario XML del tipo exige que el canonicalizador de esta historia reproduzca la forma canónica byte a byte.**
- `src/Shared/Contracts/TrustAnchor.cs` -- **NO SE TOCA.** Se documenta, pero no se usa en esta historia (AD-27: el ancla se fija fuera de banda en Story 12.1).
- `src/Shared/Contracts/ApiError.cs` -- forma única del error de API `{ code, message, cause, nextStep }`. **NO SE TOCA.**
- `src/Shared/Contracts/ErrorCatalog.cs` -- **SE EXTIENDE.** Único hogar de los códigos de error. Convención a seguir: `public const string <NombrePascal>Code = "NOMBRE_EN_MAYUSCULAS";` y luego una entrada `new(codigo, mensaje, causa, siguientePaso)` en `CatalogEntries`, con los tres textos en español. El `code` viaja como campo estructurado del log, nunca dentro del mensaje (AD-25). Códigos actuales: `UNKNOWN_ERROR`, `ANCHOR_FINGERPRINT_MISMATCH`, `MANIFEST_SIGNATURE_INVALID`, `PACKAGE_INTEGRITY_MISMATCH`, `MACHINE_NAME_INVALID`, `CLIENT_ID_MISSING`, `CLIENT_ID_INVALID`, `GAME_METADATA_MISSING`, `GAME_PAYLOAD_MISSING`, `SCHEMA_VERSION_MISSING`, `SCHEMA_VERSION_NOT_SUPPORTED`, `GAME_NOT_FOUND`, `GAME_PLAYER_RANGE_INVALID` y los dieciocho códigos por campo `GAME_*_MISSING`.

### Persistencia -- YA EXISTE, NO SE TOCA

- `src/Server/Infrastructure/Persistence/Entities/PackageDescriptorEntity.cs` -- **NO SE TOCA.** Fila de `PACKAGE_DESCRIPTOR` con `PackageId` (clave), `GameId`, `Version`, `SizeBytes` (long), `ChunkSizeBytes` (long?), `Sha256`, `ManifestRef`.
- `src/Server/Infrastructure/Persistence/Entities/InstallationManifestEntity.cs` -- **NO SE TOCA.** Fila de `INSTALLATION_MANIFEST` con `ManifestId` (clave), `PackageId`, `SchemaVersion` (int), `Signature`, `KeyId`, `CanonicalManifest` (el manifiesto canónico como texto JSON).
- `src/Server/Infrastructure/Persistence/CatalogDbContext.cs` -- **NO SE TOCA.** Ya declara `DbSet<PackageDescriptorEntity> PackageDescriptors` y `DbSet<InstallationManifestEntity> InstallationManifests`, con el mapeo explícito de columnas y las FK `Restrict` a `GAME` y a `PACKAGE_DESCRIPTOR`.
- `src/Server/Infrastructure/Persistence/Migrations/20261027133118_InitialCatalogSchema.cs` -- **NO SE TOCA.** Crea `PACKAGE_DESCRIPTOR` (7 columnas) e `INSTALLATION_MANIFEST` (6 columnas). **No hace falta migración nueva.**
- `src/Server/Infrastructure/Persistence/DataDirectory.cs` -- **NO SE TOCA.** Ya expone `PackagesPath`: la ruta del repositorio de los paquetes publicados, dentro de la carpeta de datos (`<data-dir>/packages`).

### Piezas del servidor -- PLANTILLAS A REUSAR

- `src/Server/Domain/Catalog/GameRegistration.cs` -- **PLANTILLA DEL DOMINIO.** `public static class FieldNames` con los dieciocho nombres de campo (`GameRegistration.FieldNames.License` y `.DistributionRestrictions` son los dos que esta historia necesita). `RequireText`, `RequireItems`, `RequireOptionalItems` son los ayudantes de validación privados. `GenerateGameId()` produce el UUID v7 canónico de 32 hexadecimales en minúsculas.
- `src/Server/Domain/Catalog/CatalogDomainExceptions.cs` -- **PLANTILLA DE EXCEPCIONES.** Base `public abstract class CatalogDomainException : Exception` y excepciones tipadas derivadas (`GameMetadataMissingException` con `FieldName`, `GameNotFoundException` con `GameId`, `SchemaVersionMissingException`, `SchemaVersionNotSupportedException`). La convención «Errores» sitúa las excepciones tipadas en el dominio.
- `src/Server/Application/Ports/IGameRepository.cs` -- **PLANTILLA DE PUERTO.** Interface con XML doc en cada miembro, declarada en la capa de aplicación. `FindAsync(gameId, ct)` **ya existe** y es lo que lee el videojuego para comprobar la licencia y las restricciones.
- `src/Server/Application/Catalog/RegisterGame.cs` -- **PLANTILLA DE CASO DE USO.** Constructor con `ArgumentNullException.ThrowIfNull`, `ExecuteAsync(..., CancellationToken)`, log `LogInformation` con scopes, y un `public static GameDescriptor ToDescriptor(GameRegistration game)` que proyecta la entidad de dominio sobre el contrato de `Shared`.
- `src/Server/Application/Catalog/ModifyGame.cs` -- **PLANTILLA** de un caso de uso con lectura previa por el puerto y `throw new ...Exception` cuando la fila no existe.
- `src/Server/Infrastructure/Persistence/GameRepository.cs` -- **PLANTILLA DE ADAPTADOR.** Constructor con `CatalogDbContext` inyectado, `AsNoTracking()` en las lecturas, `SaveChangesAsync(cancellationToken)`, y los proyectores privados estáticos `ToEntity`/`ToRegistration`.
- `src/Server/Host/GamesEndpoint.cs` -- **PLANTILLA DE ENDPOINT.** `public const string Route`, `public const int SupportedSchemaVersion = 1`, `private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);`, `public static string Serialize<T>(T payload)`, `public static void Map(WebApplication app)`, y el `private static async Task WriteErrorAsync(HttpContext, IMetricsCounters, ILogger, int statusCode, ApiError error, string? field)` que llama a `metrics.RecordError(error.Code)`.
- `src/Server/Host/GameRequest.cs` -- **PLANTILLA DE CUERPO.** Enseña la forma correcta: un **sobre** sobre el contrato de `Shared` (`SchemaVersion` + el identificador opcional + el contrato), que **no repite ni un nombre de campo**. El cuerpo de esta historia sigue esa forma y no repite los campos de `PackageDescriptor` ni de `InstallationManifest`.
- `src/Server/Program.cs` -- **RAÍZ DE COMPOSICIÓN.** Registra los puertos y los casos de uso con `builder.Services.AddScoped<...>()`, mapea las rutas (`GamesEndpoint.Map(app)`), y aplica `app.UseWhen(...)` dos veces: la del cliente (`IsClientSurface`) y la de la administración (`AdminSurfacePrefix`). La ruta de esta historia cuelga de `/api/v1/admin`, así que **queda cubierta por la correlación de la administración y NO exige `X-Client-Id`**.
- `src/Server/Infrastructure/Http/AdminCorrelationMiddleware.cs` -- correlación por `operationId` de la superficie de administración. `public const string OperationIdHeaderName`, `public const int CanonicalIdentifierLength` (32).
- `src/Server/Presentation/Catalog/CatalogWindow.cs` -- **PLANTILLA DE VENTANA.** UI construida en código C# (el repositorio no tiene XAML), `StackPanel`, `TextBlock`, `Button`, `ShowDialog(this)`, y la columna de navegación con «Catálogo», «Clientes» y **«Paquetes»** (la sección de esta historia).
- `src/Server/Presentation/Catalog/CatalogViewModel.cs` -- **PLANTILLA DE MODELO DE VISTA.** `public sealed record CatalogGameRow(...)` para la fila de la lista, y `Replace(IEnumerable<T>)` para rellenar la lista.
- `src/Server/Presentation/Catalog/GameFormViewModel.cs` -- **PLANTILLA DE MODELO DE VISTA DE FORMULARIO.** `RequiredFieldNames`, `LabelFor(label, fieldName)` con la marca `*`, `SplitList(string?)`, y `ToDescriptor()` que construye el contrato de `Shared` desde lo tecleado.
- `src/Server/Presentation/Catalog/GameFormView.cs` -- **PLANTILLA DE FORMULARIO.** `MonospaceFontFamily` y `MetaFontFamily` (tipografía local, deuda documentada hacia la Epic 11).
- `src/Server/Presentation/Formatting/SizeFormatter.cs` -- **SE REUTILIZA TAL CUAL.** Bytes exactos a «<n>,<d> GB», separador decimal español, exactamente un decimal.
- `src/Server/Presentation/ServerApp.cs` -- **PLANTILLA DE APLICACIÓN AVALONIA.** `public sealed class ServerApp : Avalonia.Application` con el tipo completamente cualificado por la colisión con el namespace `LANPartyHub.Server.Application`; crea un scope del contenedor y abre la ventana principal.

### Pruebas -- PLANTILLAS

- `tests/IntegrationTests/TestServer.cs` -- **PLANTILLA DE INTEGRACIÓN.** `TestServer.StartAsync(dataDirectory, ct)` arranca el host real (Kestrel real) sobre una carpeta temporal; `CreateClient()` devuelve el `HttpClient`; `Assert.Skip` cuando no hay interfaz privada.
- `tests/IntegrationTests/GameCatalogTests.cs` -- **PLANTILLA DE PRUEBA DE PERSISTENCIA.** `BuildValidRequest()`, `BuildBody(request)`, `ReadErrorAsync`, `AssertRejected(body, expectedCode)` (afirma `code`, `message`, `cause` y `nextStep` no vacíos), `ReadColumnNames(dataDirectory, "SELECT name FROM pragma_table_info('GAME')")`, `ReadRow(dataDirectory, id)`, `OpenDatabase(dataDirectory)` con `Microsoft.Data.Sqlite`, `CreateTemporaryDataDirectory()`. La clase va con `[Collection(RealHostCollection.Name)]`.
- `tests/UnitTests/SharedContractInventoryTests.cs` -- fija **exactamente 20** contratos públicos en `Shared` y prohíbe que cualquiera de ellos se declare en `Server` o `Client` (usa `DeclaredTopLevelTypes`, que incluye los no públicos). **Un tipo en `Server` que se llame `PackageDescriptor`, `InstallationManifest`, `SignedEnvelope` o `TrustAnchor` rompe este guard.**
- `tests/UnitTests/ProjectDependencyTests.cs` -- fija **exactamente 6** proyectos; `No_type_of_the_domain_layer_imports_infrastructure_or_a_framework` prohíbe la cadena `Avalonia` en `src/Server/Domain`; `No_type_of_the_application_layer_references_the_outer_layers` prohíbe **`Infrastructure` y `Presentation`** en `src/Server/Application`; `No_type_of_the_application_layer_imports_a_framework` prohíbe `Avalonia`, `Microsoft.EntityFrameworkCore`, `Microsoft.Data.Sqlite`, `Microsoft.AspNetCore`, `System.Data.Common`, `System.Net.Http`, `System.Net.Sockets` y `HttpClient` en `src/Server/Application`; `No_type_of_the_infrastructure_layer_references_the_presentation_layer` prohíbe la cadena `Presentation` (incluso en un comentario) en `src/Server/Infrastructure`.
- `docs/plan.md` -- §8: escala S0/S1/S2/S3; máximo **dos** ciclos automáticos de corrección y revisión por historia.

### Decisión criptográfica -- VERIFICADA POR COMPILACIÓN (2026-10-29)

- **No hay Ed25519 en el BCL de .NET 10.** `System.Security.Cryptography.Ed25519` **no** existe como tipo público: un proyecto de prueba que lo usa falla con `CS0246`. La cadena «Ed25519» sí aparece en el ensamblado de referencia, pero no es una API.
- **BouncyCastle.Cryptography 2.7.0** está **en la caché local de NuGet** (`~/.nuget/packages/bouncycastle.cryptography/2.7.0/`) y compila contra `net10.0`. Es la opción que permite cerrar la historia sin red.
- **API exacta, verificada por ejecución** (no supuesta):
  - `Org.BouncyCastle.Crypto.Parameters.Ed25519PrivateKeyParameters(byte[] seed, int keyLength)` con semilla de 32 bytes.
  - `priv.GeneratePublicKey()` -> `Ed25519PublicKeyParameters`.
  - `Org.BouncyCastle.Crypto.Signers.Ed25519Signer`, `signer.Init(true, privateKey)` / `signer.Init(false, publicKey)`, `BlockUpdate(bytes, off, len)`, `GenerateSignature()` -> **64 bytes**, `VerifySignature(signature)` -> `bool`.
  - `pub.GetEncoded()` -> **32 bytes** (clave pública cruda, que es lo que `TrustAnchor.PublicKey` transporta).
  - `Org.BouncyCastle.Math.EC.Rfc8032.Ed25519` existe como implementación matemática pura, pero la ruta `Crypto.Signers` es la que se usa.

## Tasks & Acceptance

**Execution:**
- `src/Server/LANPartyHub.Server.csproj` -- referenciar `BouncyCastle.Cryptography` en la versión `2.7.0` -- AD-2 exige la firma Ed25519 y el BCL de .NET 10 no la trae; la versión está verificada en la caché local de NuGet y compila contra `net10.0`. El paquete entra **aislado** detrás del puerto de firma: sus tipos no salen de `src/Server/Infrastructure/Signing`.
- `src/Server/Domain/Packages/ManifestCanonicalizer.cs` -- canonicalizador del manifiesto: produce el JSON canónico del `InstallationManifest` (claves ordenadas, sin espacios superfluos) que **es exactamente** el texto que se firma y el texto que se persiste en la columna `canonicalManifest` -- AD-2 exige que la firma cubra la forma canónica, y el comentario XML de `InstallationManifest` exige que el canonicalizador la reproduzca byte a byte. **Vive en `Domain`**: la capa de aplicación no puede tocar `Infrastructure`, y el canonicalizador es la pieza que el caso de uso y el verificador comparten, de modo que no puede haber dos copias que diverjan.
- `src/Server/Domain/Packages/PackageDomainExceptions.cs` -- excepciones de dominio tipadas derivadas de `CatalogDomainException`: `PackageLicenseMissingException`, `PackageDistributionRestrictionsMissingException`, `PackageNotFoundException`, `PackageAlreadyPublishedException`, `PackageContentMismatchException`, `ManifestOperationNotAllowedException` -- la convención «Errores» sitúa las excepciones tipadas en el dominio y el adaptador traduce la excepción al `ApiError`, nunca al revés.
- `src/Server/Application/Ports/IPackageRepository.cs` -- puerto de repositorio del módulo `PackageDistribution` con la escritura del descriptor y del manifiesto, y la lectura de un `packageId` para la comprobación de inmutabilidad -- AD-1 y la convención «State & cross-cutting»: la mutación de lo persistido ocurre solo en `Infrastructure` a través de un puerto.
- `src/Server/Application/Ports/IManifestSigner.cs` -- puerto de firma: `string Sign(string canonicalManifest)` y `string GetKeyId()` -- el caso de uso no conoce BouncyCastle (AR-1, AD-1): depende del puerto, y el adaptador de firma es el único que habla el criptográfico. `GetKeyId()` expone el `keyId` del sobre firmado, que **solo** vive ahí (AD-2).
- `src/Server/Application/Ports/IPackageContentStore.cs` -- puerto de lectura de los bytes del paquete: `Task<byte[]> ReadBytesAsync(string packageId, CancellationToken cancellationToken)` -- la lectura del archivo vive en `Infrastructure` (que es quien conoce `DataDirectory.PackagesPath`), y el caso de uso recibe los **bytes** por el puerto. El `sha256` lo calcula el caso de uso sobre esos bytes, de modo que el valor que se firma y el valor que se persiste salen de la **misma** lectura y no pueden divergir. La capa de aplicación no puede tocar `Infrastructure` (guard de `ProjectDependencyTests`), así que la lectura de disco necesita un puerto; el hash, en cambio, es una regla del caso de uso y no necesita salida al disco.
- `src/Server/Application/Packages/PublishPackage.cs` -- caso de uso que publica el paquete: lee el videojuego por el puerto de juegos, **bloquea** la publicación si falta la licencia **o** las restricciones (nombrando el campo), calcula el `sha256` de los bytes, construye el `InstallationManifest` con sus operaciones tipadas y cerradas, canonicaliza, firma por el puerto, y persiste el `PackageDescriptor` (sin firma) y el `InstallationManifest` (con su sobre firmado) -- AC1, AC2, AC4: la firma y la inmutabilidad son reglas del dominio y del caso de uso, no del adaptador.
- `src/Server/Infrastructure/Signing/Ed25519ManifestSigner.cs` -- adaptador que implementa `IManifestSigner` con BouncyCastle: mantiene la clave privada del servidor en memoria, firma los bytes UTF-8 del manifiesto canónico y devuelve la firma en hexadecimal minúsculo; `GetKeyId()` devuelve un identificador estable de la clave -- AD-2 y AD-8: la firma es Ed25519 sobre el manifiesto canónico. Los tipos de BouncyCastle **no salen de este fichero**.
- `src/Server/Infrastructure/Persistence/PackageRepository.cs` -- adaptador EF Core que escribe y lee `PACKAGE_DESCRIPTOR` e `INSTALLATION_MANIFEST` -- AD-22: las dos tablas son propiedad del servidor. El `PackageDescriptor` se persiste **sin** firma y el `InstallationManifest` **con** ella, porque la firma vive solo en el sobre (AD-2).
- `src/Server/Host/PackageRequest.cs` -- cuerpo de la administración de la publicación: un **sobre** sobre los contratos de `Shared` (`schemaVersion` + `gameId` + `package` + `manifest`), que **no repite ni un nombre de campo** de `PackageDescriptor` ni de `InstallationManifest` -- la misma lección que dejó la Story 2.1: un tipo equivalente a un contrato de `Shared` en `Server` está prohibido (regla de la épica «un solo concepto, un tipo, un lugar»). **No puede llamarse `PackageDescriptor`, `InstallationManifest` ni `SignedEnvelope`**: rompería `SharedContractInventoryTests`.
- `src/Server/Host/PackagesEndpoint.cs` -- endpoint `POST /api/v1/admin/packages` que valida la versión del esquema, invoca el caso de uso y traduce las excepciones de dominio a la forma única `ApiError` con el código estable, contando el error por su código -- AD-5 y AD-15: el patrón es el de `GamesEndpoint`, y la ruta cuelga de `/api/v1/admin`, así que la correlación de la administración la cubre sin exigir `X-Client-Id`.
- `src/Server/Program.cs` -- registrar `IPackageRepository` -> `PackageRepository`, `IManifestSigner` -> `Ed25519ManifestSigner`, el caso de uso `PublishPackage`, y mapear `PackagesEndpoint.Map(app)` -- AD-1: los casos de uso dependen del puerto y nunca del adaptador.
- `src/Server/Presentation/Packages/PackagesWindow.cs` -- ventana de la sección «Paquetes» del servidor: la sección de navegación que la columna izquierda ya nombra, con la lista de los paquetes publicados y el acceso a la publicación -- DESIGN.md: el servidor tiene columna izquierda de navegación (Catálogo, Clientes, Paquetes) y área principal. Se abre desde `CatalogWindow` o desde la aplicación; la UI se construye en código C#, como toda la del repositorio.
- `src/Server/Presentation/Packages/PackageFormViewModel.cs` -- modelo de vista del formulario de publicación: refleja la ruta del paquete, la versión, el tamaño en bytes exactos y las operaciones del manifiesto; **expone el estado del bloqueo** con el campo concreto que falta, para que la organizadora vea **cuál** de los dos falta y no un error genérico -- AC1 y UX-DR-44: el bloqueo es explícito y el formulario dice **cuál** de los dos falta.
- `src/Server/Presentation/Packages/PackageFormView.cs` -- ventana del formulario de publicación, construida en código C# (patrón `GameFormView`), que entrega el paquete al caso de uso y refleja el resultado o el rechazo -- AC1: el formulario es la superficie donde la publicación se bloquea y donde se explica el bloqueo.
- `src/Server/Presentation/ServerApp.cs` -- abrir la sección de paquetes junto a la de catálogo cuando el organizador navega a «Paquetes» -- AC1: la superficie donde se publica tiene que ser alcanzable.
- `tests/UnitTests/ManifestCanonicalizerTests.cs` -- pruebas unitarias del canonicalizador: el JSON canónico no lleva espacios superfluos, las claves están ordenadas, el mismo manifiesto produce **siempre** el mismo texto, y el texto canónico es el que se firma -- AD-2: la firma cubre la forma canónica, de modo que el canonicalizador es la pieza que no puede tener dos versiones.
- `tests/UnitTests/ManifestSignerTests.cs` -- pruebas unitarias de la firma: la firma verifica contra la clave pública, un manifiesto alterado **no** verifica, y la firma tiene 64 bytes -- AD-2 y AD-8: la firma es lo que autentica, y una firma que verifica cualquier cosa no autentica nada.
- `tests/UnitTests/PackagePublicationDomainTests.cs` -- pruebas unitarias del dominio: la publicación se bloquea sin licencia, se bloquea sin restricciones, y el bloqueo **nombra** el campo concreto; las operaciones del manifiesto son un conjunto cerrado y no admiten una operación de ejecución de comandos -- AC1 y AC5.
- `tests/IntegrationTests/PackagePublicationTests.cs` -- pruebas de integración contra `catalog.sqlite` real y el host real: publicación que persiste las 7 columnas de `PACKAGE_DESCRIPTOR` y las 6 de `INSTALLATION_MANIFEST` con el nombre y el orden exactos; el `sha256` persistido es el de los bytes reales; la firma persistida verifica contra la clave del servidor sobre el manifiesto canónico persistido; el `code` del descriptor **no** transporta firma; la publicación sin licencia y sin restricciones se rechazan con su `code` **sin escribir fila**; la republicación del mismo `packageId` se rechaza sin modificar la fila; eloperations desconocido se rechaza sin escribir fila -- AC1, AC2, AC3, AC4, AC6; el nivel de prueba que exige la historia es integración.
- `tests/UnitTests/ErrorCatalogTests.cs` -- extender para afirmar que los códigos nuevos de esta historia tienen su entrada de tres partes y que el catálogo no tiene códigos duplicados -- AD-15 y AD-25: el catálogo es el único hogar de un código y cada código tiene su guía de tres partes.

**Acceptance Criteria:**
- Given un videojuego sin información de licencia o sin restricciones de distribución, when la organizadora intenta publicar su paquete, then la publicación se **bloquea** y el formulario explica **cuál** de los dos campos falta, and no se escribe ninguna fila ni ningún archivo del paquete.
- Given un videojuego con licencia y restricciones registradas, when la organizadora publica su paquete, then el servidor persiste un `PackageDescriptor` con `packageId`, `gameId`, `version`, `sizeBytes` exacto, `chunkSizeBytes` (informativo y opcional en el camino HTTP), `sha256` y `manifestRef`, and el descriptor **no** transporta firma: la firma vive **solo** en el sobre firmado del manifiesto.
- Given un paquete publicado, when se intenta cambiar su contenido, then el sistema exige **publicar una versión nueva**: un paquete publicado es inmutable, and la fila existente no se modifica.
- Given el manifiesto de instalación de un paquete, when se firma, then la firma es **Ed25519** sobre el **manifiesto canónico** (JSON canónico, sin espacios superfluos, claves ordenadas) y el sobre contiene `{ manifest, signature, keyId }`, and el manifiesto firmado incluye `packageId`, `packageVersion`, `sizeBytes` y `sha256` del paquete, de modo que la correspondencia entre manifiesto y paquete queda **firmada**, no supuesta.
- Given el manifiesto de instalación, when se declaran sus operaciones, then son **tipadas y cerradas**: `extract`, `createDirectory`, `copyFile`, `writeConfig`, `setLaunchConfig`, `verify`, and **no existe ninguna operación de ejecución de comandos arbitrarios**.
- Given el servidor, when se inspecciona la persistencia, then `PackageDescriptor` e `InstallationManifest` (con su sobre firmado) son propiedad del servidor y se sirven como artefactos **aparte**, no incrustados en el binario del paquete.
- Given una publicación, when el manifiesto o el paquete no coincide con lo declarado, then la publicación se rechaza con un `ApiError` de tres partes y **no** se escribe ninguna fila ni ningún archivo.
- Given el servidor en marcha, when se publica un paquete, then la entrada del log lleva el `operationId` de la unidad de trabajo y el error se cuenta por su código estable cuando la operación falla.

## Spec Change Log

## Review Triage Log

## Design Notes

**Por qué la firma usa BouncyCastle y no el BCL.** AD-2 exige Ed25519 y `System.Security.Cryptography.Ed25519` no existe como tipo público en .NET 10: un proyecto de prueba que lo usa falla con `CS0246`. La alternativa disponible sin red es `BouncyCastle.Cryptography` 2.7.0, verificada en la caché local de NuGet y compilada contra `net10.0`. La librería entra **aislada** detrás del puerto `IManifestSigner`, de modo que el caso de uso no la conoce y sus tipos no salen de `src/Server/Infrastructure/Signing`. La API que se usa, verificada por ejecución, es: `new Ed25519PrivateKeyParameters(seed, 0)`, `priv.GeneratePublicKey()`, `new Ed25519Signer()` con `Init(true|false, key)`, `BlockUpdate(bytes, off, len)`, `GenerateSignature()` (64 bytes) y `VerifySignature(sig)`; la clave pública cruda son los 32 bytes de `GetEncoded()`.

**Por qué el canonicalizador vive en `Domain` y no en `Application` ni en `Infrastructure`.** El comentario XML de `InstallationManifest` exige que el canonicalizador de esta historia y el de la Story 6.1 reproduzcan **la misma** forma byte a byte, porque la firma se verifica contra ella. La capa de aplicación no puede referenciar `Infrastructure` ni `Presentation` (guard de `ProjectDependencyTests`), así que un canonicalizador en `Application` tendría que duplicarse o la verificación quedaría en otro sitio. En `Domain` hay una sola copia, y tanto el caso de uso que firma como el adaptador que verifica la usan.

**Por qué el canonicalizador produce claves ordenadas y no el orden de declaración.** AD-2 define la forma canónica como «JSON canónico, sin espacios superfluous, con claves ordenadas». El orden de declaración del record y el orden alfabético coinciden hoy en `InstallationManifest`, pero **no** en las operaciones polimórficas, cuyo discriminador `operation` se emite junto a las propiedades del tipo derivado. El canonicalizador ordena las claves **por nombre** de forma explícita, de modo que la firma no depende del orden de declaración de ningún tipo y el texto canónico es reproducible byte a byte entre el firmante y el verificador.

**Por qué el `PackageDescriptor` se persiste sin firma.** AD-2 y AD-7 son explícitos: la firma vive solo en el sobre firmado del manifiesto y `keyId` solo en ese sobre. La tabla `PACKAGE_DESCRIPTOR` **no tiene columna de firma** en el esquema que ya existe, de modo que el descriptor no podría transportarla sin una migración, y esa migración no está autorizada en esta historia. La correspondencia entre manifiesto y paquete queda firmada porque el **manifiesto** firmado lleva `packageId`, `packageVersion`, `sizeBytes` y `sha256`: el descriptor es el índice que apunta al manifiesto, no el documento firmado.

**Por qué el bloqueo nombra el campo concreto.** La AC exige que el formulario diga **cuál** de los dos campos falta, y la forma única del error de API `{ code, message, cause, nextStep }` no tiene hueco para el campo. La lectura que ya fijó la Story 2.1 se aplica aquí: es el **`code`** el que nombra el campo, y por eso esta historia añade sus propios códigos estables en lugar de reusar el genérico. El `message` sigue siendo texto en español para la persona y el `code` sigue siendo el identificador estable para la máquina (AD-25).

**Por qué la inmutabilidad se comprueba en el caso de uso y no con una restricción de la tabla.** La tabla `PACKAGE_DESCRIPTOR` tiene `packageId` como clave primaria, de modo que el «contenido ya publicado» se detecta por la clave, no por una columna de contenido. Un `UPDATE` de la fila existente es lo que la regla prohíbe, y la comprobación vive en el caso de uso, que lee el `packageId` por el puerto antes de escribir. La restricción de la base de datos no se toca: el esquema de la migración inicial ya cumple AD-22 y no se modifica.

## Verification

**Commands:**
- `dotnet build LANPartyHub.sln` -- expected: SUCCESS, sin errores ni advertencias nuevas.
- `dotnet test LANPartyHub.sln` -- expected: SUCCESS, todos los tests en verde, incluidos los nuevos de `ManifestCanonicalizerTests`, `ManifestSignerTests`, `PackagePublicationDomainTests` y `PackagePublicationTests`.
- `powershell -NoProfile -File tools/check-encoding.ps1` -- expected: SUCCESS, `OK: sin codepoints CJK ni U+FFFD`.
- `powershell -NoProfile -File tools/check-epics.ps1` -- expected: SUCCESS, sin huecos de recuento, paridad ni trazabilidad AR.

**Manual checks:**
- Inspeccionar la tabla `PACKAGE_DESCRIPTOR` con `pragma_table_info('PACKAGE_DESCRIPTOR')`: las 7 columnas con el nombre y el orden de AD-7, sin columna de firma.
- Inspeccionar la tabla `INSTALLATION_MANIFEST` con `pragma_table_info('INSTALLATION_MANIFEST')`: las 6 columnas con el nombre y el orden de AD-22.
- Inspeccionar la columna `canonicalManifest` de una fila: su texto es el **mismo** que el que la firma cubre, y la firma verifica contra la clave pública del servidor.
