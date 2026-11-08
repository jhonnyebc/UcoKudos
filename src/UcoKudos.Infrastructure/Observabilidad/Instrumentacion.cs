using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace UcoKudos.Infrastructure.Observabilidad;

/// <summary>
/// Instrumentación propia de la plataforma (ADR-0014).
///
/// Las métricas que importan aquí no son las técnicas habituales, que la instrumentación automática
/// ya aporta, sino las **de negocio**: eventos pendientes en la bandeja, antigüedad del más viejo,
/// envíos fallidos por canal y eventos descartados. Una bandeja que crece es el primer síntoma de
/// que un proveedor dejó de responder, antes de que alguien se queje.
/// </summary>
public static class Instrumentacion
{
    public const string NombreFuente = "UcoKudos";
    public const string NombreMetricas = "UcoKudos.Negocio";

    private static readonly ActivitySource Fuente = new(NombreFuente);
    private static readonly Meter Medidor = new(NombreMetricas);

    /// <summary>Eventos de la bandeja atendidos, por tipo.</summary>
    public static readonly Counter<long> EventosProcesados =
        Medidor.CreateCounter<long>("ucokudos.outbox.procesados", "evento", "Eventos de la bandeja atendidos.");

    /// <summary>
    /// Eventos que agotaron sus intentos. Es la métrica que hay que vigilar: significa que una
    /// notificación no llegó y nadie va a reintentarla.
    /// </summary>
    public static readonly Counter<long> EventosDescartados =
        Medidor.CreateCounter<long>("ucokudos.outbox.descartados", "evento", "Eventos descartados tras agotar intentos.");

    /// <summary>Envíos por canal y resultado. Permite ver qué proveedor está fallando.</summary>
    public static readonly Counter<long> EnviosPorCanal =
        Medidor.CreateCounter<long>("ucokudos.notificaciones.envios", "envio", "Envíos por canal y resultado.");

    /// <summary>Kudos publicados. Es la métrica de negocio más directa de la plataforma.</summary>
    public static readonly Counter<long> KudosPublicados =
        Medidor.CreateCounter<long>("ucokudos.kudos.publicados", "kudo", "Kudos publicados.");

    /// <summary>Abre una actividad propia.</summary>
    public static Activity? Iniciar(string nombre) => Fuente.StartActivity(nombre);

    /// <summary>
    /// Abre una actividad **continuando** la traza de un contexto guardado. Es la pieza que hace que
    /// la traza cruce la frontera asíncrona de la bandeja (QA-OBS-01).
    ///
    /// Sin esto, la petición que publicó el Kudo y el proceso que envía los avisos segundos después
    /// serían dos trazas sin relación, y no habría forma de responder qué le pasó a una publicación
    /// concreta.
    /// </summary>
    public static Activity? IniciarDesdeContexto(string nombre, string? contextoTraza)
    {
        if (string.IsNullOrWhiteSpace(contextoTraza))
            return Fuente.StartActivity(nombre);

        return ActivityContext.TryParse(contextoTraza, null, out var contexto)
            ? Fuente.StartActivity(nombre, ActivityKind.Consumer, contexto)
            : Fuente.StartActivity(nombre);
    }

    /// <summary>
    /// Serializa el contexto de la traza actual para guardarlo en el evento. El formato es el
    /// estándar de propagación, así que lo entiende cualquier herramienta.
    /// </summary>
    public static string? ContextoActual() => Activity.Current?.Id;
}
