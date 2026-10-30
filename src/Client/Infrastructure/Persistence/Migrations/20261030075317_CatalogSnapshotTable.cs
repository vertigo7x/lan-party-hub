using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LANPartyHub.Client.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogSnapshotTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CATALOG_SNAPSHOT",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    catalogVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    synchronizedAt = table.Column<string>(type: "TEXT", nullable: false),
                    payload = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CATALOG_SNAPSHOT", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CATALOG_SNAPSHOT");
        }
    }
}
