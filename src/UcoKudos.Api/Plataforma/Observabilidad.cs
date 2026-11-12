using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace UcoKudos.Api.Plataforma;

/// <summary>
/// Exporta trazas, métricas y registros por OTLP (ADR-0014).
///
/// **Por qué existe este archivo, y es una lección que conviene no perder.** La plataforma ya tenía
/// instrumentación propia —un <c>ActivitySource</c> y contadores de negocio— y el panel de
/// observabilidad desplegado y sano, con su variable de entorno apuntándole. Todo parecía
/// configurado. Pero faltaba el SDK que recoge esas señales y las envía: la instrumentación emitía
/// al vacío y el panel llevaba días mostrando cero trazas y cero registros sin que nada fallara.
///
/// Los nombres de las fuentes llegan como parámetros y no se leen de la capa de infraestructura a
/// propósito: una prueba de arquitectura prohíbe que las clases de la API dependan de ella, y la
/// única excepción es el cuerpo de <c>Program.cs</c>, que es la raíz de composición.
/// </summary>
public static class Observabilidad
{
    public static void AgregarObservabilidad(
        this WebApplicationBuilder constructor,
        string fuenteDeTrazas,
        string fuenteDeMetricas)
    {
        ArgumentNullException.ThrowIfNull(constructor);

        var destino = constructor.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var servicio = constructor.Configuration["OTEL_SERVICE_NAME"] ?? "ucokudos-api";

        // **Se dice en voz alta si no hay destino, en lugar de no hacer nada.** Un arranque
        // silencioso es justo lo que escondió el problema la primera vez: sin este aviso, no
        // exportar nada y exportar correctamente se ven exactamente igual desde fuera.
        if (string.IsNullOrWhiteSpace(destino))
        {
            constructor.Logging.AddFilter("UcoKudos.Observabilidad", LogLevel.Information);
            Console.WriteLine(
                "AVISO: OTEL_EXPORTER_OTLP_ENDPOINT no está definido. "
                + "La aplicación funciona, pero NO se exporta telemetría a ninguna parte.");
            return;
        }

        Console.WriteLine($"Observabilidad: exportando a {destino} como «{servicio}».");

        var recurso = ResourceBuilder.CreateDefault().AddService(servicio);

        constructor.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(servicio))
            .WithTracing(trazas => trazas
                // La instrumentación automática cubre las peticiones entrantes y las salientes.
                // Sin la saliente, una llamada a Meta o al almacenamiento no aparecería en la
                // traza y el hueco pasaría por tiempo de la propia aplicación.
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                // La fuente propia es la que aporta lo que ninguna instrumentación automática
                // puede saber: los tramos del despachador de la bandeja, que ocurren **después**
                // de que la petición haya respondido. Es lo que hace que la traza cruce la
                // frontera asíncrona, que es lo que pide QA-OBS-01.
                .AddSource(fuenteDeTrazas)
                // Npgsql publica sus propios tramos: así se ve cuánto de una petición es espera
                // de la base y cuánto es trabajo propio.
                .AddSource("Npgsql")
                .AddOtlpExporter())
            .WithMetrics(metricas => metricas
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(fuenteDeMetricas)
                .AddOtlpExporter());

        constructor.Logging.AddOpenTelemetry(registros =>
        {
            registros.SetResourceBuilder(recurso);

            // Sin estas dos, los registros llegan sin el texto formateado ni los valores de sus
            // parámetros, y buscar «qué pasó con el Kudo X» deja de ser posible.
            registros.IncludeFormattedMessage = true;
            registros.IncludeScopes = true;

            registros.AddOtlpExporter();
        });
    }
}
