using System.Collections;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Infrastructure.Logging;

/// <summary>
/// Correlation scope of a unit of work (AD-15, AD-23).
///
/// <para>
/// The scope carries the identifiers that tie a log entry to the work that produced it:
/// the <c>operationId</c>, which identifies the unit of work, and the <c>clientId</c>,
/// which identifies the client that asked for it. Both travel in the scope of the log, so
/// every entry written inside it carries them.
/// </para>
/// <para>
/// The identifier of the client is absent only in the entries of a request that was
/// rejected because it did not carry it: the unit of work exists and it has its
/// <c>operationId</c>, but there is no client to correlate it with.
/// </para>
/// <para>
/// The scope is data and not a message: it never carries a credential (AD-15) and it
/// never composes the text of an error (AD-25).
/// </para>
/// </summary>
public sealed class CorrelationScope : IReadOnlyList<KeyValuePair<string, object?>>
{
    /// <summary>Name of the field that carries the identifier of the unit of work.</summary>
    public const string OperationIdFieldName = "OperationId";

    /// <summary>Name of the field that carries the identifier of the client.</summary>
    public const string ClientIdFieldName = "ClientId";

    private readonly KeyValuePair<string, object?>[] _fields;

    private CorrelationScope(string operationId, string? clientId)
    {
        OperationId = operationId;
        ClientId = clientId;

        _fields = clientId is null
            ? [new KeyValuePair<string, object?>(OperationIdFieldName, operationId)]
            :
            [
                new KeyValuePair<string, object?>(OperationIdFieldName, operationId),
                new KeyValuePair<string, object?>(ClientIdFieldName, clientId),
            ];
    }

    /// <summary>Identifier of the unit of work.</summary>
    public string OperationId { get; }

    /// <summary>
    /// Identifier of the client that owns the unit of work, or <c>null</c> when the request
    /// did not carry a usable one.
    /// </summary>
    public string? ClientId { get; }

    /// <summary>
    /// Creates the scope of a unit of work.
    /// </summary>
    /// <param name="operationId">Identifier of the unit of work. It is never empty.</param>
    /// <param name="clientId">
    /// Identifier of the client, or <c>null</c> when the request did not carry a usable
    /// one.
    /// </param>
    /// <returns>The scope of the unit of work.</returns>
    /// <exception cref="ArgumentException">The identifier of the operation is empty.</exception>
    public static CorrelationScope Create(string operationId, string? clientId)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            throw new ArgumentException("El identificador de la operación no puede estar vacío.", nameof(operationId));
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            clientId = null;
        }

        return new CorrelationScope(operationId, clientId);
    }

    /// <inheritdoc />
    public int Count => _fields.Length;

    /// <inheritdoc />
    public KeyValuePair<string, object?> this[int index] => _fields[index];

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
        ((IEnumerable<KeyValuePair<string, object?>>)_fields).GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => _fields.GetEnumerator();

    /// <inheritdoc />
    public override string ToString() => "CorrelationScope";
}

/// <summary>
/// Factory that opens the correlation scope in the log of the host (AD-15).
///
/// <para>
/// The scope is pushed into the <see cref="ILoggerFactory"/> of the process, so every
/// logger that the factory creates carries it while the scope is open: the entry of the
/// hosting layer, the entry of the middleware and the entry of the endpoint belong to the
/// same unit of work.
/// </para>
/// </summary>
public sealed class CorrelationScopeFactory
{
    /// <summary>Category of the logger that opens the scope.</summary>
    public const string CategoryName = "LANPartyHub.Server.Correlation";

    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Creates the factory of the correlation scope.</summary>
    /// <param name="loggerFactory">Factory of the log of the host.</param>
    public CorrelationScopeFactory(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Opens the scope of a unit of work.
    /// </summary>
    /// <param name="operationId">Identifier of the unit of work.</param>
    /// <param name="clientId">
    /// Identifier of the client, or <c>null</c> when the request did not carry a usable
    /// one.
    /// </param>
    /// <returns>A handle that closes the scope when it is disposed.</returns>
    public IDisposable Open(string operationId, string? clientId) =>
        Open(CorrelationScope.Create(operationId, clientId));

    /// <summary>
    /// Opens the scope of a unit of work.
    /// </summary>
    /// <param name="scope">Scope of the unit of work.</param>
    /// <returns>A handle that closes the scope when it is disposed.</returns>
    public IDisposable Open(CorrelationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var handle = _loggerFactory.CreateLogger(CategoryName).BeginScope(scope);

        return handle ?? NullScope.Instance;
    }

    /// <summary>
    /// Scope that does nothing. It exists because the log of a process without a scope
    /// provider cannot open one, and a caller must always be able to close what it opened.
    /// </summary>
    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
            // There is nothing to close.
        }
    }
}
