using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Domain.Install;

/// <summary>
/// The rule that says which operation of the installation manifest is PRIVILEGED (AD-7,
/// AC2).
///
/// <para>
/// La regla es PURA y vive en el DOMINIO: recibe una operación del conjunto cerrado y
/// dice si es privilegiada, sin tocar el disco, sin red y sin conocer el adaptador
/// (AD-1, AD-16).
/// </para>
/// <para>
/// AD-7 NO fija qué operación es privilegiada, así que 7.3 lo fija aquí, en UN SOLO
/// sitio: es privilegiada la operación que escribe contenido que NO son los bytes del
/// paquete validado (<c>writeConfig</c>) o que materializa la configuración de ejecución
/// (<c>setLaunchConfig</c>). Las demás —<c>extract</c>, <c>copyFile</c>,
/// <c>createDirectory</c> y <c>verify</c>— sólo materializan bytes que el manifiesto
/// firmado ya autorizó, así que NO preguntan.
/// </para>
/// <para>
/// La interfaz NO rederiva la regla: la lee de aquí, así que no puede discrepar del
/// ejecutor que la aplica (AD-1, AD-7).
/// </para>
/// </summary>
public static class PrivilegedOperationPolicy
{
    /// <summary>
    /// Tells whether an operation of the manifest is PRIVILEGED (AD-7, AC2).
    ///
    /// <para>
    /// Una operación que NO pertenece al conjunto cerrado NUNCA es privilegiada: no se
    /// ejecuta, así que no hay nada que confirmar (AD-7, AC2).
    /// </para>
    /// </summary>
    /// <param name="operation">Operation of the manifest.</param>
    /// <returns><c>true</c> when the operation needs an explicit confirmation.</returns>
    public static bool IsPrivileged(InstallationManifest.ManifestOperation? operation) => operation switch
    {
        // La operación que escribe CONFIGURACIÓN escribe contenido que no son los bytes
        // del paquete validado, así que pide confirmación explícita (AD-7, AC2).
        InstallationManifest.WriteConfig => true,

        // La operación que MATERIALIZA la configuración de ejecución escribe fuera de los
        // bytes del paquete, así que pide confirmación explícita (AD-7, AC2).
        InstallationManifest.SetLaunchConfig => true,

        // Las demás sólo materializan bytes firmados o comprueban el árbol, así que NO
        // preguntan (AD-7, AC2).
        InstallationManifest.Extract => false,
        InstallationManifest.CopyFile => false,
        InstallationManifest.CreateDirectory => false,
        InstallationManifest.Verify => false,

        _ => false,
    };

    /// <summary>
    /// Tells whether an operation of the manifest is PRIVILEGED, by the name that the
    /// discriminator del contrato escribe (AD-7, AC2).
    ///
    /// <para>
    /// La comparación es EXACTA, por la misma razón que la del servidor: el
    /// discriminador polimórfico del contrato se resuelve contra la cadena EXACTA que
    /// declara el <c>JsonDerivedType</c>, así que un nombre que no coincide no es una
    /// operación del conjunto cerrado (AD-7, AD-15, AD-25).
    /// </para>
    /// </summary>
    /// <param name="operationName">Name of the operation of the manifest.</param>
    /// <returns><c>true</c> when the operation needs an explicit confirmation.</returns>
    public static bool IsPrivileged(string? operationName) => operationName switch
    {
        "writeConfig" => true,
        "setLaunchConfig" => true,
        _ => false,
    };

    /// <summary>
    /// The name of an operation of the manifest, the way the discriminator of the contract
    /// of <c>Shared</c> writes it (AD-7).
    ///
    /// <param name="operation">Operation of the manifest.</param>
    /// <returns>The name of the operation, or <c>null</c> when it is absent.</returns>
    public static string? NameOf(InstallationManifest.ManifestOperation? operation) => operation switch
    {
        InstallationManifest.Extract => "extract",
        InstallationManifest.CreateDirectory => "createDirectory",
        InstallationManifest.CopyFile => "copyFile",
        InstallationManifest.WriteConfig => "writeConfig",
        InstallationManifest.SetLaunchConfig => "setLaunchConfig",
        InstallationManifest.Verify => "verify",
        _ => null,
    };
}
