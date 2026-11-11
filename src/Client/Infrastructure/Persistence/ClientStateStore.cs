using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using LANPartyHub.Client.Domain.Trust;
using LANPartyHub.Client.Infrastructure.Persistence.Entities;
using LANPartyHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LANPartyHub.Client.Infrastructure.Persistence;

/// <summary>
/// Access to the database of the client (AD-11, AD-22, AD-23).
///
/// <para>
/// The adapter owns the schema of the file <c>state.sqlite</c>. The table of the identity
/// holds a single row: the identifier that the client emitted once. The identifier is never
/// rewritten, so a restart and the replacement of the binary keep the same identity while the
/// data folder survives (AD-23).
/// </para>
/// </summary>
public sealed class ClientStateStore : IClientStateStore, ITrustAnchorReader
{
    /// <summary>
    /// Name of the folder of the data of the client that holds the CÓDIGO del error de
    /// una instalación fallida (AD-11, AC5).
    /// </summary>
    public const string InstallationFailuresDirectoryName = "installation-failures";

    private readonly ClientStateDbContext _context;
    private readonly IClientDataLayout? _layout;

    /// <summary>
    /// Gate that serializes the access to the context (AD-11, AD-24).
    ///
    /// <para>
    /// Un <see cref="DbContext"/> NO es seguro entre hilos: EF Core lo rechaza con
    /// «A second operation was started on this context instance before a previous operation
    /// completed» cuando dos operaciones se solapan. La descarga PERSISTE el offset
    /// mientras corre (AD-11, SM-2) y la publicación del progreso LEE la identidad al
    /// mismo tiempo, así que las dos operaciones comparten el contexto y se solapan de
    /// verdad.
    /// </para>
    /// <para>
    /// La puerta serializa el acceso: cada operación espera a que la anterior termine,
    /// así que el contexto nunca lleva dos operaciones a la vez. La puerta se toma sobre
    /// el contexto COMPLETO, no sobre una parte, porque la restricción de EF Core es del
    /// contexto entero.
    /// </para>
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates the adapter over the database of the client.</summary>
    /// <param name="context">Database of the client.</param>
    /// <param name="layout">
    /// Layout of the data folder of the client, needed to write the DURABLE sink of the
    /// error of una instalación fallida. When it is <c>null</c> the failure only persists
    /// el estado de la fila, y el código del error no tiene sumidero (AD-11, AC5).
    /// </param>
    public ClientStateStore(ClientStateDbContext context, IClientDataLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _layout = layout;
    }

    /// <inheritdoc />
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // AD-11: the migrations are versioned and they are applied automatically at startup.
            // EnsureCreated is never used, because it skips the history of the migrations.
            await _context.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ClientIdentity?> LoadIdentityAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var row = await _context.Identities
                .AsNoTracking()
                .OrderBy(identity => identity.ClientId)
                .FirstOrDefaultAsync(cancellationToken);

            if (row is null)
            {
                return null;
            }

            if (!ClientIdentity.IsCanonical(row.ClientId))
            {
                throw new InvalidOperationException(
                    $"La identidad persistida del cliente no está en su forma canónica. "
                    + "Restaura la copia de la carpeta de datos o borra la fila 'CLIENT_IDENTITY' para emitir un identificador nuevo.");
            }

            return ClientIdentity.FromCanonical(row.ClientId);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveIdentityAsync(ClientIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // AD-23: the identifier is emitted once. A row that already exists is never
            // rewritten, so a second startup cannot change the identity of the team.
            var existing = await _context.Identities
                .AnyAsync(cancellationToken);

            if (existing)
            {
                throw new InvalidOperationException(
                    "La identidad del cliente ya está persistida y no puede volver a emitirse.");
            }

            _context.Identities.Add(new ClientIdentityEntity { ClientId = identity.CanonicalId });

            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PersistedDownloadOperation>> LoadDownloadOperationsAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var operations = await _context.DownloadOperations
                .AsNoTracking()
                .OrderBy(operation => operation.OperationId)
                .ToListAsync(cancellationToken);

            var progresses = await _context.DownloadProgresses
                .AsNoTracking()
                .ToDictionaryAsync(progress => progress.OperationId, StringComparer.Ordinal, cancellationToken);

            return operations
                .Select(operation => new PersistedDownloadOperation(
                    operation.OperationId,
                    operation.PackageId,
                    operation.State,
                    operation.TransferEngine,
                    progresses.TryGetValue(operation.OperationId, out var progress) ? progress.BytesPersisted : null,
                    progresses.TryGetValue(operation.OperationId, out var same) ? same.TotalBytes : null))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveProgressAsync(
        string operationId,
        long bytesPersisted,
        long? totalBytes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var progress = await _context.DownloadProgresses
                .FirstOrDefaultAsync(row => row.OperationId == operationId, cancellationToken);

            if (progress is null)
            {
                progress = new DownloadProgressEntity { OperationId = operationId, TotalBytes = totalBytes ?? 0L };
                _context.DownloadProgresses.Add(progress);
            }
            else if (totalBytes is { } total)
            {
                // A total that is unknown does not replace the persisted one.
                progress.TotalBytes = total;
            }

            progress.BytesPersisted = bytesPersisted;

            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveOperationAsync(
        string operationId,
        string packageId,
        string state,
        string transferEngine,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(transferEngine);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // AD-10, AD-24: la operación declara el motor que transfiere sus bytes, y el
            // formato de su progreso se deduce de ese valor. El registro se escribe una vez
            // y su estado se ACTUALIZA: el estado de la máquina es del cliente (AD-10).
            var operation = await _context.DownloadOperations
                .FirstOrDefaultAsync(row => row.OperationId == operationId, cancellationToken);

            if (operation is null)
            {
                _context.DownloadOperations.Add(new DownloadOperationEntity
                {
                    OperationId = operationId,
                    PackageId = packageId,
                    State = state,
                    TransferEngine = transferEngine,
                });
            }
            else
            {
                operation.State = state;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<PinnedTrustAnchor?> LoadTrustAnchorAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var row = await _context.TrustAnchors
                .AsNoTracking()
                .OrderBy(anchor => anchor.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (row is null)
            {
                return null;
            }

            return new PinnedTrustAnchor(row.PublicKey, row.Fingerprint, row.FixedAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveTrustAnchorAsync(PinnedTrustAnchor anchor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // AD-27: el ancla fijada es INMUTABLE. Una fila que ya existe NO se sustituye por
            // otra clave: el emparejamiento nuevo es una operación explícita del organizador y
            // no un efecto secundario de una conexión.
            var existing = await _context.TrustAnchors
                .AnyAsync(cancellationToken);

            if (existing)
            {
                throw new InvalidOperationException(
                    "El ancla de confianza ya está fijada y no puede sustituirse por otra clave sin un emparejamiento fuera de banda nuevo.");
            }

            _context.TrustAnchors.Add(new TrustAnchorEntity
            {
                Id = TrustAnchorEntity.SingleRowKey,
                PublicKey = anchor.PublicKey,
                Fingerprint = anchor.Fingerprint,
                FixedAt = anchor.FixedAt,
            });

            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveInstallationAsync(
        ClientInstallation installation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentException.ThrowIfNullOrWhiteSpace(installation.GameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(installation.InstalledVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(installation.InstallPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(installation.InstallDirectoryId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // AD-22: la clave de la fila es el gameId, así que una segunda instalación del
            // MISMO videojuego actualiza la fila que ya existe en lugar de crear otra.
            var row = await _context.Installations
                .FirstOrDefaultAsync(
                    entity => entity.GameId == installation.GameId,
                    cancellationToken);

            if (row is null)
            {
                _context.Installations.Add(new ClientInstallationEntity
                {
                    GameId = installation.GameId,
                    InstalledVersion = installation.InstalledVersion,
                    ValidationState = installation.ValidationState.ToString(),
                    InstallPath = installation.InstallPath,
                    InstallDirectoryId = installation.InstallDirectoryId,
                });
            }
            else
            {
                // AC1: el identificador de la raíz NO cambia ante una reparación, así que
                // la fila conserva el que ya tenía.
                row.InstalledVersion = installation.InstalledVersion;
                row.ValidationState = installation.ValidationState.ToString();
                row.InstallPath = installation.InstallPath;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ClientInstallation?> LoadInstallationAsync(
        string gameId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var row = await _context.Installations
                .AsNoTracking()
                .FirstOrDefaultAsync(entity => entity.GameId == gameId, cancellationToken);

            if (row is null)
            {
                return null;
            }

            // El estado persistido es el NOMBRE del valor de la máquina de estados, y un
            // nombre que la máquina no reconoce NO se inventa: la instalación se lee
            // como no registrada (AD-10).
            if (!Enum.TryParse<InstallationState>(row.ValidationState, out var state))
            {
                return null;
            }

            return new ClientInstallation(
                row.GameId,
                row.InstalledVersion,
                state,
                row.InstallPath,
                row.InstallDirectoryId);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClientInstallation>> LoadInstallationsAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var rows = await _context.Installations
                .AsNoTracking()
                .OrderBy(entity => entity.GameId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var installations = new List<ClientInstallation>(rows.Count);

            foreach (var row in rows)
            {
                // El estado persistido es el NOMBRE del valor de la máquina de estados, y
                // un nombre que la máquina no reconoce NO se inventa: esa instalación se
                // lee como no registrada (AD-10).
                if (!Enum.TryParse<InstallationState>(row.ValidationState, out var state))
                {
                    continue;
                }

                installations.Add(new ClientInstallation(
                    row.GameId,
                    row.InstalledVersion,
                    state,
                    row.InstallPath,
                    row.InstallDirectoryId));
            }

            return installations;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveInstallationFailureAsync(
        ClientInstallation installation,
        string errorCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentException.ThrowIfNullOrWhiteSpace(installation.GameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        // AC5: la fila de la instalación queda en el estado terminal `Failed`, así que el
        // siguiente arranque encuentra la operación como fallida y la acción «Reintentar»
        // está disponible sin reinstalar el cliente (AD-10, AD-11).
        await SaveInstallationAsync(
            installation with { ValidationState = InstallationState.Failed },
            cancellationToken).ConfigureAwait(false);

        // AD-11, AC5: el CÓDIGO del error va al sumidero durable, para que el diagnóstico
        // sobreviva al reinicio del cliente. Sin layout no hay sumidero, y el estado
        // terminal de la fila ya basta para que la operación sea reintentable.
        var path = FailurePathOf(installation.GameId);

        if (path is null)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            await File.WriteAllTextAsync(path, errorCode, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // El sumidero durable es un diagnóstico y no un deber: un archivo que no se
            // puede escribir no impide que la operación quede reintentable (AD-11).
        }
        catch (UnauthorizedAccessException)
        {
            // El sumidero durable es un diagnóstico y no un deber.
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string?> LoadInstallationFailureAsync(
        string gameId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        var path = FailurePathOf(gameId);

        if (path is null)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var code = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The path of the DURABLE sink of the error de una instalación, or <c>null</c> when
    /// the store does not know the layout (AD-11, AC5).
    ///
    /// <para>
    /// El sumidero vive en la carpeta de datos del cliente, FUERA de la raíz de
    /// instalación, así que escribir el diagnóstico no toca el confine de AD-9.
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <returns>The path of the durable sink, or <c>null</c>.</returns>
    private string? FailurePathOf(string gameId)
    {
        if (_layout is null)
        {
            return null;
        }

        return Path.Combine(
            _layout.Folder,
            InstallationFailuresDirectoryName,
            gameId + ".error");
    }

    /// <inheritdoc />
    public async Task SaveErrorAsync(PersistedClientError error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentException.ThrowIfNullOrWhiteSpace(error.ErrorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(error.ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(error.Code);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // AD-22: la escritura es IDEMPOTENTE por identificador, así que un reintento
            // de la operación NO duplica el diagnóstico que el jugador ve.
            var row = await _context.Errors
                .FirstOrDefaultAsync(entity => entity.ErrorId == error.ErrorId, cancellationToken);

            if (row is null)
            {
                _context.Errors.Add(new ClientErrorEntity
                {
                    ErrorId = error.ErrorId,
                    ClientId = error.ClientId,
                    OperationId = error.OperationId,
                    OperationType = error.OperationType,
                    Code = error.Code,
                    Message = error.Message,
                    Cause = error.Cause,
                    NextStep = error.NextStep,
                    OccurredAt = error.OccurredAt.ToUniversalTime().ToString("O"),
                });
            }
            else
            {
                row.ClientId = error.ClientId;
                row.OperationId = error.OperationId;
                row.OperationType = error.OperationType;
                row.Code = error.Code;
                row.Message = error.Message;
                row.Cause = error.Cause;
                row.NextStep = error.NextStep;
                row.OccurredAt = error.OccurredAt.ToUniversalTime().ToString("O");
            }

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PersistedClientError>> LoadErrorsAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var rows = await _context.Errors
                .AsNoTracking()
                .OrderBy(entity => entity.OccurredAt)
                .ThenBy(entity => entity.ErrorId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var errors = new List<PersistedClientError>(rows.Count);

            foreach (var row in rows)
            {
                // El instante persistido es ISO-8601 en UTC: una fila cuyo instante no se
                // puede leer NO se inventa, así que el visor la salta en lugar de mostrar
                // una fecha falsa (AD-25).
                if (!DateTimeOffset.TryParse(
                        row.OccurredAt,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out var occurredAt))
                {
                    continue;
                }

                errors.Add(new PersistedClientError(
                    row.ErrorId,
                    row.ClientId,
                    row.OperationId,
                    row.OperationType,
                    row.Code,
                    row.Message,
                    row.Cause,
                    row.NextStep,
                    occurredAt));
            }

            return errors;
        }
        finally
        {
            _gate.Release();
        }
    }
}
