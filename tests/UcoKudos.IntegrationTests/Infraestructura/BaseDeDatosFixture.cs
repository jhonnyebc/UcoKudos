using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using UcoKudos.Infrastructure.Persistencia;

namespace UcoKudos.IntegrationTests.Infraestructura;

/// <summary>
/// Levanta un PostgreSQL **real** en un contenedor desechable y le aplica las migraciones.
///
/// Contra una base real y no contra un doble en memoria, por lo que ADR-0003 declara: una base en
/// memoria no reproduce los índices únicos parciales de RF-04, ni el comportamiento de
/// <c>FOR UPDATE SKIP LOCKED</c>, ni el disparador que hace la auditoría inmutable, ni el truncamiento
/// de precisión de los instantes que haría fallar la verificación de la cadena de hashes.
///
/// Y eso es exactamente lo que estas pruebas tienen que verificar.
/// </summary>
public sealed class BaseDeDatosFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:18.2-alpine")
        .WithDatabase("ucokudos_pruebas")
        .WithUsername("pruebas")
        .WithPassword("pruebas")
        // Más conexiones de las que trae por omisión: la suite abre varias a la vez al probar la
        // concurrencia de la bandeja, y sin esto el contenedor las rechaza.
        .WithCommand("-c", "max_connections=200")
        .Build();

    public string CadenaConexion => _contenedor.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();

        await using var contexto = CrearContexto();
        await contexto.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _contenedor.DisposeAsync();

    /// <summary>Un contexto nuevo. Cada prueba usa el suyo, para no compartir el rastreo de cambios.</summary>
    public UcoKudosDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<UcoKudosDbContext>()
            .UseNpgsql(CadenaConexion, npgsql =>
                npgsql.MigrationsHistoryTable("migracion", UcoKudosDbContext.EsquemaNucleo))
            .Options;

        return new UcoKudosDbContext(opciones);
    }

    /// <summary>
    /// Vacía las tablas entre pruebas, respetando el orden de las claves externas.
    ///
    /// La auditoría se trunca con <c>TRUNCATE</c> y no con <c>DELETE</c> a propósito: el disparador de
    /// solo inserción rechaza los borrados, así que un <c>DELETE</c> fallaría. Que haya que hacer esto
    /// es la prueba de que el disparador funciona.
    /// </summary>
    public async Task LimpiarAsync()
    {
        await using var contexto = CrearContexto();

        await contexto.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE
                audit.evento_auditoria,
                notif.notificacion,
                notif.outbox_mensaje,
                notif.aviso,
                notif.suscripcion_push,
                interaccion.calificacion,
                interaccion.reporte,
                interaccion.veto,
                interaccion.replica,
                nucleo.adjunto,
                nucleo.kudo,
                nucleo.asignacion_rol,
                nucleo.vinculo_academico,
                nucleo.persona,
                nucleo.programa,
                nucleo.facultad
            RESTART IDENTITY CASCADE;
            """);

        // Las categorías NO se truncan: las siembra la migración y las pruebas se refieren a ellas
        // por su identificador fijo. Pero sí se devuelven a su estado sembrado, porque una prueba que
        // desactiva una categoría dejaría ese estado para las siguientes.
        //
        // Es un fallo de aislamiento que ya ocurrió: tres pruebas del flujo obligatorio empezaron a
        // fallar con «la categoría ya no está disponible» por culpa de otra prueba, no del código.
        await contexto.Database.ExecuteSqlRawAsync("UPDATE nucleo.categoria SET activa = true WHERE NOT activa;");
    }
}

/// <summary>
/// Colección de pruebas que comparten el contenedor. Levantar uno por clase de prueba multiplicaría
/// por diez el tiempo de la suite sin aportar aislamiento real, porque cada prueba ya limpia.
/// </summary>
[CollectionDefinition(Nombre)]
public sealed class ColeccionBaseDeDatos : ICollectionFixture<BaseDeDatosFixture>
{
    public const string Nombre = "base-de-datos";
}
