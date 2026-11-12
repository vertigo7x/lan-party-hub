using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The state microcopy table (AD-10, AD-25). It is exhaustive: every machine state, every
/// operation outcome and every derived view state has exactly one interface text, and no
/// other text exists.
/// </summary>
public sealed class StateDisplayMapTests
{
    [Fact]
    public void Every_download_state_has_a_text()
    {
        foreach (var state in Enum.GetValues<DownloadState>())
        {
            Assert.False(string.IsNullOrWhiteSpace(StateDisplayMap.GetText(state)));
        }
    }

    [Fact]
    public void Every_installation_state_has_a_text()
    {
        foreach (var state in Enum.GetValues<InstallationState>())
        {
            Assert.False(string.IsNullOrWhiteSpace(StateDisplayMap.GetText(state)));
        }
    }

    [Fact]
    public void Every_operation_outcome_has_a_text()
    {
        foreach (var outcome in Enum.GetValues<OperationOutcome>())
        {
            Assert.False(string.IsNullOrWhiteSpace(StateDisplayMap.GetText(outcome)));
        }
    }

    [Fact]
    public void Every_derived_view_state_has_a_text()
    {
        foreach (var viewState in Enum.GetValues<CatalogViewState>())
        {
            Assert.False(string.IsNullOrWhiteSpace(StateDisplayMap.GetText(viewState)));
        }
    }

    [Fact]
    public void Every_connection_state_has_a_text()
    {
        foreach (var connectionState in Enum.GetValues<ConnectionState>())
        {
            Assert.False(string.IsNullOrWhiteSpace(StateDisplayMap.GetText(connectionState)));
        }
    }

    [Fact]
    public void The_unverified_anchor_keeps_the_literal_of_the_architecture_spine()
    {
        Assert.Equal("Ancla verificada", StateDisplayMap.GetText(ConnectionState.AnchorVerified));
        Assert.Equal("Ancla sin verificar", StateDisplayMap.GetText(ConnectionState.AnchorUnverified));
        Assert.Equal("Desconectado", StateDisplayMap.GetText(ConnectionState.Disconnected));
    }

    [Fact]
    public void The_withdrawn_package_outcome_keeps_its_own_text()
    {
        Assert.Equal("Paquete retirado del catálogo", StateDisplayMap.GetText(OperationOutcome.PackageWithdrawn));
    }

    [Fact]
    public void The_outdated_installation_keeps_the_literal_of_the_badge()
    {
        Assert.Equal("instalado con versión desactualizada", StateDisplayMap.GetText(CatalogViewState.InstalledOutdatedVersion));
    }

    [Fact]
    public void The_failure_states_are_projected_into_the_error_badge()
    {
        Assert.Equal("Error", StateDisplayMap.GetText(DownloadState.Failed));
        Assert.Equal("Error", StateDisplayMap.GetText(InstallationState.Failed));
        Assert.Equal("Error", StateDisplayMap.GetText(OperationOutcome.Failed));
    }

    [Fact]
    public void The_download_states_keep_the_microcopy_of_the_experience_spine()
    {
        Assert.Equal("Descargando", StateDisplayMap.GetText(DownloadState.Downloading));
        Assert.Equal("Pausada", StateDisplayMap.GetText(DownloadState.Paused));
        Assert.Equal("Descargado", StateDisplayMap.GetText(DownloadState.Completed));
        Assert.Equal("Instalando", StateDisplayMap.GetText(InstallationState.Installing));
        Assert.Equal("Instalado", StateDisplayMap.GetText(InstallationState.Installed));
    }

    [Fact]
    public void The_outcome_of_a_withdrawn_package_is_not_a_failure()
    {
        Assert.NotEqual(OperationOutcome.PackageWithdrawn, OperationOutcome.Failed);
        Assert.NotEqual(StateDisplayMap.GetText(OperationOutcome.PackageWithdrawn), StateDisplayMap.GetText(OperationOutcome.Failed));
    }

    [Fact]
    public void The_corrupted_package_keeps_the_literal_of_the_experience_spine()
    {
        Assert.Equal(
            "Paquete dañado: el hash no coincide",
            StateDisplayMap.GetText(ErrorCatalog.PackageIntegrityMismatchCode));
    }

    [Fact]
    public void The_untrusted_package_keeps_the_literal_of_the_experience_spine()
    {
        Assert.Equal(
            "Paquete no confiable: no se pudo validar contra la raíz de confianza",
            StateDisplayMap.GetText(ErrorCatalog.ManifestSignatureInvalidCode));
    }

    [Fact]
    public void The_damaged_package_and_the_untrusted_package_are_different_texts()
    {
        var damaged = StateDisplayMap.GetText(ErrorCatalog.PackageIntegrityMismatchCode);
        var untrusted = StateDisplayMap.GetText(ErrorCatalog.ManifestSignatureInvalidCode);

        Assert.NotEqual(damaged, untrusted);
        Assert.Contains("dañado", damaged, StringComparison.Ordinal);
        Assert.Contains("no confiable", untrusted, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_code_falls_back_to_the_generic_text_of_the_catalog()
    {
        var text = StateDisplayMap.GetText("CODE_THAT_THE_CATALOG_DOES_NOT_KNOW");

        Assert.Equal(
            ErrorCatalog.Resolve("CODE_THAT_THE_CATALOG_DOES_NOT_KNOW").Message,
            text);
    }

    [Fact]
    public void An_unknown_code_is_never_the_primary_text()
    {
        var text = StateDisplayMap.GetText("CODE_THAT_THE_CATALOG_DOES_NOT_KNOW");

        Assert.DoesNotContain("CODE_THAT_THE_CATALOG_DOES_NOT_KNOW", text, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public void Every_code_of_the_catalog_has_a_text()
    {
        foreach (var code in ErrorCatalog.Codes)
        {
            Assert.False(string.IsNullOrWhiteSpace(StateDisplayMap.GetText(code)));
        }
    }

    /// <summary>
    /// El texto de una descarga que REANUDA lo compone la tabla: «Reanudando desde 42 % ·
    /// 3,2 GB de 7,6 GB» (UX-DR-6).
    /// </summary>
    [Fact]
    public void The_text_of_a_resumed_download_comes_from_the_table()
    {
        Assert.Equal(
            "Reanudando desde 42 % · 3,2 GB de 7,6 GB",
            StateDisplayMap.GetResumingText("42 % · 3,2 GB de 7,6 GB"));
    }

    /// <summary>
    /// Un tiempo que ha pasado se dice en MINUTOS, nunca en segundos: la velocidad de la
    /// descarga no está validada (UX-DR-6, NFR-2).
    /// </summary>
    [Theory]
    [InlineData(0, "hace 0 min")]
    [InlineData(120, "hace 2 min")]
    [InlineData(45, "hace 1 min")]
    [InlineData(7200, "hace 120 min")]
    public void A_time_that_went_by_is_always_in_minutes(int seconds, string expected)
    {
        Assert.Equal(expected, StateDisplayMap.GetTimeAgoText(TimeSpan.FromSeconds(seconds)));
    }

}
