using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Body of the administration of the videogames of the catalog (AD-5, AD-34).
///
/// <para>
/// The body is a <em>sobre</em> around the contract of <c>Shared</c>: it carries the
/// <c>schemaVersion</c> that every mutation of the API carries (AD-5) and, optionally,
/// the identifier of the videogame that is being modified. The eighteen metadata of FR-1
/// travel in the <see cref="GameDescriptor"/> that the body brings, so this type does not
/// repeat a single name of a field and cannot diverge from the contract (AD-34).
/// </para>
/// <para>
/// The body is a type of the server and not a contract of <c>Shared</c>: AD-18 fixes
/// exhaustively the twenty contracts of <c>Shared</c> and the body of an administration
/// request is consumed only by the server, never by the client of the LAN.
/// </para>
/// <para>
/// The identifier of the videogame is what tells the alta and the modificación apart: a
/// body that does not carry it registers a videogame and the server generates the
/// identifier; a body that carries it modifies that videogame and the identifier never
/// changes (AD-34).
/// </para>
/// </summary>
public sealed class GameRequest
{
    /// <summary>
    /// Version of the schema of the body. A body that does not carry it, or that carries a
    /// value that the server does not support, is rejected before anything is written
    /// (AD-5).
    /// </summary>
    public int? SchemaVersion { get; set; }

    /// <summary>
    /// Identifier of the videogame to modify. It is absent in the registration, and the
    /// server generates it then (AD-23).
    /// </summary>
    public string? GameId { get; set; }

    /// <summary>
    /// The eighteen metadata of FR-1, with the exact form of the contract of
    /// <c>Shared</c> (AD-34).
    /// </summary>
    public GameDescriptor? Game { get; set; }

    /// <summary>
    /// Projects the body of the request onto the contract that the use cases of the
    /// catalog receive. The identifier of the videogame is the one of the body, or the
    /// one that the descriptor carries when the body does not name it.
    /// </summary>
    /// <returns>The contract of the videogame.</returns>
    /// <exception cref="GameRequestInvalidException">
    /// The body does not carry the metadata of the videogame.
    /// </exception>
    public GameDescriptor ToDescriptor()
    {
        if (Game is null)
        {
            throw new GameRequestInvalidException(
                "El cuerpo de la petición no lleva los metadatos del videojuego.");
        }

        if (string.IsNullOrWhiteSpace(GameId))
        {
            return Game;
        }

        return Game with { GameId = GameId.Trim() };
    }
}

/// <summary>
/// The body of the administration does not carry the metadata of the videogame (AD-5,
/// AD-34).
/// </summary>
public sealed class GameRequestInvalidException : Exception
{
    /// <summary>Creates the exception of a body that does not carry the metadata.</summary>
    /// <param name="message">Message that tells what is missing in the body.</param>
    public GameRequestInvalidException(string message)
        : base(message)
    {
    }
}
