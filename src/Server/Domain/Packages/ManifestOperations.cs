using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Domain.Packages;

/// <summary>
/// The closed set of the operations of the installation manifest (AD-7, AD-8).
///
/// <para>
/// The operations of the manifest are typed and closed: <c>extract</c>,
/// <c>createDirectory</c>, <c>copyFile</c>, <c>writeConfig</c>, <c>setLaunchConfig</c> and
/// <c>verify</c>. There is no operation that runs arbitrary commands, and the set is
/// declared here, in the domain, so that the rule is a single one and not a list that each
/// side of the system writes on its own (AD-7).
/// </para>
/// <para>
/// The check is done by the type of the operation and not by the name of a string: the
/// polymorphic operations of the contract of <c>Shared</c> carry their discriminator, so
/// a value that is not one of the six known types is rejected here and never reaches the
/// persistence (AD-7).
/// </para>
/// </summary>
public static class ManifestOperations
{
    /// <summary>
    /// The names of the six operations of the closed set, in the order that the manifest
    /// declares them (AD-7).
    /// </summary>
    public static IReadOnlyList<string> AllowedNames =>
    [
        "extract",
        "createDirectory",
        "copyFile",
        "writeConfig",
        "setLaunchConfig",
        "verify",
    ];

    /// <summary>
    /// Tells whether a name belongs to the closed set of the operations of the manifest
    /// (AD-7).
    /// </summary>
    /// <param name="name">Name of the operation.</param>
    /// <returns><c>true</c> when the operation belongs to the closed set.</returns>
    public static bool IsAllowed(string? name) =>
        name is not null && AllowedNames.Contains(name, StringComparer.Ordinal);

    /// <summary>
    /// Requires that every operation of a manifest belongs to the closed set. An operation
    /// outside the set is rejected before anything is written (AD-7, AD-8).
    /// </summary>
    /// <param name="operations">Operations of the manifest to check.</param>
    /// <exception cref="ManifestOperationNotAllowedException">
    /// An operation of the manifest is outside the closed set.
    /// </exception>
    public static void EnsureAllowed(IEnumerable<InstallationManifest.ManifestOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        foreach (var operation in operations)
        {
            EnsureAllowed(operation);
        }
    }

    /// <summary>
    /// Requires that an operation belongs to the closed set of the operations of the
    /// manifest (AD-7, AD-8).
    /// </summary>
    /// <param name="operation">Operation of the manifest to check.</param>
    /// <exception cref="ManifestOperationNotAllowedException">
    /// The operation is outside the closed set.
    /// </exception>
    public static void EnsureAllowed(InstallationManifest.ManifestOperation? operation)
    {
        var name = NameOf(operation);

        if (!IsAllowed(name))
        {
            throw new ManifestOperationNotAllowedException(name ?? "desconocida");
        }
    }

    /// <summary>
    /// Reads the name of an operation, the way the discriminator of the contract of
    /// <c>Shared</c> writes it (AD-7).
    /// </summary>
    /// <param name="operation">Operation of the manifest.</param>
    /// <returns>The name of the operation, or <c>null</c> when the operation is absent.</returns>
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
