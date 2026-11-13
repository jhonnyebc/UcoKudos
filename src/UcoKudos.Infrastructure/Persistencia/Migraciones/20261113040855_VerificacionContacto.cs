using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UcoKudos.Infrastructure.Persistencia.Migraciones;

/// <inheritdoc />
public partial class VerificacionContacto : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "verificacion_contacto",
            schema: "nucleo",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                huella_codigo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expira_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                intentos = table.Column<int>(type: "integer", nullable: false),
                consumida = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_verificacion_contacto", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_verificacion_persona_tipo",
            schema: "nucleo",
            table: "verificacion_contacto",
            columns: new[] { "persona_id", "tipo" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "verificacion_contacto",
            schema: "nucleo");
    }
}
