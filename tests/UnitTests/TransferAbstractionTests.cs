using System.Reflection;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Infrastructure.Networking;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Shape of the abstraction of the transfer of bytes (AD-4, AD-20, AD-24, AD-28).
///
/// <para>
/// The checks observe the real sources and the real assembly of the repository, so they see
/// the structure that is committed and not a copy of it.
/// </para>
/// </summary>
public sealed class TransferAbstractionTests
{
    /// <summary>
    /// Vocabulary that the domain of the MVP never uses: the motor de transferencia es un
    /// camino del servidor al cliente, no un intercambio entre peers (AD-4, AD-28).
    /// </summary>
    private static readonly string[] ForbiddenDomainVocabulary =
    [
        "HttpClient",
        "HttpRequestMessage",
        "HttpResponseMessage",
        "BitTorrent",
        "Bittorrent",
        "MagnetUri",
        "Tracker",
        "Trackers",
        "Swarm",
        "Swarms",
        "Peer",
        "Peers",
        "Socket",
        "Sockets",
        "Fragment",
        "Fragments",
    ];

    /// <summary>
    /// Claves y opciones de la configuración que el MVP no puede exponer: no hay selector
    /// de motor, ni de P2P, ni de tracker (AD-4, AD-28).
    /// </summary>
    private static readonly string[] ForbiddenConfigurationVocabulary =
    [
        "transferEngine",
        "TransferEngine",
        "bittorrent",
        "BitTorrent",
        "tracker",
        "Tracker",
        "p2p",
        "P2P",
        "swarm",
        "Swarm",
        "peer",
        "Peer",
    ];

    [Fact]
    public void The_port_is_declared_in_the_application_layer()
    {
        // AD-1: el puerto vive en la capa de aplicación y el dominio nunca lo nombra.
        Assert.Equal(
            "LANPartyHub.Client.Application.Ports",
            typeof(ITransferEngine).Namespace);
    }

    [Fact]
    public void The_port_does_not_expose_a_transport_mechanism()
    {
        // AD-4: el dominio no menciona HTTP, BitTorrent, trackers ni sockets. El puerto
        // habla de URL y de rango, no de transporte.
        var members = typeof(ITransferEngine).GetMembers();

        Assert.DoesNotContain(members, member =>
            member.Name.Contains("Http", StringComparison.OrdinalIgnoreCase)
            || member.Name.Contains("Torrent", StringComparison.OrdinalIgnoreCase)
            || member.Name.Contains("Peer", StringComparison.OrdinalIgnoreCase)
            || member.Name.Contains("Tracker", StringComparison.OrdinalIgnoreCase)
            || member.Name.Contains("Swarm", StringComparison.OrdinalIgnoreCase)
            || member.Name.Contains("Socket", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void There_is_exactly_one_implementation_of_the_port()
    {
        // AD-4, AD-28: el MVP tiene EXACTAMENTE un camino de descarga.
        var implementations = typeof(ITransferEngine).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                && typeof(ITransferEngine).IsAssignableFrom(type))
            .ToArray();

        Assert.Single(implementations);
        Assert.Equal(typeof(HttpRangeTransferEngine), implementations[0]);
    }

    [Fact]
    public void The_only_implementation_speaks_http_with_ranges()
    {
        Assert.True(typeof(ITransferEngine).IsAssignableFrom(typeof(HttpRangeTransferEngine)));
        Assert.True(typeof(HttpRangeTransferEngine).IsSealed);
    }

    [Fact]
    public void The_domain_of_the_client_does_not_mention_any_transport_mechanism()
    {
        var domainFiles = RepositoryLayout.SourceFiles(Path.Combine("src", "Client", "Domain"));

        Assert.NotEmpty(domainFiles);

        var offenders = new List<string>();

        foreach (var file in domainFiles)
        {
            var content = File.ReadAllText(file);

            foreach (var forbidden in ForbiddenDomainVocabulary)
            {
                if (content.Contains(forbidden, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {forbidden}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_domain_of_the_client_does_not_hold_the_registry_of_the_engines()
    {
        // El registro cerrado vive en la capa de aplicación: el dominio nombra el
        // mecanismo solo cuando el dominio lo necesita, y no lo necesita (AD-4).
        var domainFiles = RepositoryLayout.SourceFiles(Path.Combine("src", "Client", "Domain"));

        Assert.DoesNotContain(
            domainFiles,
            file => Path.GetFileName(file).Contains("TransferEngine", StringComparison.Ordinal));

        var domainTypes = typeof(LANPartyHub.Client.Domain.DomainLayer).Assembly
            .GetTypes()
            .Where(type => type.Namespace is not null
                && type.Namespace.StartsWith("LANPartyHub.Client.Domain", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(domainTypes);

        Assert.DoesNotContain(
            domainTypes,
            type => type.Name.Contains("TransferEngine", StringComparison.Ordinal));
    }

    [Fact]
    public void The_project_of_the_client_references_no_p2p_package()
    {
        // AD-28: el proyecto del MVP no referencia ninguna biblioteca de P2P. La
        // comprobación mira los `PackageReference` REALES y no el texto del fichero, así
        // que un comentario que nombra la biblioteca ausente no la cuenta como referencia.
        var projectFile = Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Client",
            "LANPartyHub.Client.csproj");

        var project = System.Xml.Linq.XDocument.Load(projectFile);

        var packages = project
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .ToArray();

        Assert.NotEmpty(packages);

        foreach (var package in packages)
        {
            foreach (var forbidden in new[] { "MonoTorrent", "BitTorrent", "P2P", "Swarm", "Tracker" })
            {
                Assert.DoesNotContain(forbidden, package, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void The_configuration_of_the_client_exposes_no_engine_selector()
    {
        // AD-4, AC2: la configuración NO expone ningún selector de motor de transferencia:
        // el valor por defecto es HTTP y no hay valor alternativo.
        var configurationFile = Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Client",
            "Host",
            "ClientConfiguration.cs");

        var content = File.ReadAllText(configurationFile);

        foreach (var forbidden in ForbiddenConfigurationVocabulary)
        {
            Assert.DoesNotContain(forbidden, content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_command_line_of_the_client_accepts_no_engine_selector()
    {
        var commandLineFile = Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Client",
            "Host",
            "ClientCommandLine.cs");

        var content = File.ReadAllText(commandLineFile);

        foreach (var forbidden in ForbiddenConfigurationVocabulary)
        {
            Assert.DoesNotContain(forbidden, content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_client_accepts_only_its_three_options()
    {
        var options = LANPartyHub.Client.Host.ClientCommandLine.Options;

        Assert.Equal(
            new[] { "--server-address", "--install-directory", "--theme" },
            options);
    }    [Fact]
    public void The_server_does_not_expose_any_endpoint_of_p2p_or_of_a_tracker()
    {
        // AD-28: la tabla REST del MVP no expone ningún endpoint de P2P ni de tracker.
        var serverFiles = RepositoryLayout.SourceFiles(Path.Combine("src", "Server", "Host"));

        Assert.NotEmpty(serverFiles);

        var offenders = new List<string>();

        foreach (var file in serverFiles)
        {
            var content = File.ReadAllText(file);

            foreach (var forbidden in new[] { "announce", "torrent", "peer", "swarm", "tracker" })
            {
                if (content.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {forbidden}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_client_project_does_not_expose_the_transfer_engine_outside_the_infrastructure()
    {
        // AD-1: el dominio y la capa de aplicación conocen el PORT, nunca el adaptador.
        // El identificador persistido vive en la capa de aplicación porque AC4 exige el
        // valor EXACTO y el dominio no puede nombrarlo, así que la capa de aplicación lo
        // declara como un VALOR y no como una referencia al tipo de la infraestructura.
        var applicationFiles = RepositoryLayout.SourceFiles(Path.Combine("src", "Client", "Application"));

        Assert.NotEmpty(applicationFiles);

        var offenders = applicationFiles
            .Where(file => File.ReadAllText(file)
                .Contains("LANPartyHub.Client.Infrastructure", StringComparison.Ordinal))
            .Select(file => Path.GetFileName(file))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_only_implementation_lives_in_the_infrastructure_layer()
    {
        var implementations = typeof(ITransferEngine).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                && typeof(ITransferEngine).IsAssignableFrom(type))
            .ToArray();

        var implementation = Assert.Single(implementations);

        Assert.Equal(
            "LANPartyHub.Client.Infrastructure.Networking",
            implementation.Namespace);
    }

    [Fact]
    public void The_assembly_of_the_client_does_not_reference_a_p2p_library()
    {
        var referencedAssemblies = typeof(ITransferEngine).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(
            referencedAssemblies,
            name => name.Contains("Torrent", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Peer", StringComparison.OrdinalIgnoreCase));
    }
}
