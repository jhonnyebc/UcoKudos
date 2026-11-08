using Microsoft.EntityFrameworkCore;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Domain.Transversal;
using UcoKudos.Infrastructure.Persistencia.Outbox;

namespace UcoKudos.Infrastructure.Persistencia;

/// <summary>
/// Unidad de trabajo sobre el contexto de EF Core (ADR-0005).
///
/// Es la pieza que hace atómica la publicación de un Kudo: el cambio de estado, su evento de
/// auditoría y su evento de la bandeja se acumulan en el mismo contexto y se confirman en una sola
/// transacción. Los repositorios **no** guardan por su cuenta, y eso es parte del patrón y no un
/// descuido: si uno lo hiciera, la atomicidad se rompería sin que nada avisara.
///
/// Traduce el conflicto de concurrencia del ORM a una excepción de dominio, para que la capa de
/// interfaces lo convierta en un 409 y el usuario pueda reintentar en lugar de que algo se
/// sobrescriba en silencio.
/// </summary>
public sealed class UnidadTrabajoEf(UcoKudosDbContext contexto) : IUnidadTrabajo, IBandejaSalida
{
    public async Task ConfirmarAsync(CancellationToken ct)
    {
        try
        {
            await contexto.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException error)
        {
            throw new ExcepcionConflictoConcurrencia(
                "Otra operación modificó este contenido al mismo tiempo. Vuelva a intentarlo.")
            {
                Source = error.Source
            };
        }
    }

    public void Descartar()
    {
        foreach (var entrada in contexto.ChangeTracker.Entries().ToList())
            entrada.State = EntityState.Detached;
    }

    /// <summary>
    /// Programa el evento para despacharlo cuanto antes. Se añade al **mismo** contexto, así que
    /// viaja en la misma transacción que el cambio que lo origina: es lo que cierra la ventana
    /// «commit exitoso, envío perdido».
    /// </summary>
    public void Programar(EventoPendiente evento)
    {
        ArgumentNullException.ThrowIfNull(evento);
        ProgramarPara(evento, evento.OcurridoEn);
    }

    public void ProgramarPara(EventoPendiente evento, DateTimeOffset cuando)
    {
        ArgumentNullException.ThrowIfNull(evento);

        contexto.MensajesOutbox.Add(new MensajeOutbox(
            evento.EventoId,
            evento.TipoEvento,
            evento.Carga,
            evento.OcurridoEn,
            cuando,
            evento.ContextoTraza));
    }
}
