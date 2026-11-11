using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LANPartyHub.Client.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientErrorRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CLIENT_ERROR_REGISTER",
                columns: table => new
                {
                    errorId = table.Column<string>(type: "TEXT", nullable: false),
                    clientId = table.Column<string>(type: "TEXT", nullable: false),
                    operationId = table.Column<string>(type: "TEXT", nullable: false),
                    operationType = table.Column<string>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    message = table.Column<string>(type: "TEXT", nullable: false),
                    cause = table.Column<string>(type: "TEXT", nullable: false),
                    nextStep = table.Column<string>(type: "TEXT", nullable: false),
                    occurredAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CLIENT_ERROR_REGISTER", x => x.errorId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CLIENT_ERROR_REGISTER");
        }
    }
}
