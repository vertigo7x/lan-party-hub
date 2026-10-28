using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LANPartyHub.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalogSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CLIENT_REGISTRATION",
                columns: table => new
                {
                    clientId = table.Column<string>(type: "TEXT", nullable: false),
                    machineName = table.Column<string>(type: "TEXT", nullable: false),
                    clientVersion = table.Column<string>(type: "TEXT", nullable: false),
                    protocolVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    connectionState = table.Column<string>(type: "TEXT", nullable: false),
                    lastSeenAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CLIENT_REGISTRATION", x => x.clientId);
                });

            migrationBuilder.CreateTable(
                name: "GAME",
                columns: table => new
                {
                    gameId = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    categories = table.Column<string>(type: "TEXT", nullable: false),
                    tags = table.Column<string>(type: "TEXT", nullable: false),
                    coverImage = table.Column<string>(type: "TEXT", nullable: false),
                    images = table.Column<string>(type: "TEXT", nullable: false),
                    version = table.Column<string>(type: "TEXT", nullable: false),
                    sizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    minPlayers = table.Column<int>(type: "INTEGER", nullable: false),
                    maxPlayers = table.Column<int>(type: "INTEGER", nullable: false),
                    operatingSystem = table.Column<string>(type: "TEXT", nullable: false),
                    distributionType = table.Column<string>(type: "TEXT", nullable: false),
                    installationType = table.Column<string>(type: "TEXT", nullable: false),
                    launchConfiguration = table.Column<string>(type: "TEXT", nullable: false),
                    dependencies = table.Column<string>(type: "TEXT", nullable: false),
                    license = table.Column<string>(type: "TEXT", nullable: false),
                    distributionRestrictions = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GAME", x => x.gameId);
                });

            migrationBuilder.CreateTable(
                name: "CLIENT_ERROR",
                columns: table => new
                {
                    errorId = table.Column<string>(type: "TEXT", nullable: false),
                    clientId = table.Column<string>(type: "TEXT", nullable: false),
                    operationId = table.Column<string>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    message = table.Column<string>(type: "TEXT", nullable: false),
                    cause = table.Column<string>(type: "TEXT", nullable: false),
                    nextStep = table.Column<string>(type: "TEXT", nullable: false),
                    occurredAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CLIENT_ERROR", x => x.errorId);
                    table.ForeignKey(
                        name: "FK_CLIENT_ERROR_CLIENT_REGISTRATION_clientId",
                        column: x => x.clientId,
                        principalTable: "CLIENT_REGISTRATION",
                        principalColumn: "clientId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CLIENT_INSTALLATION_VIEW",
                columns: table => new
                {
                    clientId = table.Column<string>(type: "TEXT", nullable: false),
                    gameId = table.Column<string>(type: "TEXT", nullable: false),
                    installedVersion = table.Column<string>(type: "TEXT", nullable: false),
                    validationStateText = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CLIENT_INSTALLATION_VIEW", x => new { x.clientId, x.gameId });
                    table.ForeignKey(
                        name: "FK_CLIENT_INSTALLATION_VIEW_CLIENT_REGISTRATION_clientId",
                        column: x => x.clientId,
                        principalTable: "CLIENT_REGISTRATION",
                        principalColumn: "clientId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PACKAGE_DESCRIPTOR",
                columns: table => new
                {
                    packageId = table.Column<string>(type: "TEXT", nullable: false),
                    gameId = table.Column<string>(type: "TEXT", nullable: false),
                    version = table.Column<string>(type: "TEXT", nullable: false),
                    sizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    chunkSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    manifestRef = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PACKAGE_DESCRIPTOR", x => x.packageId);
                    table.ForeignKey(
                        name: "FK_PACKAGE_DESCRIPTOR_GAME_gameId",
                        column: x => x.gameId,
                        principalTable: "GAME",
                        principalColumn: "gameId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DOWNLOAD_INTENT",
                columns: table => new
                {
                    operationId = table.Column<string>(type: "TEXT", nullable: false),
                    clientId = table.Column<string>(type: "TEXT", nullable: false),
                    packageId = table.Column<string>(type: "TEXT", nullable: false),
                    state = table.Column<string>(type: "TEXT", nullable: false),
                    outcome = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DOWNLOAD_INTENT", x => x.operationId);
                    table.ForeignKey(
                        name: "FK_DOWNLOAD_INTENT_CLIENT_REGISTRATION_clientId",
                        column: x => x.clientId,
                        principalTable: "CLIENT_REGISTRATION",
                        principalColumn: "clientId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DOWNLOAD_INTENT_PACKAGE_DESCRIPTOR_packageId",
                        column: x => x.packageId,
                        principalTable: "PACKAGE_DESCRIPTOR",
                        principalColumn: "packageId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "INSTALLATION_MANIFEST",
                columns: table => new
                {
                    manifestId = table.Column<string>(type: "TEXT", nullable: false),
                    packageId = table.Column<string>(type: "TEXT", nullable: false),
                    schemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    signature = table.Column<string>(type: "TEXT", nullable: false),
                    keyId = table.Column<string>(type: "TEXT", nullable: false),
                    canonicalManifest = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_INSTALLATION_MANIFEST", x => x.manifestId);
                    table.ForeignKey(
                        name: "FK_INSTALLATION_MANIFEST_PACKAGE_DESCRIPTOR_packageId",
                        column: x => x.packageId,
                        principalTable: "PACKAGE_DESCRIPTOR",
                        principalColumn: "packageId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CLIENT_ERROR_clientId",
                table: "CLIENT_ERROR",
                column: "clientId");

            migrationBuilder.CreateIndex(
                name: "IX_DOWNLOAD_INTENT_clientId",
                table: "DOWNLOAD_INTENT",
                column: "clientId");

            migrationBuilder.CreateIndex(
                name: "IX_DOWNLOAD_INTENT_packageId",
                table: "DOWNLOAD_INTENT",
                column: "packageId");

            migrationBuilder.CreateIndex(
                name: "IX_INSTALLATION_MANIFEST_packageId",
                table: "INSTALLATION_MANIFEST",
                column: "packageId");

            migrationBuilder.CreateIndex(
                name: "IX_PACKAGE_DESCRIPTOR_gameId",
                table: "PACKAGE_DESCRIPTOR",
                column: "gameId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CLIENT_ERROR");

            migrationBuilder.DropTable(
                name: "CLIENT_INSTALLATION_VIEW");

            migrationBuilder.DropTable(
                name: "DOWNLOAD_INTENT");

            migrationBuilder.DropTable(
                name: "INSTALLATION_MANIFEST");

            migrationBuilder.DropTable(
                name: "CLIENT_REGISTRATION");

            migrationBuilder.DropTable(
                name: "PACKAGE_DESCRIPTOR");

            migrationBuilder.DropTable(
                name: "GAME");
        }
    }
}
