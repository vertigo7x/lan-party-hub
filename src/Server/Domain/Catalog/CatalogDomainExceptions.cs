namespace LANPartyHub.Server.Domain.Catalog;

/// <summary>
/// Base type of the exceptions of the domain of the catalog (AD-1, AD-15).
///
/// <para>
/// The exceptions of the domain are typed and they live in this layer. The adapter that
/// serves the request translates them into the single error shape of the system,
/// <c>ApiError</c>, and never the other way round (AD-15, AD-25).
/// </para>
/// </summary>
public abstract class CatalogDomainException : Exception
{
    /// <summary>Creates the exception of the domain of the catalog.</summary>
    /// <param name="message">Message that names the concrete field that fails.</param>
    protected CatalogDomainException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates the exception of the domain of the catalog.
    /// </summary>
    /// <param name="message">Message that names the concrete field that fails.</param>
    /// <param name="innerException">Exception that caused this one.</param>
    protected CatalogDomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The registration of a videogame carries a mandatory metadata of FR-1 that is empty or
/// absent (AD-1, AD-34).
///
/// <para>
/// The message names the concrete field that is missing, so the organizer knows which one
/// to fill in without having to guess (AD-25). The stable code of the catalog that
/// answers this exception also names the field, because the shape of <c>ApiError</c> has
/// no room for it (AD-15, AD-25).
/// </para>
/// </summary>
public sealed class GameMetadataMissingException : CatalogDomainException
{
    /// <summary>Creates the exception of a mandatory metadata that is absent.</summary>
    /// <param name="fieldName">Name of the field that is missing or empty.</param>
    public GameMetadataMissingException(string fieldName)
        : base($"Falta el metadato obligatorio '{fieldName}' del videojuego.")
    {
        FieldName = fieldName;
    }

    /// <summary>Name of the field that is missing or empty.</summary>
    public string FieldName { get; }
}

/// <summary>
/// The number of players of a videogame is not a valid range: the minimum is greater than
/// the maximum (AD-1, FR-1).
/// </summary>
public sealed class GamePlayerRangeException : CatalogDomainException
{
    /// <summary>Creates the exception of an invalid range of players.</summary>
    /// <param name="minPlayers">Minimum number of players that was given.</param>
    /// <param name="maxPlayers">Maximum number of players that was given.</param>
    public GamePlayerRangeException(int minPlayers, int maxPlayers)
        : base($"El número mínimo de jugadores ({minPlayers}) es mayor que el máximo ({maxPlayers}).")
    {
        MinPlayers = minPlayers;
        MaxPlayers = maxPlayers;
    }

    /// <summary>Minimum number of players that was given.</summary>
    public int MinPlayers { get; }

    /// <summary>Maximum number of players that was given.</summary>
    public int MaxPlayers { get; }
}

/// <summary>
/// The body of a mutation does not carry the <c>schemaVersion</c> that the API requires in
/// every mutation (AD-5).
/// </summary>
public sealed class SchemaVersionMissingException : CatalogDomainException
{
    /// <summary>Creates the exception of a body without the version of the schema.</summary>
    public SchemaVersionMissingException()
        : base("El cuerpo de la petición no lleva el campo schemaVersion.")
    {
    }
}

/// <summary>
/// The body of a mutation carries a <c>schemaVersion</c> that the server does not support
/// (AD-5, AD-8).
/// </summary>
public sealed class SchemaVersionNotSupportedException : CatalogDomainException
{
    /// <summary>Creates the exception of a version of the schema that is not supported.</summary>
    /// <param name="schemaVersion">Version of the schema that arrived in the body.</param>
    public SchemaVersionNotSupportedException(int schemaVersion)
        : base($"La versión del esquema {schemaVersion} no está soportada por este servidor.")
    {
        SchemaVersion = schemaVersion;
    }

    /// <summary>Version of the schema that arrived in the body.</summary>
    public int SchemaVersion { get; }
}

/// <summary>
/// The modification of a videogame carries a <c>gameId</c> that is not in the catalog
/// (AD-22).
/// </summary>
public sealed class GameNotFoundException : CatalogDomainException
{
    /// <summary>Creates the exception of a videogame that is not in the catalog.</summary>
    /// <param name="gameId">Identifier of the videogame that was not found.</param>
    public GameNotFoundException(string gameId)
        : base($"El videogame '{gameId}' no está en el catálogo.")
    {
        GameId = gameId;
    }

    /// <summary>Identifier of the videogame that was not found.</summary>
    public string GameId { get; }
}
