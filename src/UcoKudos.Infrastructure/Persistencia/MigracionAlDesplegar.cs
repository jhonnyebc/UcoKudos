using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace UcoKudos.Infrastructure.Persistencia;

/// <summary>
/// Aplica las migraciones pendientes durante el despliegue (ADR-0003, ADR-0016).
///
/// Vive en la infraestructura y no en la API porque es quien conoce el contexto de persistencia:
/// la API solo decide **cuándo** migrar, no **cómo** (ADR-0001, regla 4).
/// </summary>
public static class MigracionAlDesplegar
{
    public static async Task MigrarBaseDeDatosAsync(
        this IServiceProvider servicios, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        var registro = servicios.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(MigracionAlDesplegar));
        var contexto = servicios.GetRequiredService<UcoKudosDbContext>();

        var pendientes = (await contexto.Database.GetPendingMigrationsAsync(ct)).ToList();

        if (pendientes.Count == 0)
        {
            // Decirlo explícitamente importa: un despliegue silencioso deja la duda de si las
            // migraciones se aplicaron o se saltaron por error.
            registro.LogInformation("No hay migraciones pendientes. El esquema ya está al día.");
            return;
        }

        registro.LogInformation(
            "Aplicando {Cuantas} migraciones pendientes: {Migraciones}",
            pendientes.Count,
            string.Join(", ", pendientes));

        await contexto.Database.MigrateAsync(ct);

        registro.LogInformation("Esquema actualizado.");
    }
}
