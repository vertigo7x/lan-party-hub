using System.Net;
using System.Net.Sockets;
using LANPartyHub.Server.Infrastructure.Networking;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The real host of the event server, started over a data folder (AD-16).
///
/// <para>
/// The integration level starts the real host, with the real Kestrel and the real
/// sockets, because the correlation of the requests and the metrics of the host are only
/// observable with the server in march.
/// </para>
/// </summary>
internal sealed class TestServer : IAsyncDisposable
{
    private const string NoPrivateInterfaceReason =
        "This host has no operational private interface (RFC 1918 or ULA), so the real startup cannot be exercised here.";

    /// <summary>
    /// Number of ports that are tried when the one that was probed is taken between the
    /// probe and the bind.
    /// </summary>
    private const int MaximumStartAttempts = 3;

    private readonly WebApplication _application;
    private readonly bool _ownsDataDirectory;

    private TestServer(
        WebApplication application,
        IPAddress boundAddress,
        int port,
        string dataDirectory,
        bool ownsDataDirectory)
    {
        _application = application;
        _ownsDataDirectory = ownsDataDirectory;

        BoundAddress = boundAddress;
        Port = port;
        DataDirectory = dataDirectory;
    }

    /// <summary>Address of the private interface the host listens on.</summary>
    public IPAddress BoundAddress { get; }

    /// <summary>Port of the host.</summary>
    public int Port { get; }

    /// <summary>Folder of the data of the host.</summary>
    public string DataDirectory { get; }

    /// <summary>The application of the host, for the services that the tests inspect.</summary>
    public WebApplication Application => _application;

    /// <summary>
    /// Starts the real host over a temporary data folder.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The host, ready to serve.</returns>
    public static Task<TestServer> StartAsync(CancellationToken cancellationToken = default) =>
        StartAsync(dataDirectory: null, cancellationToken);

    /// <summary>
    /// Starts the real host over the given data folder.
    /// </summary>
    /// <param name="dataDirectory">
    /// Folder of the data of the host, or <c>null</c> for a temporary one that the host
    /// deletes when it ends.
    /// </param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The host, ready to serve.</returns>
    public static async Task<TestServer> StartAsync(
        string? dataDirectory,
        CancellationToken cancellationToken = default)
    {
        var addresses = new PrivateAddressResolver().GetPrivateAddresses();

        if (addresses.Count == 0)
        {
            Assert.Skip(NoPrivateInterfaceReason);

            throw new InvalidOperationException("The host cannot be started without a private interface.");
        }

        var ownsDataDirectory = dataDirectory is null;
        var folder = dataDirectory ?? Directory.CreateTempSubdirectory("lan-party-hub-server-").FullName;

        for (var attempt = 1; ; attempt++)
        {
            var port = GetFreeTcpPort();
            var application = await Program.BuildServerAsync(["--data-dir", folder, "--http-port", port.ToString()]);

            try
            {
                await application.StartAsync(cancellationToken);

                return new TestServer(application, addresses.First(), port, folder, ownsDataDirectory);
            }
            catch (Exception exception) when (attempt < MaximumStartAttempts && exception is IOException)
            {
                // The port was taken between the probe and the bind: another one is tried.
                await application.DisposeAsync();
            }
            catch
            {
                // The application is disposed so a failed start does not leave the port of
                // the server bound and poison the next test, and the folder that the host
                // created does not stay behind.
                await application.DisposeAsync();

                if (ownsDataDirectory)
                {
                    DeleteFolder(folder);
                }

                throw;
            }
        }
    }

    /// <summary>
    /// Creates a client of the host.
    /// </summary>
    /// <returns>A client whose base address is the address of the host.</returns>
    public HttpClient CreateClient() => new() { BaseAddress = new Uri($"http://{BoundAddress}:{Port}") };

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _application.StopAsync();

        await _application.DisposeAsync();

        if (_ownsDataDirectory)
        {
            DeleteFolder(DataDirectory);
        }
    }

    /// <summary>
    /// Deletes a folder. A file that the operating system still holds does not fail the
    /// test.
    /// </summary>
    private static void DeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // A file that the operating system still holds does not fail the test.
        }
        catch (UnauthorizedAccessException)
        {
            // The folder cannot be deleted now, and it does not fail the test.
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
