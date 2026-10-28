using System.Linq.Expressions;
using System.Text.Json;
using LANPartyHub.Server.Infrastructure.Persistence.Entities;
using LANPartyHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LANPartyHub.Server.Infrastructure.Persistence;

/// <summary>
/// The database of the event server (AD-11, AD-22).
///
/// The server owns exactly one SQLite file, <c>catalog.sqlite</c>, and its schema is the one
/// of the ERD of the spine: seven tables with the exact name and the exact order of their
/// columns. The mapping is explicit for every entity, so the schema is not the result of a
/// convention that could change with the version of EF Core.
///
/// The schema is materialized once, by the versioned migrations of this assembly. The
/// uniqueness of the <c>machineName</c> by canonical form (AD-30) is hardened by the
/// migration of the story that owns it, so the rule is not duplicated here (AD-1).
/// </summary>
public sealed class CatalogDbContext : DbContext
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ValueConverter<string[], string> StringArrayToJsonConverter = new(
        value => JsonSerializer.Serialize(value, JsonOptions),
        value => JsonSerializer.Deserialize<string[]>(value, JsonOptions) ?? Array.Empty<string>());

    private static readonly ValueComparer<string[]> StringArrayComparer = new(
        (left, right) => left != null && right != null && left.SequenceEqual(right),
        array => array.Aggregate(0, (hash, value) => HashCode.Combine(hash, value == null ? 0 : value.GetHashCode())),
        array => array.ToArray());

    private static readonly ValueConverter<InstallationManifest.LaunchSettings, string> LaunchSettingsToJsonConverter = new(
        value => JsonSerializer.Serialize(value, JsonOptions),
        value => JsonSerializer.Deserialize<InstallationManifest.LaunchSettings>(value, JsonOptions)
            ?? new InstallationManifest.LaunchSettings(string.Empty, Array.Empty<string>(), string.Empty));

    /// <summary>Creates the context with the options of the host.</summary>
    /// <param name="options">Options of the context.</param>
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options)
        : base(options)
    {
    }

    /// <summary>Rows of the <c>GAME</c> table.</summary>
    public DbSet<GameEntity> Games => Set<GameEntity>();

    /// <summary>Rows of the <c>PACKAGE_DESCRIPTOR</c> table.</summary>
    public DbSet<PackageDescriptorEntity> PackageDescriptors => Set<PackageDescriptorEntity>();

    /// <summary>Rows of the <c>INSTALLATION_MANIFEST</c> table.</summary>
    public DbSet<InstallationManifestEntity> InstallationManifests => Set<InstallationManifestEntity>();

    /// <summary>Rows of the <c>CLIENT_REGISTRATION</c> table.</summary>
    public DbSet<ClientRegistrationEntity> ClientRegistrations => Set<ClientRegistrationEntity>();

    /// <summary>Rows of the <c>CLIENT_INSTALLATION_VIEW</c> table.</summary>
    public DbSet<ClientInstallationView> ClientInstallationViews => Set<ClientInstallationView>();

    /// <summary>Rows of the <c>DOWNLOAD_INTENT</c> table.</summary>
    public DbSet<DownloadIntentEntity> DownloadIntents => Set<DownloadIntentEntity>();

    /// <summary>Rows of the <c>CLIENT_ERROR</c> table.</summary>
    public DbSet<ClientErrorEntity> ClientErrors => Set<ClientErrorEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<GameEntity>(entity =>
        {
            entity.ToTable("GAME");
            entity.HasKey(e => e.GameId);

            entity.Property(e => e.GameId).HasColumnName("gameId").IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").IsRequired();
            ConfigureStringArray(entity, e => e.Categories, "categories");
            ConfigureStringArray(entity, e => e.Tags, "tags");
            entity.Property(e => e.CoverImage).HasColumnName("coverImage").IsRequired();
            ConfigureStringArray(entity, e => e.Images, "images");
            entity.Property(e => e.Version).HasColumnName("version").IsRequired();
            entity.Property(e => e.SizeBytes).HasColumnName("sizeBytes").IsRequired();
            entity.Property(e => e.MinPlayers).HasColumnName("minPlayers").IsRequired();
            entity.Property(e => e.MaxPlayers).HasColumnName("maxPlayers").IsRequired();
            entity.Property(e => e.OperatingSystem).HasColumnName("operatingSystem").IsRequired();
            entity.Property(e => e.DistributionType).HasColumnName("distributionType").IsRequired();
            entity.Property(e => e.InstallationType).HasColumnName("installationType").IsRequired();
            entity.Property(e => e.LaunchConfiguration).HasColumnName("launchConfiguration").HasConversion(LaunchSettingsToJsonConverter).IsRequired();
            ConfigureStringArray(entity, e => e.Dependencies, "dependencies");
            entity.Property(e => e.License).HasColumnName("license").IsRequired();
            entity.Property(e => e.DistributionRestrictions).HasColumnName("distributionRestrictions").IsRequired();
        });

        modelBuilder.Entity<PackageDescriptorEntity>(entity =>
        {
            entity.ToTable("PACKAGE_DESCRIPTOR");
            entity.HasKey(e => e.PackageId);

            entity.Property(e => e.PackageId).HasColumnName("packageId").IsRequired();
            entity.Property(e => e.GameId).HasColumnName("gameId").IsRequired();
            entity.Property(e => e.Version).HasColumnName("version").IsRequired();
            entity.Property(e => e.SizeBytes).HasColumnName("sizeBytes").IsRequired();
            entity.Property(e => e.ChunkSizeBytes).HasColumnName("chunkSizeBytes");
            entity.Property(e => e.Sha256).HasColumnName("sha256").IsRequired();
            entity.Property(e => e.ManifestRef).HasColumnName("manifestRef").IsRequired();

            entity.HasOne<GameEntity>()
                .WithMany()
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InstallationManifestEntity>(entity =>
        {
            entity.ToTable("INSTALLATION_MANIFEST");
            entity.HasKey(e => e.ManifestId);

            entity.Property(e => e.ManifestId).HasColumnName("manifestId").IsRequired();
            entity.Property(e => e.PackageId).HasColumnName("packageId").IsRequired();
            entity.Property(e => e.SchemaVersion).HasColumnName("schemaVersion").IsRequired();
            entity.Property(e => e.Signature).HasColumnName("signature").IsRequired();
            entity.Property(e => e.KeyId).HasColumnName("keyId").IsRequired();
            entity.Property(e => e.CanonicalManifest).HasColumnName("canonicalManifest").IsRequired();

            entity.HasOne<PackageDescriptorEntity>()
                .WithMany()
                .HasForeignKey(e => e.PackageId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ClientRegistrationEntity>(entity =>
        {
            entity.ToTable("CLIENT_REGISTRATION");
            entity.HasKey(e => e.ClientId);

            entity.Property(e => e.ClientId).HasColumnName("clientId").IsRequired();
            entity.Property(e => e.MachineName).HasColumnName("machineName").IsRequired();
            entity.Property(e => e.ClientVersion).HasColumnName("clientVersion").IsRequired();
            entity.Property(e => e.ProtocolVersion).HasColumnName("protocolVersion").IsRequired();
            entity.Property(e => e.ConnectionState).HasColumnName("connectionState").IsRequired();
            entity.Property(e => e.LastSeenAt).HasColumnName("lastSeenAt").IsRequired();
        });

        modelBuilder.Entity<ClientInstallationView>(entity =>
        {
            entity.ToTable("CLIENT_INSTALLATION_VIEW");
            entity.HasKey(e => new { e.ClientId, e.GameId });

            entity.Property(e => e.ClientId).HasColumnName("clientId").IsRequired();
            entity.Property(e => e.GameId).HasColumnName("gameId").IsRequired();
            entity.Property(e => e.InstalledVersion).HasColumnName("installedVersion").IsRequired();
            entity.Property(e => e.ValidationStateText).HasColumnName("validationStateText").IsRequired();

            entity.HasOne<ClientRegistrationEntity>()
                .WithMany()
                .HasForeignKey(e => e.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DownloadIntentEntity>(entity =>
        {
            entity.ToTable("DOWNLOAD_INTENT");
            entity.HasKey(e => e.OperationId);

            entity.Property(e => e.OperationId).HasColumnName("operationId").IsRequired();
            entity.Property(e => e.ClientId).HasColumnName("clientId").IsRequired();
            entity.Property(e => e.PackageId).HasColumnName("packageId").IsRequired();
            entity.Property(e => e.State).HasColumnName("state").IsRequired();
            entity.Property(e => e.Outcome).HasColumnName("outcome").IsRequired();

            entity.HasOne<ClientRegistrationEntity>()
                .WithMany()
                .HasForeignKey(e => e.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<PackageDescriptorEntity>()
                .WithMany()
                .HasForeignKey(e => e.PackageId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ClientErrorEntity>(entity =>
        {
            entity.ToTable("CLIENT_ERROR");
            entity.HasKey(e => e.ErrorId);

            entity.Property(e => e.ErrorId).HasColumnName("errorId").IsRequired();
            entity.Property(e => e.ClientId).HasColumnName("clientId").IsRequired();
            entity.Property(e => e.OperationId).HasColumnName("operationId").IsRequired();
            entity.Property(e => e.Code).HasColumnName("code").IsRequired();
            entity.Property(e => e.Message).HasColumnName("message").IsRequired();
            entity.Property(e => e.Cause).HasColumnName("cause").IsRequired();
            entity.Property(e => e.NextStep).HasColumnName("nextStep").IsRequired();
            entity.Property(e => e.OccurredAt).HasColumnName("occurredAt").IsRequired();

            entity.HasOne<ClientRegistrationEntity>()
                .WithMany()
                .HasForeignKey(e => e.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureStringArray(
        EntityTypeBuilder<GameEntity> entity,
        Expression<Func<GameEntity, string[]>> property,
        string columnName)
    {
        var builder = entity.Property(property);

        builder.HasColumnName(columnName).HasConversion(StringArrayToJsonConverter).IsRequired();
        builder.Metadata.SetValueComparer(StringArrayComparer);
    }
}
