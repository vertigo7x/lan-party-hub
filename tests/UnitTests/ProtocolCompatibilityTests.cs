using LANPartyHub.Server.Domain.Clients;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Rule of the compatibility of the versions of the clients (AD-6, FR-4).
///
/// <para>
/// La matriz de compatibilidad vive SOLO en el servidor: el cliente declara su versión y
/// el servidor decide. El rango es <c>[mínimo, actual]</c>, así que un cliente demasiado
/// viejo y un cliente demasiado nuevo son los dos incompatibles (AD-6).
/// </para>
/// </summary>
public sealed class ProtocolCompatibilityTests
{
    [Fact]
    public void A_client_with_the_current_protocol_version_is_compatible()
    {
        Assert.True(ProtocolCompatibility.IsCompatible(
            clientProtocolVersion: 3,
            minimumProtocolVersion: 1,
            currentProtocolVersion: 3));
    }

    [Fact]
    public void A_client_with_the_minimum_protocol_version_is_compatible()
    {
        // El mínimo del rango es INCLUSIVO: el cliente que habla la versión más vieja que
        // el servidor admite todavía es compatible (AD-6).
        Assert.True(ProtocolCompatibility.IsCompatible(
            clientProtocolVersion: 1,
            minimumProtocolVersion: 1,
            currentProtocolVersion: 3));
    }

    [Fact]
    public void A_client_older_than_the_minimum_is_not_compatible()
    {
        Assert.False(ProtocolCompatibility.IsCompatible(
            clientProtocolVersion: 0,
            minimumProtocolVersion: 1,
            currentProtocolVersion: 3));
    }

    [Fact]
    public void A_client_newer_than_the_current_version_is_not_compatible()
    {
        // El protocolo del futuro NO se adivina: un cliente que habla una versión que el
        // servidor no conoce es incompatible (AD-6).
        Assert.False(ProtocolCompatibility.IsCompatible(
            clientProtocolVersion: 4,
            minimumProtocolVersion: 1,
            currentProtocolVersion: 3));
    }

    [Fact]
    public void A_client_inside_the_range_is_compatible()
    {
        Assert.True(ProtocolCompatibility.IsCompatible(
            clientProtocolVersion: 2,
            minimumProtocolVersion: 1,
            currentProtocolVersion: 3));
    }

    [Fact]
    public void A_range_with_a_single_version_accepts_only_that_version()
    {
        Assert.True(ProtocolCompatibility.IsCompatible(
            clientProtocolVersion: 2,
            minimumProtocolVersion: 2,
            currentProtocolVersion: 2));

        Assert.False(ProtocolCompatibility.IsCompatible(
            clientProtocolVersion: 1,
            minimumProtocolVersion: 2,
            currentProtocolVersion: 2));

        Assert.False(ProtocolCompatibility.IsCompatible(
            clientProtocolVersion: 3,
            minimumProtocolVersion: 2,
            currentProtocolVersion: 2));
    }

    [Fact]
    public void A_client_with_a_negative_protocol_version_is_not_compatible()
    {
        Assert.False(ProtocolCompatibility.IsCompatible(
            clientProtocolVersion: -1,
            minimumProtocolVersion: 1,
            currentProtocolVersion: 3));
    }

    [Fact]
    public void The_current_protocol_version_of_the_contract_is_the_one_that_the_server_speaks()
    {
        // AD-6, AD-18: el servidor y el cliente toman la versión del protocolo de la
        // MISMA constante del contrato compartido, así que no pueden discrepar.
        Assert.Equal(
            LANPartyHub.Shared.Contracts.VersionInfo.CurrentProtocolVersion,
            LANPartyHub.Server.Host.ServerVersion.ProtocolVersion);
    }
}
