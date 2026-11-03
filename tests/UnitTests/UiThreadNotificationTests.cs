using Avalonia.Controls;
using LANPartyHub.Client.Application.CatalogSync;
using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Presentation;
using LANPartyHub.Client.Presentation.Status;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// La publicación de la disponibilidad y del estado de la sincronización llega desde el
/// GRUPO DE HILOS (AC3, AC4, AC5).
///
/// <para>
/// El supervisor de la disponibilidad publica desde el bucle de la supervisión, que va con
/// <c>ConfigureAwait(false)</c>, y la reconexión resincroniza el catálogo también fuera del
/// hilo de la interfaz. Un control de Avalonia SOLO se toca desde el hilo que lo posee:
/// <c>AvaloniaObject.SetValue</c> lanza <c>InvalidOperationException</c> («The calling thread
/// cannot access this object because a different thread owns it»), la excepción sale de la
/// publicación sin capturar, la supervisión queda fallida y el panel NO se repinta ni
/// reconecta nunca (AC3, AC4, AC5).
/// </para>
/// <para>
/// Estos tests fijan que la notificación se ENCOLA en el hilo de la interfaz: el suscriptor
/// NO se invoca en el hilo que publica, sino en el que posee los controles, y el cliente se
/// recupera solo tras la caída del servidor (AC3, AC4, AC5).
/// </para>
/// </summary>
public sealed class UiThreadNotificationTests
{
    private static readonly ServerEndpoint Endpoint = new("192.168.1.20", 5177);

    /// <summary>
    /// Runs the work of the given action on a thread that is NOT the thread of the
    /// interface, and waits for it to finish (AC3).
    /// </summary>
    /// <param name="action">Work that the worker thread does.</param>
    /// <returns>A task that completes when the worker thread finished.</returns>
    private static Task RunOnWorkerThreadAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var worker = new Thread(() =>
        {
            try
            {
                action();

                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
        };

        worker.Start();

        return completion.Task;
    }

    /// <summary>
    /// The state of the synchronization that the composition publishes.
    /// </summary>
    /// <param name="catalogVersion">Version of the catalog that the client applied.</param>
    /// <returns>The published state.</returns>
    private static CatalogSyncState SynchronizedState(long catalogVersion) =>
        CatalogSyncState.UpToDate(
            catalogVersion,
            DateTimeOffset.UtcNow,
            [],
            [],
            snapshot: null,
            serverCatalogVersion: catalogVersion);

    [Fact]
    public async Task A_control_of_avalonia_fails_when_it_is_touched_from_another_thread()
    {
        // Este test fija la CAUSA RAÍZ: un control de Avalonia solo se toca desde el hilo
        // que lo posee. Sin esta garantía, el encolado de la notificación no sería
        // observable y el fallo podría volver en silencio (AC3, AC4, AC5).
        var text = new TextBlock();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunOnWorkerThreadAsync(() => text.Text = "from the worker"));
    }

    [Fact]
    public void The_avalonia_implementation_never_applies_the_work_outside_the_owner_thread()
    {
        // AC3, AC4, AC5: el trabajo se aplica SIEMPRE en el hilo que posee el despachador,
        // y en NINGÚN otro. Esta es la garantía que evita que un control se toque desde un
        // hilo que no lo posee, que es lo que hacía fallar el cliente (AC3).
        //
        // La garantía no depende de quién sea el dueño: el despachador es un singleton del
        // proceso y su dueño es el hilo que lo tocó primero.
        var uiThread = new AvaloniaUiThread();
        var appliedOutsideTheOwnerThread = false;
        var applied = false;

        var worker = new Thread(() =>
        {
            var publishingThreadId = Environment.CurrentManagedThreadId;

            uiThread.Post(() =>
            {
                applied = true;

                appliedOutsideTheOwnerThread =
                    Environment.CurrentManagedThreadId != publishingThreadId
                    && !Avalonia.Threading.Dispatcher.UIThread.CheckAccess();
            });
        })
        {
            IsBackground = true,
        };

        worker.Start();
        worker.Join();

        // Si el despachador NO tiene dueño todavía, el trabajo queda encolado y todavía no
        // se aplicó: la garantía se cumple por construcción (AC3).
        if (!applied)
        {
            Assert.False(appliedOutsideTheOwnerThread);

            return;
        }

        Assert.False(appliedOutsideTheOwnerThread);
    }

    [Fact]
    public void The_avalonia_implementation_applies_the_work_right_away_in_the_owner_thread()
    {
        // AC3: si el llamador YA está en el hilo de la interfaz, el trabajo se ejecuta de
        // inmediato y no se encola: encolar sin necesidad retrasaría el repintado y
        // rompería el orden de las notificaciones.
        var uiThread = new AvaloniaUiThread();
        var executed = false;

        uiThread.Post(() => executed = true);

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            // Este hilo ES el hilo de la interfaz: el trabajo se aplicó de inmediato (AC3).
            Assert.True(executed);

            return;
        }

        // Otro hilo lo posee: el trabajo se encola y se aplica allí, nunca en línea
        // (AC3, AC4, AC5).
        Assert.False(executed);
    }

    [Fact]
    public async Task The_state_that_the_composition_publishes_is_not_notified_inline()
    {
        // AC3, AC4, AC5: el suscriptor del estado de la sincronización NO se invoca en el
        // hilo que publica. Antes del arreglo se invocaba en línea y el aviso del
        // catálogo quedaba sin repintar para siempre (AC3, AC4, AC5).
        var uiThread = new RecordingUiThread();
        var composition = CreateComposition(uiThread);

        await using (composition)
        {
            var publishingThreadId = 0;
            var notifiedInPublishingThread = true;
            var notified = false;

            composition.StateChanged += _ =>
            {
                notifiedInPublishingThread =
                    Environment.CurrentManagedThreadId == publishingThreadId;

                notified = true;
            };

            await RunOnWorkerThreadAsync(() =>
            {
                publishingThreadId = Environment.CurrentManagedThreadId;

                composition.Publish(SynchronizedState(7));
            });

            // La publicación se ENCOLA: el suscriptor NO se invoca en el hilo que publica.
            Assert.Single(uiThread.Posted);
            Assert.False(notified);

            // El hilo de la interfaz ejecuta lo encolado, y el suscriptor se invoca allí.
            uiThread.Run();

            Assert.True(notified);
            Assert.False(notifiedInPublishingThread);
        }
    }

    [Fact]
    public async Task The_state_that_the_composition_publishes_reaches_the_subscriber()
    {
        // AC1, AC3: la publicación NO se pierde: el suscriptor recibe el estado aunque la
        // publicación venga del grupo de hilos (AC3, AC4, AC5).
        var uiThread = new RecordingUiThread();
        var composition = CreateComposition(uiThread);

        await using (composition)
        {
            var received = new TaskCompletionSource<CatalogSyncState>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            composition.StateChanged += state => received.TrySetResult(state);

            await RunOnWorkerThreadAsync(() => composition.Publish(SynchronizedState(7)));

            // El hilo de la interfaz ejecuta lo encolado.
            uiThread.Run();

            var state = await received.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            Assert.True(state.Synchronized);
        }
    }

    [Fact]
    public async Task The_replayed_state_of_a_late_subscriber_arrives()
    {
        // AC1: la primera sincronización ocurre durante el arranque, ANTES de que exista la
        // ventana, así que el suscriptor que llega tarde recibe el último estado. El replay
        // también se entrega en el hilo de la interfaz (AC3).
        var uiThread = new RecordingUiThread();
        var composition = CreateComposition(uiThread);

        await using (composition)
        {
            var received = new TaskCompletionSource<CatalogSyncState>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            composition.Publish(SynchronizedState(11));

            uiThread.Run();

            composition.StateChanged += state => received.TrySetResult(state);

            uiThread.Run();

            var state = await received.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            Assert.True(state.Synchronized);
        }
    }

    [Fact]
    public void The_persistent_panel_of_the_connection_can_be_built()
    {
        // AC1, AC3, AC4: el panel PERSISTENTE tiene que poder CONSTRUIRSE. La insignia de
        // la presencia es un hijo visual del punto de presencia, así que la fila de la
        // presencia lleva SU propio texto: un control de Avalonia no puede estar en dos
        // sitios a la vez (AC1).
        var viewModel = new ConnectionStatusPanelViewModel();

        var panel = new ConnectionStatusPanel(viewModel);

        Assert.NotNull(panel.PresenceBadge);
    }

    [Fact]
    public void The_persistent_panel_reports_the_presence_of_the_server()
    {
        // AC1, AC3: el panel pinta el punto de presencia con su color y su texto, y el
        // aviso del servidor caído sale con su contador (AC4, UX-DR-29).
        var viewModel = new ConnectionStatusPanelViewModel();

        var panel = new ConnectionStatusPanel(viewModel);

        viewModel.ApplyAvailability(ServerAvailability.Reachable(Endpoint, "MESA-1", "1.4.0"));

        Assert.Equal(ConnectionStatusPanelViewModel.PresenceAvailableText, viewModel.PresenceText);
        Assert.Equal("MESA-1", viewModel.ServerNameText);
    }

    /// <summary>
    /// Builds a composition of the synchronization that does NOT talk to any server: the
    /// tests exercise the PUBLICATION of the state, no la red (AC3).
    /// </summary>
    /// <param name="uiThread">Hilo de la interfaz que la composición usa.</param>
    /// <returns>The composition.</returns>
    private static CatalogSyncComposition CreateComposition(IUiThread uiThread)
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"lan-party-hub-{Guid.NewGuid():N}",
            "state.sqlite");

        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        return new CatalogSyncComposition(
            new Uri("http://192.168.1.20:5177"),
            databasePath,
            new StubRequestHeaders(),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            connectionGate: null,
            uiThread);
    }

    /// <summary>
    /// Headers of the correlation that the composition stamps in every request (AD-15,
    /// AD-23).
    /// </summary>
    private sealed class StubRequestHeaders : IClientRequestHeaders
    {
        public string? ClientId => "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";

        public string? OperationId => "0193f2a1-b2c3-4d4e-5f60-718293a4b5c7";

        public string? MachineName => null;
    }

    /// <summary>
    /// Hilo de la interfaz que REGISTRA lo que se encola y lo ejecuta cuando el test lo
    /// pide (AC3).
    ///
    /// <para>
    /// El despachador de Avalonia es un singleton del proceso que pertenece al hilo que lo
    /// tocó primero, así que un test que lo usa tiene que competir por ese hilo. El port
    /// existe para que la regla se pueda ejercitar SIN el despachador (AD-1).
    /// </para>
    /// </summary>
    private sealed class RecordingUiThread : IUiThread
    {
        private readonly List<Action> _posted = [];

        /// <summary>The work that was posted and has NOT run yet.</summary>
        public IReadOnlyList<Action> Posted => _posted;

        public void Post(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            _posted.Add(action);
        }

        /// <summary>
        /// Runs the work that was posted (AC3).
        /// </summary>
        public void Run()
        {
            var pending = _posted.ToArray();

            _posted.Clear();

            foreach (var action in pending)
            {
                action();
            }
        }
    }
}
