using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UcoKudos.Infrastructure.Persistencia.Migraciones;

/// <inheritdoc />
public partial class AuditoriaDatosComoTexto : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "datos",
            schema: "audit",
            table: "evento_auditoria",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "jsonb");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "datos",
            schema: "audit",
            table: "evento_auditoria",
            type: "jsonb",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text");
    }
}
