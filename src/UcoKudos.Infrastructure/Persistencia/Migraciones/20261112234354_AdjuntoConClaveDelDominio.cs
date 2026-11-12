using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UcoKudos.Infrastructure.Persistencia.Migraciones;

/// <summary>
/// **No cambia el esquema de la base, y está vacía a propósito.**
///
/// Declara que la clave del adjunto la asigna el dominio y no EF (<c>ValueGeneratedNever</c>), lo
/// que corrige un 409 al añadir un adjunto a un Kudo cargado de la base. Eso es un cambio del
/// *modelo*, no de las tablas: la columna <c>id</c> ya era un <c>uuid</c> sin valor por omisión.
///
/// Existe porque EF Core se niega a aplicar migraciones si el modelo difiere de la última
/// instantánea: sin esta, el despliegue habría fallado al migrar. Lo único que cambia es la
/// instantánea, que pierde una línea.
/// </summary>
public partial class AdjuntoConClaveDelDominio : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
