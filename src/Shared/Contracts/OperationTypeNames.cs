namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// The VOCABULARY of the kind of operation that travels in the projection of the clients
/// (AD-18, AD-22, R-MVP-7).
///
/// <para>
/// El tipo de una operación cruza el cable como texto, así que el NOMBRE tiene que vivir
/// en un SOLO sitio: el cliente lo declara y el servidor lo resuelve, así que duplicar la
/// constante en los dos lados es duplicar una fuente de verdad (R-MVP-7, AR-3).
/// </para>
/// <para>
/// Los nombres son estables y NO son texto libre: el servidor compara por IDENTIDAD, así
/// que un tipo que no está en esta tabla no se inventa (AD-22).
/// </para>
/// </summary>
public static class OperationTypeNames
{
    /// <summary>Name of the kind of operation of a DOWNLOAD of bytes.</summary>
    public const string Download = "download";

    /// <summary>Name of the kind of operation of an INSTALLATION of a package.</summary>
    public const string Installation = "installation";

    /// <summary>
    /// Name of the kind of operation of a LAUNCH of an installed videogame (AC2).
    ///
    /// <para>
    /// El lanzamiento es una UNIDAD DE TRABAJO del cliente, así que su error lleva el
    /// mismo identificador de operación que las demás, y el visor de diagnóstico puede
    /// ASOCIARLO al tipo de operación que lo produjo (AD-23).
    /// </para>
    /// </summary>
    public const string Launch = "launch";
}
