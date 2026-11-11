using LANPartyHub.Client.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LANPartyHub.Client.Infrastructure.Persistence;

/// <summary>
/// The database of the client (AD-11, AD-22).
///
/// <para>
/// The client owns exactly one SQLite file, <c>state.sqlite</c>, and its schema is the one of
/// the ERD of the spine: five tables with the exact name and the exact order of their
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

    /// <summary>Rows of the <c>CATALOG_SNAPSHOT</c> table.</summary>
    public DbSet<CatalogSnapshotEntity> CatalogSnapshots => Set<CatalogSnapshotEntity>();

    /// <summary>Rows of the <c>TRUST_ANCHOR</c> table.</summary>
    public DbSet<TrustAnchorEntity> TrustAnchors => Set<TrustAnchorEntity>();

    /// <summary>Rows of the <c>CLIENT_ERROR</c> table.</summary>
    public DbSet<ClientErrorEntity> Errors => Set<ClientErrorEntity>();

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

        modelBuilder.Entity<CatalogSnapshotEntity>(entity =>
        {
            entity.ToTable("CATALOG_SNAPSHOT");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id").IsRequired();
            entity.Property(e => e.CatalogVersion).HasColumnName("catalogVersion").IsRequired();
            entity.Property(e => e.SynchronizedAt).HasColumnName("synchronizedAt").IsRequired();
            entity.Property(e => e.Payload).HasColumnName("payload").IsRequired();
        });

        // AD-27: la tabla del ancla de confianza fijada. Guarda UNA fila con la clave
        // pública, el fingerprint confirmado y el instante de la fijación. La tabla no
        // replica ninguna tabla del servidor: el esquema es del cliente (AD-22).
        modelBuilder.Entity<TrustAnchorEntity>(entity =>
        {
            entity.ToTable("TRUST_ANCHOR");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id").IsRequired();
            entity.Property(e => e.PublicKey).HasColumnName("publicKey").IsRequired();
            entity.Property(e => e.Fingerprint).HasColumnName("fingerprint").IsRequired();
            entity.Property(e => e.FixedAt).HasColumnName("fixedAt").IsRequired();
        });

        // AD-11, AD-15, AD-22, NFR-7: la tabla del REGISTRO de diagnóstico del cliente.
        // La fila guarda el error COMPLETO —código, mensaje, causa y siguiente paso— junto
        // con el <c>clientId</c> y el <c>operationId</c> que lo correlacionan con el log
        // del servidor (AD-23).
        //
        // AD-22: la tabla NO se llama igual que ninguna del servidor: el servidor tiene
        // su propia tabla de errores y el cliente tiene la SUYA, así que las dos bases de
        // datos NO replican ninguna tabla la una de la otra.
        modelBuilder.Entity<ClientErrorEntity>(entity =>
        {
            entity.ToTable("CLIENT_ERROR_REGISTER");
            entity.HasKey(e => e.ErrorId);

            entity.Property(e => e.ErrorId).HasColumnName("errorId").IsRequired();
            entity.Property(e => e.ClientId).HasColumnName("clientId").IsRequired();
            entity.Property(e => e.OperationId).HasColumnName("operationId").IsRequired();
            entity.Property(e => e.OperationType).HasColumnName("operationType").IsRequired();
            entity.Property(e => e.Code).HasColumnName("code").IsRequired();
            entity.Property(e => e.Message).HasColumnName("message").IsRequired();
            entity.Property(e => e.Cause).HasColumnName("cause").IsRequired();
            entity.Property(e => e.NextStep).HasColumnName("nextStep").IsRequired();
            entity.Property(e => e.OccurredAt).HasColumnName("occurredAt").IsRequired();
        });
    }
}
