using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Presentation.Trust;

/// <summary>
/// View model of the panel of the ancla de confianza (AC1, AD-27).
///
/// <para>
/// AD-27: el panel existe para que el organizador LEA el fingerprint del ancla y lo
/// reparta FUERA de banda. El fingerprint va en bloques de 4 caracteres, que es como la
/// persona lo dicta por teléfono o lo transcribe de un cartel, y el código QR lleva el
/// fingerprint y la dirección del servidor para que el jugador lo escanee (AD-27).
/// </para>
/// <para>
/// El panel NO calcula ni decide nada: la clave y el fingerprint salen del adaptador que
/// posee la clave del servidor, de modo que la vista no puede derivar un valor distinto
/// del que el servidor publica (AD-1, AD-27).
/// </para>
/// </summary>
public sealed class TrustAnchorViewModel
{
    /// <summary>
    /// Number of characters of every block of the fingerprint (AD-27).
    ///
    /// <para>
    /// AD-27: el servidor muestra el fingerprint «como texto corto y agrupado por bloques de
    /// 4 caracteres», porque ese es el formato que la persona puede contrastar contra la
    /// pantalla del cliente sin leer 64 caracteres de corrido.
    /// </para>
    /// </summary>
    public const int BlockLength = 4;

    /// <summary>
    /// Creates the view model of the panel of the ancla de confianza.
    /// </summary>
    /// <param name="anchor">Ancla de confianza that the server publishes.</param>
    /// <param name="serverAddress">Address of the server, in the form <c>host:puerto</c>.</param>
    public TrustAnchorViewModel(TrustAnchor anchor, string serverAddress)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverAddress);

        Anchor = anchor;
        ServerAddress = serverAddress;
    }

    /// <summary>The ancla de confianza that the server publishes.</summary>
    public TrustAnchor Anchor { get; }

    /// <summary>Address of the server, in the form <c>host:puerto</c>.</summary>
    public string ServerAddress { get; }

    /// <summary>
    /// The fingerprint of the ancla, in lowercase hexadecimal (AD-27).
    /// </summary>
    public string Fingerprint => Anchor.Fingerprint;

    /// <summary>
    /// The fingerprint grouped in blocks of 4 characters, separated by a single space, so
    /// the organizer can read it aloud and the player can contrast it against the screen
    /// (AD-27).
    /// </summary>
    public string GroupedFingerprint => Group(Fingerprint);

    /// <summary>
    /// Groups a fingerprint in blocks of <see cref="BlockLength"/> characters, separated by
    /// a single space.
    /// </summary>
    /// <param name="fingerprint">Fingerprint to group.</param>
    /// <returns>The grouped fingerprint.</returns>
    public static string Group(string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);

        if (fingerprint.Length == 0)
        {
            return string.Empty;
        }

        var blocks = new List<string>();

        for (var index = 0; index < fingerprint.Length; index += BlockLength)
        {
            blocks.Add(fingerprint.Substring(index, Math.Min(BlockLength, fingerprint.Length - index)));
        }

        return string.Join(' ', blocks);
    }

    /// <summary>
    /// Text that tells the organizer WHAT this value is and WHY it has to be delivered out
    /// of band (AD-27).
    ///
    /// <para>
    /// AD-27: la distribución del ancla es un requisito de operación, no un detalle de
    /// interfaz. Si el organizador reparte el ejecutable y el fingerprint por el mismo
    /// mensaje sin verificar, el fingerprint NO es out-of-band y el cliente tiene que
    /// rechazar la conexión. El texto dice exactamente eso.
    /// </para>
    /// </summary>
    public string InstructionsText =>
        "Entrega este fingerprint a los jugadores por un canal que NO pase por la red del evento "
        + "(mesa de registro, cartel o chat). Si lo repartes junto al ejecutable sin verificarlo, "
        + "el cliente rechazará la conexión.";
}
