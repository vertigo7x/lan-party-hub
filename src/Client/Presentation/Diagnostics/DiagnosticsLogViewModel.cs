using Avalonia.Media;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Presentation.Design;
using LANPartyHub.Client.Presentation.Errors;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Presentation.Diagnostics;

/// <summary>
/// An entry of the REGISTRO de diagnóstico del cliente (AC1, AC2, NFR-7).
///
/// <para>
/// La entrada lleva las CUATRO PARTES del error —el código estable, el mensaje, la causa y
/// el siguiente paso—, así que el jugador sabe qué falló, por qué y qué hacer ahora sin
/// interpretar un identificador (AD-15, UX-DR-36).
/// </para>
/// <para>
/// La entrada lleva ALSO el identificador del equipo y el de la operación, así que un
/// error del cliente se puede UNIR con el log del servidor, que comparte esos dos
/// identificadores (AC3, AD-23, AR-20).
/// </para>
/// <para>
/// La entrada lleva el TIPO de la operación que la produjo —una descarga, una instalación
/// o un lanzamiento—, así que el diagnóstico queda ASOCIADO a la operación que lo produjo
/// (AC2).
/// </para>
/// </summary>
public sealed class DiagnosticsLogEntry
{
    private readonly ErrorMessageViewModel _error;

    /// <summary>
    /// Creates an entry of the register of diagnostics.
    /// </summary>
    /// <param name="error">Error of the catalog that explains the failure.</param>
    /// <param name="clientId">Canonical identifier of the team that registered the error.</param>
    /// <param name="operationId">
    /// Identifier of the unit of work that produced the error, empty when the error is not
    /// bound to one.
    /// </param>
    /// <param name="operationType">Kind of operation that produced the error.</param>
    /// <param name="occurredAt">Instant of the error, in UTC.</param>
    public DiagnosticsLogEntry(
        ApiError error,
        string clientId,
        string operationId,
        string operationType,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(error);

        _error = new ErrorMessageViewModel(error);

        ClientId = clientId;
        OperationId = operationId;
        OperationType = operationType;
        OccurredAt = occurredAt;
    }

    /// <summary>
    /// Creates an entry of the register of diagnostics from the row that the client
    /// persisted (AD-11, NFR-7).
    /// </summary>
    /// <param name="error">Error that the client persisted.</param>
    public DiagnosticsLogEntry(PersistedClientError error)
        : this(
            new ApiError(
                error.Code,
                error.Message,
                error.Cause,
                error.NextStep),
            error.ClientId,
            error.OperationId,
            error.OperationType,
            error.OccurredAt)
    {
        ArgumentNullException.ThrowIfNull(error);
    }

    /// <summary>
    /// The stable code of the catalog. It is an INTERNAL identifier: it never replaces the
    /// text principal de la interfaz (AD-15, AD-25).
    /// </summary>
    public string Code => _error.Code;

    /// <summary>What failed (AD-15).</summary>
    public string Message => _error.Message;

    /// <summary>Why it failed (AD-15).</summary>
    public string Cause => _error.Cause;

    /// <summary>What to do now (AD-15).</summary>
    public string NextStep => _error.NextStep;

    /// <summary>Canonical identifier of the team that registered the error (AD-23).</summary>
    public string ClientId { get; }

    /// <summary>
    /// Identifier of the unit of work that produced the error, empty when the error is not
    /// bound to one (AD-23).
    /// </summary>
    public string OperationId { get; }

    /// <summary>
    /// Kind of operation that produced the error: a download, an installation or a launch
    /// (AC2).
    /// </summary>
    public string OperationType { get; }

    /// <summary>Instant of the error, in UTC.</summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>
    /// The FOUR parts of the error as ONE text, as
    /// «&lt;código&gt; · &lt;qué&gt; · &lt;por qué&gt; · &lt;qué hacer&gt;» (AD-15, UX-DR-36).
    /// </summary>
    public string Text => $"{Code} · {Message} · {Cause} · {NextStep}";
}

/// <summary>
/// View model of the VISOR de diagnóstico del cliente (AC1, AC2, AC3, AC4, NFR-7).
///
/// <para>
/// El visor muestra el registro de errores persistidos de su equipo, con sus cuatro
/// partes y con la asociación a la descarga, la instalación o el lanzamiento que los
/// produjo (AC1, AC2).
/// </para>
/// <para>
/// El visor FILTRA por <c>operationId</c>, por <c>clientId</c> y por tipo de operación,
/// así que el jugador puede aislar el diagnóstico de una unidad de trabajo concreta y
/// unirlo con el log del servidor (AC3, AD-23).
/// </para>
/// <para>
/// AC4: el visor dispone de VARIANTE CLARA con los tokens <c>-light</c> de
/// <c>DESIGN.md</c>, y el modo OSCURO es el PREDETERMINADO (UX-DR-41, UX-DR-1).
/// </para>
/// </summary>
public sealed class DiagnosticsLogViewModel
{
    /// <summary>Title of the visor de diagnóstico (AC1).</summary>
    public const string TitleText = "Diagnóstico";

    /// <summary>Text of the state empty of the visor (AC1, UX-DR-28).</summary>
    public const string EmptyText = "No hay errores registrados.";

    /// <summary>Label of the row of the FILTROS (AC2, AC3).</summary>
    public const string FiltersLabelText = "Filtros";

    /// <summary>Label of the conmutador de la variante clara (AC4, UX-DR-41).</summary>
    public const string LightVariantText = "Variante clara";

    /// <summary>Placeholder of the filter by <c>operationId</c> (AC3).</summary>
    public const string OperationIdFilterPlaceholder = "Identificador de operación";

    /// <summary>Placeholder of the filter by <c>clientId</c> (AC3).</summary>
    public const string ClientIdFilterPlaceholder = "Identificador del equipo";

    private readonly List<DiagnosticsLogEntry> _entries = [];
    private readonly Lock _gate = new();

    private string _operationIdFilter = string.Empty;
    private string _clientIdFilter = string.Empty;
    private string _operationTypeFilter = string.Empty;

    /// <summary>
    /// Creates the view model of the visor de diagnóstico.
    /// </summary>
    /// <param name="entries">Errors that the client registered.</param>
    public DiagnosticsLogViewModel(IEnumerable<DiagnosticsLogEntry>? entries = null)
    {
        if (entries is not null)
        {
            _entries.AddRange(entries);
        }
    }

    /// <summary>
    /// Raised when the entries or the filters change, así que la vista se repinta
    /// (AC1).
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// The entries of the register, with the filter applied (AC1, AC2, AC3).
    /// </summary>
    public IReadOnlyList<DiagnosticsLogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries.Where(Matches)];
            }
        }
    }

    /// <summary>
    /// Number of entries that the register holds, con el filtro aplicado (AC1).
    /// </summary>
    public int Count => Entries.Count;

    /// <summary>
    /// Whether the register has no entries that pass the filter (AC1).
    /// </summary>
    public bool IsEmpty => Count == 0;

    /// <summary>
    /// The kinds of operation that the register holds, para que el filtro ofrezca SOLO los
    /// que existen (AC2, AD-25).
    /// </summary>
    public IReadOnlyList<string> OperationTypes
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries
                    .Select(entry => entry.OperationType)
                    .Where(type => !string.IsNullOrWhiteSpace(type))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(type => type, StringComparer.Ordinal)];
            }
        }
    }

    /// <summary>
    /// Text of the filter by <c>operationId</c>. An empty filter matches EVERY entry
    /// (AC3).
    /// </summary>
    public string OperationIdFilter
    {
        get
        {
            lock (_gate)
            {
                return _operationIdFilter;
            }
        }

        set => ApplyFilter(() => _operationIdFilter = value ?? string.Empty);
    }

    /// <summary>
    /// Text of the filter by <c>clientId</c>. An empty filter matches EVERY entry (AC3).
    /// </summary>
    public string ClientIdFilter
    {
        get
        {
            lock (_gate)
            {
                return _clientIdFilter;
            }
        }

        set => ApplyFilter(() => _clientIdFilter = value ?? string.Empty);
    }

    /// <summary>
    /// Text of the filter by the KIND of operation. An empty filter matches EVERY entry
    /// (AC2).
    /// </summary>
    public string OperationTypeFilter
    {
        get
        {
            lock (_gate)
            {
                return _operationTypeFilter;
            }
        }

        set => ApplyFilter(() => _operationTypeFilter = value ?? string.Empty);
    }

    /// <summary>
    /// Whether the visor presents the VARIANTE CLARA (AC4, UX-DR-41).
    ///
    /// <para>
    /// El modo OSCURO es el PREDETERMINADO: la variante clara existe y se puede pedir,
    /// pero el visor no la aplica por su cuenta (UX-DR-1, UX-DR-41).
    /// </para>
    /// </summary>
    public bool IsLightVariant { get; private set; }

    /// <summary>
    /// The background of the visor, que es el token oscuro por defecto y el claro cuando
    /// el visor está en su variante clara (AC4, UX-DR-41).
    /// </summary>
    public Color Background => IsLightVariant
        ? DesignTokens.SurfaceBaseLight
        : DesignTokens.SurfaceBase;

    /// <summary>
    /// The ink of the text principal del visor (AC4, UX-DR-41).
    /// </summary>
    public Color PrimaryInk => IsLightVariant
        ? DesignTokens.InkPrimaryLight
        : DesignTokens.InkPrimary;

    /// <summary>
    /// The ink of the texto secundario del visor (AC4, UX-DR-41).
    /// </summary>
    public Color SecondaryInk => IsLightVariant
        ? DesignTokens.InkSecondaryLight
        : DesignTokens.InkSecondary;

    /// <summary>
    /// The border of hairline del visor (AC4, UX-DR-41).
    /// </summary>
    public Color HairlineBorder => IsLightVariant
        ? DesignTokens.BorderHairlineLight
        : DesignTokens.BorderHairline;

    /// <summary>
    /// The color of peligro del visor, que es el del catálogo de estados (AC4, DESIGN).
    /// </summary>
    public Color Danger => IsLightVariant
        ? DesignTokens.StateDangerLight
        : DesignTokens.StateDanger;

    /// <summary>
    /// Replaces the entries of the register with the ones that the client persisted
    /// (AC1).
    /// </summary>
    /// <param name="entries">Errors that the client registered.</param>
    public void ApplyEntries(IEnumerable<DiagnosticsLogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        lock (_gate)
        {
            _entries.Clear();
            _entries.AddRange(entries);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Replaces the entries of the register with the ones that the client persisted
    /// (AC1).
    /// </summary>
    /// <param name="errors">Errors that the client persisted.</param>
    public void ApplyErrors(IEnumerable<PersistedClientError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        ApplyEntries(errors.Select(error => new DiagnosticsLogEntry(error)));
    }

    /// <summary>
    /// Asks for the VARIANTE CLARA del visor, con los tokens <c>-light</c> de
    /// <c>DESIGN.md</c> (AC4, UX-DR-41).
    /// </summary>
    public void UseLightVariant()
    {
        IsLightVariant = true;

        Changed?.Invoke();
    }

    /// <summary>
    /// Vuelve al modo OSCURO, que es el PREDETERMINADO del cliente (AC4, UX-DR-1).
    /// </summary>
    public void UseDarkVariant()
    {
        IsLightVariant = false;

        Changed?.Invoke();
    }

    /// <summary>
    /// Applies a change of the filters and repaints the visor (AC2, AC3).
    /// </summary>
    /// <param name="change">Change of the filters.</param>
    private void ApplyFilter(Action change)
    {
        lock (_gate)
        {
            change();
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Tells whether an entry PASSES the filters of the visor (AC2, AC3).
    /// </summary>
    /// <param name="entry">Entry of the register.</param>
    /// <returns><c>true</c> when the entry matches every filter.</returns>
    private bool Matches(DiagnosticsLogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(_operationIdFilter) &&
            !string.Equals(entry.OperationId, _operationIdFilter, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_clientIdFilter) &&
            !string.Equals(entry.ClientId, _clientIdFilter, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_operationTypeFilter) &&
            !string.Equals(entry.OperationType, _operationTypeFilter, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }
}
