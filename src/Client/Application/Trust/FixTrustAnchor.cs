using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.Trust;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Application.Trust;

/// <summary>
/// Outcome of the fixation of the ancla de confianza (AD-27).
///
/// <para>
/// AD-27: el rechazo PRECEDE a persistir y tiene tres niveles con <c>code</c> distinto:
/// ancla sustituida, firma inválida y <c>sha256</c> divergente. Este caso de uso
/// resuelve el primero de los tres, y el <c>code</c> que devuelve es el de «fingerprint de
/// ancla no coincide», que es DISTINTO del de «firma inválida» (AD-27).
/// </para>
/// </summary>
public enum TrustAnchorOutcome
{
    /// <summary>
    /// The ancla was fixed and persisted, because the fingerprints matched (AD-27).
    /// </summary>
    Fixed,

    /// <summary>
    /// The fingerprints did NOT match: nothing was persisted and the operation is
    /// rejected (AD-27).
    /// </summary>
    Rejected,

    /// <summary>
    /// The ancla was already fixed and the server presents a DIFFERENT key: the fixed
    /// ancla is NOT replaced and a new out-of-band pairing is required (AD-27).
    /// </summary>
    AlreadyFixed,
}

/// <summary>
/// Result of the fixation of the ancla de confianza (AD-27).
///
/// <param name="Outcome">Outcome of the operation.</param>
/// <param name="Error">
/// Error of the catalog that explains the rejection, or <c>null</c> when the ancla was
/// fixed.
/// </param>
/// <param name="PresentedFingerprint">
/// The fingerprint that the client derived from the key that the server presented, or
/// <c>null</c> when the key could not be read.
/// </param>
/// <param name="FixedFingerprint">
/// The fingerprint that is persisted, or <c>null</c> when the operation did not fix any.
/// </param>
public sealed record TrustAnchorResult(
    TrustAnchorOutcome Outcome,
    ApiError? Error,
    string? PresentedFingerprint = null,
    string? FixedFingerprint = null)
{
    /// <summary>Whether the ancla was fixed and persisted (AD-27).</summary>
    public bool IsFixed => Outcome == TrustAnchorOutcome.Fixed;

    /// <summary>Whether the operation was rejected without writing anything (AD-27).</summary>
    public bool IsRejected => Outcome != TrustAnchorOutcome.Fixed;
}

/// <summary>
/// Use case of the FIXATION of the ancla de confianza fuera de banda (AC2, AC3, AC4, AC6,
/// AD-27).
///
/// <para>
/// AD-27: el orden normativo de la fijación es fijo y el rechazo en cualquier paso detiene
/// la secuencia. El caso de uso IMPLEMENTA ese orden:
/// </para>
/// <list type="number">
/// <item>El cliente obtiene el fingerprint esperado fuera de banda (QR o contraste en la
/// pantalla del organizador).</item>
/// <item>El cliente obtiene del servidor la clave pública del ancla y CALCULA su
/// fingerprint.</item>
/// <item>El cliente COMPARA el fingerprint calculado con el esperado. Si no coinciden:
/// RECHAZA.</item>
/// <item>El cliente PERSISTE el ancla fijada junto con el fingerprint esperado, de modo que
/// las conexiones posteriores se validan contra un valor ya persistido.</item>
/// </list>
/// <para>
/// El rechazo es ANTERIOR a persistir y en este nivel NO se escribe nada: ni el ancla, ni
/// un manifiesto en la caché, ni un archivo parcial, ni una operación registrada. La
/// comparación ocurre ANTES de tocar el store, y el store solo se llama cuando la
/// comparación pasó (AC2, AD-27).
/// </para>
/// <para>
/// Un ancla ya fijada NO se sustituye: una clave distinta se rechaza y exige un
/// emparejamiento nuevo, explícito y fuera de banda (AC6, AD-27).
/// </para>
/// <para>
/// El caso de uso depende del puerto declarado en esta capa y nunca de un adaptador, así
/// que el rechazo previo a persistir es observable SIN una LAN y sin un disco (AD-1,
/// AD-16).
/// </para>
/// </summary>
public sealed class FixTrustAnchor
{
    private readonly IClientStateStore _stateStore;
    private readonly TimeProvider _time;
    private readonly ILogger<FixTrustAnchor> _logger;

    /// <summary>Creates the use case of the fixation of the ancla de confianza.</summary>
    /// <param name="stateStore">Access to the database of the client.</param>
    /// <param name="time">Source of the instant of the fixation.</param>
    /// <param name="logger">Log of the use case.</param>
    public FixTrustAnchor(
        IClientStateStore stateStore,
        TimeProvider time,
        ILogger<FixTrustAnchor> logger)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _stateStore = stateStore;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Fixes the ancla de confianza: it derives the fingerprint of the key that the server
    /// presented, compares it with the expected one and persists the ancla only when they
    /// match (AC2, AC3, AD-27).
    ///
    /// <para>
    /// AD-27: el rechazo precede a persistir. Cuando los fingerprints NO coinciden, esta
    /// operación NO escribe nada en el disco y devuelve el error del catálogo con el
    /// <c>code</c> de «fingerprint de ancla no coincide», que es distinto del de «firma
    /// inválida» (AC2, AD-27).
    /// </para>
    /// </summary>
    /// <param name="expectedFingerprint">
    /// The fingerprint that came out of band, as the player transported it.
    /// </param>
    /// <param name="presentedAnchor">
    /// The ancla that the server presented in <c>GET /api/v1/server/info</c>.
    /// </param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The result of the fixation.</returns>
    public async Task<TrustAnchorResult> ExecuteAsync(
        string? expectedFingerprint,
        TrustAnchor? presentedAnchor,
        CancellationToken cancellationToken)
    {
        // AD-27: el fingerprint esperado tiene que ser un fingerprint. Un valor que no lo
        // es no puede compararse con nada, así que se rechaza sin escribir nada.
        if (!AnchorFingerprint.TryParse(expectedFingerprint, out var parsedExpected)
            || parsedExpected is null)
        {
            _logger.LogWarning(
                "El fingerprint esperado del ancla de confianza no es válido: el cliente rechaza el emparejamiento y no escribe nada en disco.");

            return new TrustAnchorResult(
                TrustAnchorOutcome.Rejected,
                ErrorCatalog.Resolve(ErrorCatalog.AnchorFingerprintMismatchCode));
        }

        var expected = parsedExpected;

        if (presentedAnchor is null)
        {
            _logger.LogWarning(
                "El servidor no publicó ninguna ancla de confianza: el cliente rechaza el emparejamiento y no escribe nada en disco.");

            return new TrustAnchorResult(
                TrustAnchorOutcome.Rejected,
                ErrorCatalog.Resolve(ErrorCatalog.AnchorFingerprintMismatchCode));
        }

        // AD-27: paso 2. El cliente DERIVA el fingerprint de la clave que el servidor
        // presenta. El servidor publica el suyo con la misma regla, así que los dos lados
        // aplican una sola regla y no puede haber dos.
        AnchorFingerprint calculated;

        try
        {
            calculated = AnchorFingerprint.FromPublicKey(presentedAnchor.PublicKey);
        }
        catch (FormatException exception)
        {
            _logger.LogWarning(
                exception,
                "La clave pública del ancla que presentó el servidor no es válida: el cliente rechaza el emparejamiento y no escribe nada en disco.");

            return new TrustAnchorResult(
                TrustAnchorOutcome.Rejected,
                ErrorCatalog.Resolve(ErrorCatalog.AnchorFingerprintMismatchCode));
        }

        // AD-27: paso 3. La comparación. Si NO coinciden, se RECHAZA.
        if (!calculated.Matches(expected))
        {
            _logger.LogWarning(
                "El fingerprint del ancla que presentó el servidor ({CalculatedFingerprint}) NO coincide con el esperado ({ExpectedFingerprint}): el cliente rechaza el emparejamiento y no escribe nada en disco.",
                calculated.Value,
                expected!.Value);

            return new TrustAnchorResult(
                TrustAnchorOutcome.Rejected,
                ErrorCatalog.Resolve(ErrorCatalog.AnchorFingerprintMismatchCode),
                calculated.Value,
                null);
        }

        // AD-27: un ancla fijada es INMUTABLE. Si ya hay una y es OTRA, se rechaza y se
        // exige un emparejamiento nuevo fuera de banda (AC6).
        var fixedAnchor = await _stateStore
            .LoadTrustAnchorAsync(cancellationToken)
            .ConfigureAwait(false);

        if (fixedAnchor is not null)
        {
            var isSameAnchor = fixedAnchor.AnchorFingerprint.Matches(calculated);

            if (!isSameAnchor)
            {
                _logger.LogWarning(
                    "El servidor presentó un ancla ({PresentedFingerprint}) que NO es la que ya está fijada ({FixedFingerprint}): el cliente no la sustituye y exige un emparejamiento fuera de banda nuevo.",
                    calculated.Value,
                    fixedAnchor.Fingerprint);

                return new TrustAnchorResult(
                    TrustAnchorOutcome.AlreadyFixed,
                    ErrorCatalog.Resolve(ErrorCatalog.AnchorFingerprintMismatchCode),
                    calculated.Value,
                    fixedAnchor.Fingerprint);
            }

            // El ancla fijada ES la que el servidor presenta: no se vuelve a escribir y el
            // cliente ya puede validar contra el valor persistido (AC3).
            _logger.LogInformation(
                "El ancla de confianza ya fijada ({Fingerprint}) coincide con la que presenta el servidor.",
                fixedAnchor.Fingerprint);

            return new TrustAnchorResult(
                TrustAnchorOutcome.Fixed,
                null,
                calculated.Value,
                fixedAnchor.Fingerprint);
        }

        // AD-27: paso 4. Solo aquí, con la comparación ya pasada, se escribe el ancla.
        var pinned = new PinnedTrustAnchor(
            presentedAnchor.PublicKey,
            calculated.Value,
            _time.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture));

        await _stateStore.SaveTrustAnchorAsync(pinned, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Ancla de confianza fijada con el fingerprint {Fingerprint}: las conexiones posteriores se validan contra el valor persistido.",
            pinned.Fingerprint);

        return new TrustAnchorResult(
            TrustAnchorOutcome.Fixed,
            null,
            calculated.Value,
            pinned.Fingerprint);
    }
}
