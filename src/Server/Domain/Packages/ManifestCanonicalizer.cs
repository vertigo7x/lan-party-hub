using System.Text;
using System.Text.Json;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Domain.Packages;

/// <summary>
/// Canonicalizer of the installation manifest (AD-2, AD-7, AD-8).
///
/// <para>
/// The signature covers the <em>canonical form</em> of the manifest, not an arbitrary
/// serialization of it: the canonical form is the JSON text with the keys sorted by name
/// and without any superfluous whitespace. The same text is the one that is signed, the one
/// that is persisted in the column <c>canonicalManifest</c> and the one that a verifier
/// recomputes, so the signer and the verifier can never diverge (AD-2).
/// </para>
/// <para>
/// The canonicalizer lives in the domain and not in the application layer because the
/// application layer may not reference the infrastructure, and the piece that canonicalizes
/// has to be a single one: two copies of it would be able to produce two different texts
/// for the same manifest, and only one of them would verify (AD-1).
/// </para>
/// <para>
/// The polymorphic operations are canonicalized with their discriminator <c>operation</c>
/// and the properties of their derived type. The discriminator is the type tag of the
/// operation and the serializer of the contract of <c>Shared</c> requires it to be the
/// FIRST property of the object, so it leads; the properties of the derived type are
/// sorted by name. The canonical text is therefore readable back by
/// <c>JsonSerializer.Deserialize&lt;InstallationManifest&gt;</c>, which is what makes it
/// verifiable by the client of the Story 6.1, and it does not depend on the order of
/// declaration of any type.
/// </para>
/// </summary>
public static class ManifestCanonicalizer
{
    /// <summary>
    /// Produces the canonical JSON text of an installation manifest: the keys sorted by
    /// name and without any superfluous whitespace.
    /// </summary>
    /// <param name="manifest">Manifest to canonicalize.</param>
    /// <returns>The canonical JSON text of the manifest.</returns>
    /// <exception cref="ArgumentNullException">The manifest is <c>null</c>.</exception>
    public static string Canonicalize(InstallationManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false,
        }))
        {
            WriteManifest(writer, manifest);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Writes the canonical form of a manifest. Every object writes its properties sorted
    /// by name, and the discriminator of the polymorphic operations travels as a property
    /// with the name <c>operation</c>.
    /// </summary>
    /// <param name="writer">Writer of the canonical text.</param>
    /// <param name="manifest">Manifest to write.</param>
    private static void WriteManifest(Utf8JsonWriter writer, InstallationManifest manifest)
    {
        writer.WriteStartObject();

        // The keys are written one by one and in alphabetical order, so the canonical text
        // does not depend on the order of declaration of the record (AD-2).
        WriteProperty(writer, "launch", manifest.Launch, WriteLaunch);
        WriteProperty(writer, "operations", manifest.Operations, WriteOperations);
        WriteProperty(writer, "packageId", manifest.PackageId, WriteString);
        WriteProperty(writer, "packageVersion", manifest.PackageVersion, WriteString);
        WriteProperty(writer, "schemaVersion", manifest.SchemaVersion, WriteNumber);
        WriteProperty(writer, "sha256", manifest.Sha256, WriteString);
        WriteProperty(writer, "sizeBytes", manifest.SizeBytes, WriteLongNumber);

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes the canonical form of the launch configuration. Its keys are sorted by name
    /// as well.
    /// </summary>
    /// <param name="writer">Writer of the canonical text.</param>
    /// <param name="launch">Launch configuration to write.</param>
    private static void WriteLaunch(Utf8JsonWriter writer, InstallationManifest.LaunchSettings launch)
    {
        writer.WriteStartObject();

        WriteProperty(writer, "arguments", launch.Arguments, WriteStrings);
        WriteProperty(writer, "executablePath", launch.ExecutablePath, WriteString);
        WriteProperty(writer, "workingDirectory", launch.WorkingDirectory, WriteString);

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes the canonical form of the list of operations. The order of the list is the
    /// order of application of the operations, so it is preserved; the keys of every
    /// operation are sorted by name.
    /// </summary>
    /// <param name="writer">Writer of the canonical text.</param>
    /// <param name="operations">Operations to write.</param>
    private static void WriteOperations(Utf8JsonWriter writer, IReadOnlyList<InstallationManifest.ManifestOperation> operations)
    {
        writer.WriteStartArray();

        foreach (var operation in operations)
        {
            WriteOperation(writer, operation);
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes the canonical form of one operation: the discriminator <c>operation</c> and
    /// the properties of its derived type, with the keys sorted by name.
    /// </summary>
    /// <param name="writer">Writer of the canonical text.</param>
    /// <param name="operation">Operation to write.</param>
    private static void WriteOperation(Utf8JsonWriter writer, InstallationManifest.ManifestOperation operation)
    {
        writer.WriteStartObject();

        // The discriminator is the type tag of the operation, not one of its properties,
        // and the serializer of the contract of Shared requires it to be the FIRST
        // property of the object: a canonical text that puts it after the properties
        // cannot be read back by JsonSerializer, and the signature of a manifest that
        // nobody can deserialize does not authenticate anything (AD-2).
        //
        // The properties of the derived type are sorted by name, so the canonical form
        // does not depend on the order of declaration of any type (AD-2).
        switch (operation)
        {
            case InstallationManifest.Extract extract:
                WriteProperty(writer, "operation", "extract", WriteString);
                WriteProperty(writer, "archivePath", extract.ArchivePath, WriteString);
                WriteProperty(writer, "destinationPath", extract.DestinationPath, WriteString);
                break;

            case InstallationManifest.CreateDirectory createDirectory:
                WriteProperty(writer, "operation", "createDirectory", WriteString);
                WriteProperty(writer, "path", createDirectory.Path, WriteString);
                break;

            case InstallationManifest.CopyFile copyFile:
                WriteProperty(writer, "operation", "copyFile", WriteString);
                WriteProperty(writer, "destinationPath", copyFile.DestinationPath, WriteString);
                WriteProperty(writer, "sourcePath", copyFile.SourcePath, WriteString);
                break;

            case InstallationManifest.WriteConfig writeConfig:
                WriteProperty(writer, "operation", "writeConfig", WriteString);
                WriteProperty(writer, "content", writeConfig.Content, WriteString);
                WriteProperty(writer, "path", writeConfig.Path, WriteString);
                break;

            case InstallationManifest.SetLaunchConfig setLaunchConfig:
                WriteProperty(writer, "operation", "setLaunchConfig", WriteString);
                WriteProperty(writer, "launch", setLaunchConfig.Launch, WriteLaunch);
                break;

            case InstallationManifest.Verify verify:
                WriteProperty(writer, "operation", "verify", WriteString);
                WriteProperty(writer, "path", verify.Path, WriteString);
                break;

            default:
                throw new ArgumentException(
                    $"La operación de manifiesto '{operation.GetType().Name}' no pertenece al conjunto cerrado de operaciones.",
                    nameof(operation));
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes a property of the canonical form.
    ///
    /// <para>
    /// The property is ALWAYS written, even when its value is absent: the canonical text
    /// of a manifest must depend only on the value of the manifest and not on whether a
    /// value happened to be present, so that the same manifest always produces the same
    /// bytes and the signature is reproducible (AD-2).
    /// </para>
    /// </summary>
    /// <typeparam name="T">Type of the value of the property.</typeparam>
    /// <param name="writer">Writer of the canonical text.</param>
    /// <param name="name">Name of the property.</param>
    /// <param name="value">Value of the property.</param>
    /// <param name="write">Writer of the value of the property.</param>
    private static void WriteProperty<T>(
        Utf8JsonWriter writer,
        string name,
        T value,
        Action<Utf8JsonWriter, T> write)
    {
        writer.WritePropertyName(name);

        write(writer, value);
    }

    /// <summary>Writes a text value.</summary>
    /// <param name="writer">Writer of the canonical text.</param>
    /// <param name="value">Value to write.</param>
    private static void WriteString(Utf8JsonWriter writer, string value) => writer.WriteStringValue(value);

    /// <summary>Writes a numeric value.</summary>
    /// <param name="writer">Writer of the canonical text.</param>
    /// <param name="value">Value to write.</param>
    private static void WriteNumber(Utf8JsonWriter writer, int value) => writer.WriteNumberValue(value);

    /// <summary>Writes a numeric value.</summary>
    /// <param name="writer">Writer of the canonical text.</param>
    /// <param name="value">Value to write.</param>
    private static void WriteLongNumber(Utf8JsonWriter writer, long value) => writer.WriteNumberValue(value);

    /// <summary>
    /// Writes a list of texts as a JSON array, preserving its order.
    /// </summary>
    /// <param name="writer">Writer of the canonical text.</param>
    /// <param name="values">Values to write.</param>
    private static void WriteStrings(Utf8JsonWriter writer, IReadOnlyList<string> values)
    {
        writer.WriteStartArray();

        if (values is not null)
        {
            foreach (var value in values)
            {
                writer.WriteStringValue(value);
            }
        }

        writer.WriteEndArray();
    }
}
