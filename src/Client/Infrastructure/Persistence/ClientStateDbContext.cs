using LANPartyHub.Client.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LANPartyHub.Client.Infrastructure.Persistence;

/// <summary>
/// The database of the client (AD-11, AD-22).
///
/// <para>
/// The client owns exactly one SQLite file, <c>state.sqlite</c>, and its schema is the one of
/// the ERD of the spine: four tables with the exact name and the exact order of their
/// columns. The mapping is explicit for every entity, so the schema is not the result of a
/// convention that could change with the version of EF Core.
/// </para>
/// <para>
/// The schema belongs to the client and it never replicates a table of the server: the
/// catalogue of the server and the view of the organization stay in <c>catalog.sqlite</c>
/// (AD-22).
/// </para>
/// </summary>
public sealed class ClientStateDbContext : DbContext
{
    /// <summary>Creates the context with the options of the composition.</summary>
    /// <param name="options">Options of the context.</param>
    public ClientStateDbContext(DbContextOptions<ClientStateDbContext> options)
        : base(options)
    {
    }

    /// <summary>Rows of the <c>CLIENT_IDENTITY</c> table.</summary>
    public DbSet<ClientIdentityEntity> Identities => Set<ClientIdentityEntity>();

    /// <summary>Rows of the <c>CLIENT_INSTALLATION</c> table.</summary>
    public DbSet<ClientInstallationEntity> Installations => Set<ClientInstallationEntity>();

    /// <summary>Rows of the <c>DOWNLOAD_OPERATION</c> table.</summary>
    public DbSet<DownloadOperationEntity> DownloadOperations => Set<DownloadOperationEntity>();

    /// <summary>Rows of the <c>DOWNLOAD_PROGRESS</c> table.</summary>
    public DbSet<DownloadProgressEntity> DownloadProgresses => Set<DownloadProgressEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ClientIdentityEntity>(entity =>
        {
            entity.ToTable("CLIENT_IDENTITY");
            entity.HasKey(e => e.ClientId);

            entity.Property(e => e.ClientId).HasColumnName("clientId").IsRequired();
        });

        modelBuilder.Entity<ClientInstallationEntity>(entity =>
        {
            entity.ToTable("CLIENT_INSTALLATION");
            entity.HasKey(e => e.GameId);

            entity.Property(e => e.GameId).HasColumnName("gameId").IsRequired();
            entity.Property(e => e.InstalledVersion).HasColumnName("installedVersion").IsRequired();
            entity.Property(e => e.ValidationState).HasColumnName("validationState").IsRequired();
            entity.Property(e => e.InstallPath).HasColumnName("installPath").IsRequired();
            entity.Property(e => e.InstallDirectoryId).HasColumnName("installDirectoryId").IsRequired();
        });

        modelBuilder.Entity<DownloadOperationEntity>(entity =>
        {
            entity.ToTable("DOWNLOAD_OPERATION");
            entity.HasKey(e => e.OperationId);

            entity.Property(e => e.OperationId).HasColumnName("operationId").IsRequired();
            entity.Property(e => e.PackageId).HasColumnName("packageId").IsRequired();
            entity.Property(e => e.State).HasColumnName("state").IsRequired();
            entity.Property(e => e.TransferEngine).HasColumnName("transferEngine").IsRequired();
        });

        modelBuilder.Entity<DownloadProgressEntity>(entity =>
        {
            entity.ToTable("DOWNLOAD_PROGRESS");
            entity.HasKey(e => e.OperationId);

            entity.Property(e => e.OperationId).HasColumnName("operationId").IsRequired();
            entity.Property(e => e.BytesPersisted).HasColumnName("bytesPersisted").IsRequired();
            entity.Property(e => e.TotalBytes).HasColumnName("totalBytes").IsRequired();

            // The progress belongs to its operation, so it disappears with it.
            entity.HasOne<DownloadOperationEntity>()
                .WithMany()
                .HasForeignKey(e => e.OperationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
