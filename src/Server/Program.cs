// Composition root of the server (AD-1, AD-5).
//
// Story 1.1 only fixes the shape of the solution: the host is created and started, but the
// REST surface, the SignalR hub, the data folder and the observability endpoints arrive
// with Story 1.2 (arranque del servidor) and the following stories.
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.Run();
