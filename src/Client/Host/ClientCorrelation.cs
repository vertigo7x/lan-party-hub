using System.Collections;
using System.Collections.Generic;
using LANPartyHub.Client.Application.Ports;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Host;

/// <summary>
/// Correlation scope of the client (AD-15, AD-23).
///
/// <para>
/// The scope carries the two identifiers that tie a log entry to the work that produced
/// it: the <c>clientId</c>, which is the technical identity of the team and never
/// changes, and the <c>operationId</c>, which identifies the unit of work that is
/// running. Both travel in the scope of the log, so every entry written inside it carries
/// them.
/// </para>
/// <para>
/// The scope is data and not a message: it never carries a credential (AD-15) and it
/// never composes the text of an error (AD-25).
/// </para>
/// </summary>
public sealed class ClientCorrelation : IReadOnlyList<KeyValuePair<string, object?>>
{
    /// <summary>Name of the field that carries the identifier of the unit of work.</summary>
    public const string OperationIdFieldName = "OperationId";

    /// <summary>Name of the field that carries the identifier of the client.</summary>
    public const string ClientIdFieldName = "ClientId";

    private readonly KeyValuePair<string, object?>[] _fields;

    private ClientCorrelation(string clientId, string? operationId)
    {
        ClientId = clientId;
        OperationId = operationId;

        _fields = operationId is null
            ? [new KeyValuePair<string, object?>(ClientIdFieldName, clientId)]
            :
            [
                new KeyValuePair<string, object?>(ClientIdFieldName, clientId),
                new KeyValuePair<string, object?>(OperationIdFieldName, operationId),
            ];
    }

    /// <summary>Canonical identifier of the team (AD-23).</summary>
    public string ClientId { get; }

    /// <summary>Identifier of the unit of work, or <c>null</c> when there is none.</summary>
    public string? OperationId { get; }

    /// <summary>
    /// Creates the scope of the client.
    /// </summary>
    /// <param name="clientId">Canonical identifier of the team. It is never empty.</param>
    /// <param name="operationId">
    /// Identifier of the unit of work that is running, or <c>null</c> when the client is
    /// not running one.
    /// </param>
    /// <returns>The scope of the client.</returns>
    /// <exception cref="ArgumentException">The identifier of the client is empty.</exception>
    public static ClientCorrelation Create(string clientId, string? operationId = null)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("El identificador del cliente no puede estar vacío.", nameof(clientId));
        }

        if (operationId is not null && string.IsNullOrWhiteSpace(operationId))
        {
            throw new ArgumentException("El identificador de la operación no puede estar vacío.", nameof(operationId));
        }

        return new ClientCorrelation(clientId, operationId);
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
    public override string ToString() => "ClientCorrelation";
}

/// <summary>
/// Factory that opens the correlation scope of the client in its log (AD-15).
///
/// <para>
/// The scope is pushed into the <see cref="ILoggerFactory"/> of the client, so every
/// logger that the factory creates carries it while the scope is open: the entry of the
/// startup, the entry of the reconciliation and the entry of the operation that is
/// running belong to the same unit of work.
/// </para>
/// <para>
/// The scope belongs to the composition root: it is opened as soon as the identity of the
/// team is known and it is closed when the session ends, never when the startup ends.
/// </para>
/// </summary>
public sealed class ClientCorrelationFactory : IClientCorrelationScope
{
    /// <summary>Category of the logger that opens the scope.</summary>
    public const string CategoryName = "LANPartyHub.Client.Correlation";

    private readonly ILoggerFactory _loggerFactory;
    private readonly Lock _lock = new();

    private IDisposable? _scope;
    private string? _clientId;
    private string? _operationId;

    /// <summary>Creates the factory of the correlation scope.</summary>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    public ClientCorrelationFactory(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public string? ClientId
    {
        get
        {
            lock (_lock)
            {
                return _clientId;
            }
        }
    }

    /// <inheritdoc />
    public string? OperationId
    {
        get
        {
            lock (_lock)
            {
                return _operationId;
            }
        }
    }

    /// <inheritdoc />
    public void Open(string clientId, string? operationId = null)
    {
        var correlation = ClientCorrelation.Create(clientId, operationId);

        lock (_lock)
        {
            _scope?.Dispose();

            _scope = _loggerFactory.CreateLogger(CategoryName).BeginScope(correlation) ?? NullScope.Instance;
            _clientId = correlation.ClientId;
            _operationId = correlation.OperationId;
        }
    }

    /// <summary>
    /// Closes the scope that is open. The composition root calls it when the session ends.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _scope?.Dispose();

            _scope = null;
            _clientId = null;
            _operationId = null;
        }
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
