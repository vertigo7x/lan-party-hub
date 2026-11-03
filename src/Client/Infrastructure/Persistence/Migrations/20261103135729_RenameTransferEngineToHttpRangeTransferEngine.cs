using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LANPartyHub.Client.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameTransferEngineToHttpRangeTransferEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AD-4, AC4: el valor persistido de `transferEngine` es EXACTAMENTE el
            // identificador de la única implementación del puerto, `HttpRangeTransferEngine`.
            // El esquema no cambia: solo cambia el VALOR de la columna, así que la
            // migración es una reescritura de datos y no una alteración de tabla.
            //
            // La reescritura es OBLIGATORIA y no opcional: el valor es cerrado, así que un
            // registro que guarde `HttpRange` sería rechazado al arrancar y la operación no
            // se reanudaría (AD-24).
            migrationBuilder.Sql(
                """
                UPDATE "DOWNLOAD_OPERATION"
                SET "transferEngine" = 'HttpRangeTransferEngine'
                WHERE "transferEngine" = 'HttpRange';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // La reversión devuelve el valor anterior a las filas que la migración de
            // subida renombró. Un motor que no existe en la versión activa vuelve a ser
            // un motor desconocido, y el cliente lo rechaza en el arranque (AD-24).
            migrationBuilder.Sql(
                """
                UPDATE "DOWNLOAD_OPERATION"
                SET "transferEngine" = 'HttpRange'
                WHERE "transferEngine" = 'HttpRangeTransferEngine';
                """);
        }
    }
}
