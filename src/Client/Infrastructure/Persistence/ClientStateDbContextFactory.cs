using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LANPartyHub.Client.Infrastructure.Persistence;

/// <summary>
/// Design time factory of the database of the client.
/// <para>
/// The tool <c>dotnet ef</c> skips the host of the application, so it cannot find the
/// <see cref="DbContext"/> through the composition of the client. This factory gives the tool
/// the same provider and the same assembly of migrations that the runtime uses, so the
/// generated migration belongs to the model that is really applied at startup.
/// </para>
/// </summary>
public sealed class ClientStateDbContextFactory : IDesignTimeDbContextFactory<ClientStateDbContext>
{
    /// <inheritdoc />
    public ClientStateDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ClientStateDbContext>();

        optionsBuilder.UseSqlite(
            "Data Source=design-time-state.sqlite",
            sqlite => sqlite.MigrationsAssembly(typeof(ClientStateDbContext).Assembly.GetName().Name));

        return new ClientStateDbContext(optionsBuilder.Options);
    }
}
