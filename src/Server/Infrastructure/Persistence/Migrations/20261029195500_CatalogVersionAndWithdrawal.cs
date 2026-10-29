using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LANPartyHub.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogVersionAndWithdrawal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "withdrawnAt",
                table: "PACKAGE_DESCRIPTOR",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CATALOG_VERSION",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    catalogVersion = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CATALOG_VERSION", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CATALOG_VERSION");

            migrationBuilder.DropColumn(
                name: "withdrawnAt",
                table: "PACKAGE_DESCRIPTOR");
        }
    }
}
