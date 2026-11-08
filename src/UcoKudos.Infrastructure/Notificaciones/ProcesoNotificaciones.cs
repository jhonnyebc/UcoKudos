using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UcoKudos.Application.Notificaciones.Entrada.Puertos;
using UcoKudos.Infrastructure.Observabilidad;
using UcoKudos.Infrastructure.Persistencia;

namespace UcoKudos.Infrastructure.Notificaciones;

/// <summary>Opciones del proceso de notificaciones.</summary>
public sealed class OpcionesProcesoNotificaciones
{
    public const string Seccion = "UcoKudos:Notificaciones";

    public bool Habilitado { get; set; } = true;

    public int IntervaloSegundos { get; set; } = 2;

    /// <summary>Eventos por vuelta. Acota el trabajo de cada ciclo para que no monopolice el proceso.</summary>
    public int LoteMaximo { get; set; } = 50;
}

/// <summary>
/// Procesa la bandeja de salida (ADR-0005, ADR-0006). Es la segunda mitad del flujo obligatorio: los
/// pasos 2, 3 y 4 del enunciado ocurren aquí.
///
/// Las cuatro decisiones que hacen que esto funcione, y que conviene poder explicar:
///
///  1. **`FOR UPDATE SKIP LOCKED`**: toma un evento con bloqueo y salta los que otra instancia ya
///     tomó. Si mañana hay dos instancias, ninguna procesa el mismo evento que otra (QA-ELAS-01).
///
///  2. **Se marca procesado DENTRO de la misma transacción** en que se atiende. Marcarlo después
///     reabriría la ventana de pérdida que el patrón Outbox existe para cerrar: si el proceso muere
///     entre el envío y la marca, el evento se reenviaría.
///
///  3. **La traza se retoma** del contexto guardado en el evento, así que la publicación y sus avisos
///     son la misma traza y se puede responder «qué le pasó a esta publicación» (QA-OBS-01).
///
///  4. **Un fallo transitorio devuelve el evento a la cola** con espera creciente; uno permanente lo
///     descarta tras agotar intentos. Reintentar un número inválido mil veces no lo vuelve válido.
/// </summary>
public sealed partial class ProcesoNotificaciones(
    IServiceScopeFactory alcances,
    OpcionesProcesoNotificaciones opciones,
    TimeProvider reloj,
    ILogger<ProcesoNotificaciones> registro) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!opciones.Habilitado)
        {
            ProcesoDesactivado(registro);
            return;
        }

        using var temporizador = new PeriodicTimer(
            TimeSpan.FromSeconds(Math.Max(1, opciones.IntervaloSegundos)), reloj);

        do
        {
            try
            {
                await ProcesarVueltaAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                // Una vuelta fallida no detiene el proceso: el evento sigue pendiente y se reintenta.
                VueltaFallida(registro, error);
            }
        }
        while (await temporizador.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Una vuelta completa. Es público para que las pruebas de integración lo invoquen sin esperar al temporizador.</summary>
    public async Task<int> ProcesarVueltaAsync(CancellationToken ct)
    {
        var atendidos = 0;
        while (atendidos < opciones.LoteMaximo && await ProcesarSiguienteAsync(ct))
            atendidos++;

        return atendidos;
    }

    /// <summary>
    /// Toma y procesa un evento. Devuelve false cuando no queda ninguno libre, que es la señal de que
    /// la vuelta terminó.
    /// </summary>
    private async Task<bool> ProcesarSiguienteAsync(CancellationToken ct)
    {
        await using var alcance = alcances.CreateAsyncScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<UcoKudosDbContext>();
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        var ahora = reloj.GetUtcNow();

        // SKIP LOCKED: varias instancias no se pisan. Va en SQL escrito a mano porque el ORM no
        // expresa el bloqueo con salto de filas bloqueadas.
        var eventos = await contexto.MensajesOutbox
            .FromSql(
                $"""
                 SELECT * FROM notif.outbox_mensaje
                 WHERE procesado_en IS NULL AND proximo_intento <= {ahora}
                 ORDER BY proximo_intento, ocurrido_en
                 LIMIT 1
                 FOR UPDATE SKIP LOCKED
                 """)
            .ToListAsync(ct);

        var evento = eventos.FirstOrDefault();
        if (evento is null)
            return false;

        // La traza se retoma aquí: es lo que hace que la publicación y sus avisos sean la misma traza.
        using var actividad = Instrumentacion.IniciarDesdeContexto(
            "notificaciones.procesar", evento.ContextoTraza);
        actividad?.SetTag("evento.tipo", evento.TipoEvento);
        actividad?.SetTag("evento.id", evento.EventoId);

        try
        {
            var puerto = alcance.ServiceProvider.GetRequiredService<INotificacionesPuertoEntrada>();

            var terminado = await puerto.ProcesarAsync(
                evento.EventoId, evento.TipoEvento, evento.Payload, evento.OcurridoEn, evento.ContextoTraza, ct);

            if (terminado)
            {
                // Se marca procesado en la MISMA transacción en que se atendió.
                evento.MarcarProcesado(reloj.GetUtcNow());
            }
            else
            {
                evento.RegistrarFallo("Algún canal falló de forma transitoria.", reloj.GetUtcNow());
            }

            await contexto.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);

            Instrumentacion.EventosProcesados.Add(1,
                new KeyValuePair<string, object?>("tipo", evento.TipoEvento),
                new KeyValuePair<string, object?>("terminado", terminado));

            EventoAtendido(registro, evento.TipoEvento, evento.EventoId, terminado);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // La transacción se deshace: el evento queda pendiente, no a medio procesar. Es lo que
            // hace que matar el proceso en el peor momento no pierda ninguna notificación.
            await transaccion.RollbackAsync(ct);
            actividad?.SetStatus(ActivityStatusCode.Error, error.Message);
            EventoFallido(registro, evento.TipoEvento, evento.EventoId, error);

            await RegistrarFalloAsync(evento.Id, error, ct);
        }

        return true;
    }

    /// <summary>
    /// Registra el fallo en una transacción aparte, porque la anterior se deshizo. Si no, el contador
    /// de intentos nunca subiría y el evento se reintentaría para siempre.
    /// </summary>
    private async Task RegistrarFalloAsync(Guid id, Exception error, CancellationToken ct)
    {
        try
        {
            await using var alcance = alcances.CreateAsyncScope();
            var contexto = alcance.ServiceProvider.GetRequiredService<UcoKudosDbContext>();

            var evento = await contexto.MensajesOutbox.FirstOrDefaultAsync(m => m.Id == id, ct);
            if (evento is null)
                return;

            evento.RegistrarFallo(error.GetType().Name + ": " + error.Message, reloj.GetUtcNow());
            await contexto.SaveChangesAsync(ct);

            if (evento.ProcesadoEn is not null)
            {
                EventoDescartado(registro, evento.TipoEvento, evento.EventoId, Persistencia.Outbox.MensajeOutbox.MaximoIntentos);
                Instrumentacion.EventosDescartados.Add(1, new KeyValuePair<string, object?>("tipo", evento.TipoEvento));
            }
        }
        catch (Exception secundario) when (secundario is not OperationCanceledException)
        {
            VueltaFallida(registro, secundario);
        }
    }

    [LoggerMessage(EventId = 9001, Level = LogLevel.Information,
        Message = "Notificaciones: evento {TipoEvento} {EventoId} atendido (terminado: {Terminado}).")]
    private static partial void EventoAtendido(ILogger registro, string tipoEvento, Guid eventoId, bool terminado);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Error,
        Message = "Notificaciones: el evento {TipoEvento} {EventoId} falló; se reintentará.")]
    private static partial void EventoFallido(ILogger registro, string tipoEvento, Guid eventoId, Exception error);

    [LoggerMessage(EventId = 9003, Level = LogLevel.Warning,
        Message = "Notificaciones: el evento {TipoEvento} {EventoId} agotó sus {Intentos} intentos y se descartó.")]
    private static partial void EventoDescartado(ILogger registro, string tipoEvento, Guid eventoId, int intentos);

    [LoggerMessage(EventId = 9004, Level = LogLevel.Error, Message = "Notificaciones: la vuelta falló.")]
    private static partial void VueltaFallida(ILogger registro, Exception error);

    [LoggerMessage(EventId = 9005, Level = LogLevel.Warning,
        Message = "Notificaciones: el proceso está desactivado por configuración.")]
    private static partial void ProcesoDesactivado(ILogger registro);
}
