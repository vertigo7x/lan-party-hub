using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LANPartyHub.Client.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialClientStateSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CLIENT_IDENTITY",
                columns: table => new
                {
                    clientId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CLIENT_IDENTITY", x => x.clientId);
                });

            migrationBuilder.CreateTable(
                name: "CLIENT_INSTALLATION",
                columns: table => new
                {
                    gameId = table.Column<string>(type: "TEXT", nullable: false),
                    installedVersion = table.Column<string>(type: "TEXT", nullable: false),
                    validationState = table.Column<string>(type: "TEXT", nullable: false),
                    installPath = table.Column<string>(type: "TEXT", nullable: false),
                    installDirectoryId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CLIENT_INSTALLATION", x => x.gameId);
                });

            migrationBuilder.CreateTable(
                name: "DOWNLOAD_OPERATION",
                columns: table => new
                {
                    operationId = table.Column<string>(type: "TEXT", nullable: false),
                    packageId = table.Column<string>(type: "TEXT", nullable: false),
                    state = table.Column<string>(type: "TEXT", nullable: false),
                    transferEngine = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DOWNLOAD_OPERATION", x => x.operationId);
                });

            migrationBuilder.CreateTable(
                name: "DOWNLOAD_PROGRESS",
                columns: table => new
                {
                    operationId = table.Column<string>(type: "TEXT", nullable: false),
                    bytesPersisted = table.Column<long>(type: "INTEGER", nullable: false),
                    totalBytes = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DOWNLOAD_PROGRESS", x => x.operationId);
                    table.ForeignKey(
                        name: "FK_DOWNLOAD_PROGRESS_DOWNLOAD_OPERATION_operationId",
                        column: x => x.operationId,
                        principalTable: "DOWNLOAD_OPERATION",
                        principalColumn: "operationId",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CLIENT_IDENTITY");

            migrationBuilder.DropTable(
                name: "CLIENT_INSTALLATION");

            migrationBuilder.DropTable(
                name: "DOWNLOAD_PROGRESS");

            migrationBuilder.DropTable(
                name: "DOWNLOAD_OPERATION");
        }
    }
}
