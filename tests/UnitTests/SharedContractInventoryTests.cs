using System.Reflection;
using LANPartyHub.Client.Domain.StateMachines;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Inventory of the contracts declared in <c>Shared</c> (AD-18, AD-33, AD-34, AR-3).
/// The list is exhaustive: the required contracts exist in <c>Shared</c>, <c>Shared</c>
/// declares nothing else, and none of them is declared again in the server or the client.
/// </summary>
public sealed class SharedContractInventoryTests
{
    private static readonly string[] RequiredContractTypes =
    [
        "ApiError",
        "ErrorCatalog",
        "DownloadState",
        "InstallationState",
        "OperationOutcome",
        "StateDisplayMap",
        "CatalogViewState",
        "CatalogVersionOrder",
        "ClientStateChanged",
        "DownloadProgressUpdated",
        "InstallationProgressUpdated",
        "ClientErrorReported",
        "CatalogChanged",
        "CatalogSnapshot",
        "ClientRegistrationView",
        "TrustAnchor",
        "PackageDescriptor",
        "GameDescriptor",
        "InstallationManifest",
        "VersionInfo",
        "ServerEndpoint",
        "ConnectionState",
    ];

    private static readonly string[] ForbiddenSharedAssemblies =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Data.Sqlite",
        "Avalonia",
        "Microsoft.AspNetCore",
        "System.Net.Http",
    ];

    private static Assembly SharedAssembly => typeof(ApiError).Assembly;

    private static Assembly ServerAssembly => typeof(LANPartyHub.Server.Domain.DomainLayer).Assembly;

    private static Assembly ClientAssembly => typeof(DownloadStateMachine).Assembly;

    private static IReadOnlyList<string> PublicTopLevelTypes(Assembly assembly) => assembly
        .GetExportedTypes()
        .Where(type => !type.IsNested)
        .Select(type => type.Name)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

    private static IReadOnlyList<string> DeclaredTopLevelTypes(Assembly assembly) => assembly
        .GetTypes()
        .Where(type => !type.IsNested)
        .Select(type => type.Name)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

    [Theory]
    [InlineData("ApiError")]
    [InlineData("ErrorCatalog")]
    [InlineData("DownloadState")]
    [InlineData("InstallationState")]
    [InlineData("OperationOutcome")]
    [InlineData("StateDisplayMap")]
    [InlineData("CatalogViewState")]
    [InlineData("CatalogVersionOrder")]
    [InlineData("ClientStateChanged")]
    [InlineData("DownloadProgressUpdated")]
    [InlineData("InstallationProgressUpdated")]
    [InlineData("ClientErrorReported")]
    [InlineData("CatalogChanged")]
    [InlineData("CatalogSnapshot")]
    [InlineData("ClientRegistrationView")]
    [InlineData("TrustAnchor")]
    [InlineData("PackageDescriptor")]
    [InlineData("GameDescriptor")]
    [InlineData("InstallationManifest")]
    [InlineData("VersionInfo")]
    [InlineData("ServerEndpoint")]
    [InlineData("ConnectionState")]
    public void The_required_contract_is_declared_in_shared(string contractTypeName)
    {
        Assert.Contains(contractTypeName, PublicTopLevelTypes(SharedAssembly));
    }

    [Fact]
    public void Shared_declares_exactly_the_required_contracts_and_nothing_else()
    {
        var declared = PublicTopLevelTypes(SharedAssembly);

        Assert.Equal(RequiredContractTypes.OrderBy(name => name, StringComparer.Ordinal), declared);
    }

    [Fact]
    public void Shared_declares_twenty_two_contracts()
    {
        Assert.Equal(22, RequiredContractTypes.Length);
        Assert.Equal(22, PublicTopLevelTypes(SharedAssembly).Count);
    }

    [Fact]
    public void No_contract_of_shared_is_duplicated_in_the_server()
    {
        var serverTypes = DeclaredTopLevelTypes(ServerAssembly);

        foreach (var contractTypeName in RequiredContractTypes)
        {
            Assert.DoesNotContain(contractTypeName, serverTypes);
        }
    }

    [Fact]
    public void No_contract_of_shared_is_duplicated_in_the_client()
    {
        var clientTypes = DeclaredTopLevelTypes(ClientAssembly);

        foreach (var contractTypeName in RequiredContractTypes)
        {
            Assert.DoesNotContain(contractTypeName, clientTypes);
        }
    }

    [Fact]
    public void The_state_machines_are_declared_once_in_the_executor()
    {
        var clientTypes = DeclaredTopLevelTypes(ClientAssembly);
        var serverTypes = DeclaredTopLevelTypes(ServerAssembly);

        Assert.Contains("DownloadStateMachine", clientTypes);
        Assert.Contains("InstallationStateMachine", clientTypes);
        Assert.DoesNotContain("DownloadStateMachine", serverTypes);
        Assert.DoesNotContain("InstallationStateMachine", serverTypes);
    }

    [Fact]
    public void The_shared_contracts_do_not_reference_a_framework()
    {
        var referencedAssemblies = SharedAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        foreach (var forbidden in ForbiddenSharedAssemblies)
        {
            Assert.DoesNotContain(forbidden, referencedAssemblies);
        }
    }

    [Fact]
    public void The_free_text_state_of_an_active_operation_is_resolvable()
    {
        foreach (var state in Enum.GetValues<DownloadState>())
        {
            var parsed = Enum.Parse<DownloadState>(state.ToString());
            var text = StateDisplayMap.GetText(parsed);

            Assert.Equal(state, parsed);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }

        foreach (var state in Enum.GetValues<InstallationState>())
        {
            var parsed = Enum.Parse<InstallationState>(state.ToString());
            var text = StateDisplayMap.GetText(parsed);

            Assert.Equal(state, parsed);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }
}
