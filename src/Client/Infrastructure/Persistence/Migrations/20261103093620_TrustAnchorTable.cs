using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LANPartyHub.Client.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrustAnchorTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TRUST_ANCHOR",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    publicKey = table.Column<string>(type: "TEXT", nullable: false),
                    fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    fixedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRUST_ANCHOR", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TRUST_ANCHOR");
        }
    }
}
