using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LANPartyHub.Server.Infrastructure.Persistence;

/// <summary>
/// Design time factory of the catalog database.
/// <para>
/// The tool <c>dotnet ef</c> skips the host of the application, so it cannot find the
/// <see cref="DbContext"/> through the dependency injection of the server. This factory gives
/// the tool the same provider and the same assembly of migrations that the runtime uses, so
/// the generated migration belongs to the model that is really applied at startup.
/// </para>
/// </summary>
public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    /// <inheritdoc />
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CatalogDbContext>();

        optionsBuilder.UseSqlite(
            "Data Source=design-time-catalog.sqlite",
            sqlite => sqlite.MigrationsAssembly(typeof(CatalogDbContext).Assembly.GetName().Name));

        return new CatalogDbContext(optionsBuilder.Options);
    }
}
