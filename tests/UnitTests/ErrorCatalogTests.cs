using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The error catalog (AD-15, AD-25, AD-27). Every error has the three part message, the
/// code is never the primary text and an unknown code resolves into the generic message
/// without losing its own code.
/// </summary>
public sealed class ErrorCatalogTests
{
    [Fact]
    public void Every_entry_declares_the_three_parts_of_the_message()
    {
        Assert.NotEmpty(ErrorCatalog.Entries);

        foreach (var entry in ErrorCatalog.Entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Message));
            Assert.False(string.IsNullOrWhiteSpace(entry.Cause));
            Assert.False(string.IsNullOrWhiteSpace(entry.NextStep));
        }
    }

    [Fact]
    public void The_code_of_an_entry_is_never_its_primary_text()
    {
        foreach (var entry in ErrorCatalog.Entries)
        {
            Assert.NotEqual(entry.Code, entry.Message);
            Assert.False(entry.Message.StartsWith(entry.Code, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void An_unknown_code_resolves_into_the_generic_message()
    {
        const string unknownCode = "A_CODE_THAT_IS_NOT_IN_THE_CATALOG";
        var generic = ErrorCatalog.Entries.Single(entry => entry.Code == ErrorCatalog.UnknownErrorCode);

        var resolved = ErrorCatalog.Resolve(unknownCode);

        Assert.Equal(generic.Message, resolved.Message);
        Assert.Equal(generic.Cause, resolved.Cause);
        Assert.Equal(generic.NextStep, resolved.NextStep);
    }

    [Fact]
    public void An_unknown_code_keeps_its_own_code_and_shows_the_generic_text()
    {
        const string unknownCode = "A_CODE_THAT_IS_NOT_IN_THE_CATALOG";
        var generic = ErrorCatalog.Entries.Single(entry => entry.Code == ErrorCatalog.UnknownErrorCode);

        var resolved = ErrorCatalog.Resolve(unknownCode);

        Assert.Equal(unknownCode, resolved.Code);
        Assert.Equal(generic.Message, resolved.Message);
        Assert.Equal(generic.Cause, resolved.Cause);
        Assert.Equal(generic.NextStep, resolved.NextStep);
    }

    [Fact]
    public void An_unknown_code_is_never_shown_as_the_primary_text()
    {
        const string unknownCode = "A_CODE_THAT_IS_NOT_IN_THE_CATALOG";

        var error = ErrorCatalog.Resolve(unknownCode);

        Assert.NotEqual(unknownCode, error.Message);
        Assert.False(error.Message.Contains(unknownCode, StringComparison.OrdinalIgnoreCase));
        Assert.False(error.Cause.Contains(unknownCode, StringComparison.OrdinalIgnoreCase));
        Assert.False(error.NextStep.Contains(unknownCode, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Try_get_returns_false_and_the_default_value_for_an_unknown_code()
    {
        const string unknownCode = "A_CODE_THAT_IS_NOT_IN_THE_CATALOG";

        var found = ErrorCatalog.TryGet(unknownCode, out var error);

        Assert.False(found);
        Assert.Null(error);
    }

    [Fact]
    public void A_known_code_resolves_into_its_own_entry()
    {
        var resolved = ErrorCatalog.Resolve(ErrorCatalog.AnchorFingerprintMismatchCode);

        Assert.True(ErrorCatalog.TryGet(ErrorCatalog.AnchorFingerprintMismatchCode, out var entry));
        Assert.NotNull(entry);
        Assert.Equal(entry, resolved);
        Assert.Equal(ErrorCatalog.AnchorFingerprintMismatchCode, resolved.Code);
    }

    [Fact]
    public void The_anchor_mismatch_and_the_invalid_signature_are_different_codes()
    {
        Assert.NotEqual(
            ErrorCatalog.AnchorFingerprintMismatchCode,
            ErrorCatalog.ManifestSignatureInvalidCode);
    }

    [Fact]
    public void The_machine_name_rejection_has_its_own_code()
    {
        var codes = ErrorCatalog.Codes;

        Assert.Contains(ErrorCatalog.MachineNameInvalidCode, codes);
        Assert.DoesNotContain(ErrorCatalog.MachineNameInvalidCode, codes.Where(code =>
            code is ErrorCatalog.AnchorFingerprintMismatchCode or ErrorCatalog.ManifestSignatureInvalidCode));
    }

    [Fact]
    public void The_missing_client_identifier_has_its_own_code_and_its_three_parts()
    {
        var codes = ErrorCatalog.Codes;

        Assert.Contains(ErrorCatalog.ClientIdMissingCode, codes);
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());

        Assert.True(ErrorCatalog.TryGet(ErrorCatalog.ClientIdMissingCode, out var entry));
        Assert.NotNull(entry);
        Assert.False(string.IsNullOrWhiteSpace(entry!.Message));
        Assert.False(string.IsNullOrWhiteSpace(entry.Cause));
        Assert.False(string.IsNullOrWhiteSpace(entry.NextStep));
    }

    [Fact]
    public void The_withdrawn_package_outcome_is_not_registered_as_an_error()
    {
        foreach (var entry in ErrorCatalog.Entries)
        {
            Assert.DoesNotContain("retirado", entry.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("retirado", entry.Cause, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("retirado", entry.NextStep, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_catalog_holds_one_entry_per_stable_code()
    {
        var codes = ErrorCatalog.Codes.ToArray();

        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ErrorCatalog.Entries.Count, codes.Length);
    }

    [Fact]
    public void The_exposed_entries_cannot_be_mutated_by_a_consumer()
    {
        Assert.IsNotType<ApiError[]>(ErrorCatalog.Entries);

        var entries = ErrorCatalog.Entries as IList<ApiError>;

        Assert.NotNull(entries);
        Assert.True(entries!.IsReadOnly);
    }

    [Fact]
    public void The_codes_of_the_publication_have_their_own_entry_of_three_parts()
    {
        string[] codes =
        [
            ErrorCatalog.PackagePayloadMissingCode,
            ErrorCatalog.PackageLicenseMissingCode,
            ErrorCatalog.PackageDistributionRestrictionsMissingCode,
            ErrorCatalog.PackageGameNotFoundCode,
            ErrorCatalog.PackageAlreadyPublishedCode,
            ErrorCatalog.PackageContentMismatchCode,
            ErrorCatalog.ManifestOperationNotAllowedCode,
        ];

        foreach (var code in codes)
        {
            Assert.Contains(code, ErrorCatalog.Codes);

            Assert.True(ErrorCatalog.TryGet(code, out var entry));
            Assert.NotNull(entry);
            Assert.False(string.IsNullOrWhiteSpace(entry!.Message));
            Assert.False(string.IsNullOrWhiteSpace(entry.Cause));
            Assert.False(string.IsNullOrWhiteSpace(entry.NextStep));
        }
    }

    [Fact]
    public void The_two_blocks_of_the_publication_are_two_different_codes()
    {
        // The two absences are counted apart, so that the metric of errors per code
        // distinguishes the missing license from the missing restrictions of
        // distribution (AD-15, AD-25).
        Assert.NotEqual(
            ErrorCatalog.PackageLicenseMissingCode,
            ErrorCatalog.PackageDistributionRestrictionsMissingCode);

        Assert.NotEqual(
            ErrorCatalog.PackageLicenseMissingCode,
            ErrorCatalog.GameLicenseMissingCode);

        Assert.NotEqual(
            ErrorCatalog.PackageDistributionRestrictionsMissingCode,
            ErrorCatalog.GameDistributionRestrictionsMissingCode);
    }
}
