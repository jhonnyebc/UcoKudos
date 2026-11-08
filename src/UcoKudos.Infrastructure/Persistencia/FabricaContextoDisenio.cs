using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UcoKudos.Infrastructure.Persistencia;

/// <summary>
/// Crea el contexto para las herramientas de migración, que corren **sin** levantar la aplicación.
///
/// Usa una cadena de conexión de marcador: generar o inspeccionar una migración no necesita una base
/// real, solo saber que el proveedor es PostgreSQL. Así se puede generar una migración sin tener la
/// base encendida y sin que la herramienta pida secretos (ADR-0018).
///
/// Para aplicar migraciones de verdad, la cadena viene de la configuración del entorno.
/// </summary>
public sealed class FabricaContextoDisenio : IDesignTimeDbContextFactory<UcoKudosDbContext>
{
    public UcoKudosDbContext CreateDbContext(string[] args)
    {
        var cadena = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                     ?? "Host=localhost;Port=5435;Database=ucokudos;Username=ucokudos;Password=disenio";

        var opciones = new DbContextOptionsBuilder<UcoKudosDbContext>()
            .UseNpgsql(cadena, npgsql => npgsql.MigrationsHistoryTable("migracion", UcoKudosDbContext.EsquemaNucleo))
            .Options;

        return new UcoKudosDbContext(opciones);
    }
}
