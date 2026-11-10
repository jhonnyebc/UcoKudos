using Microsoft.EntityFrameworkCore;
using UcoKudos.Application.Notificaciones.Salida.Puertos;
using UcoKudos.Infrastructure.Persistencia;

namespace UcoKudos.Infrastructure.Lectura;

/// <summary>
/// Lado de lectura de los avisos (ADR-0010).
///
/// Sin seguimiento de cambios: estas consultas solo pintan una pantalla, y rastrear entidades que
/// nadie va a modificar es trabajo desperdiciado en el camino que más se recorre.
/// </summary>
public sealed class ConsultasAvisosEf(UcoKudosDbContext contexto) : IConsultasAvisos
{
    public async Task<(IReadOnlyList<FilaAviso> Elementos, int SinLeer)> DeLaPersonaAsync(
        Guid personaId, int tamano, CancellationToken ct)
    {
        var elementos = await contexto.Avisos
            .AsNoTracking()
            .Where(a => a.PersonaId == personaId)
            .OrderByDescending(a => a.GeneradoEn)
            .ThenByDescending(a => a.Id)
            .Take(tamano)
            .Select(a => new FilaAviso(a.Id, a.ClaveMensaje, a.Variables, a.Leido, a.GeneradoEn))
            .ToListAsync(ct);

        // El contador cuenta **todos** los no leídos, no los de esta página. Es lo que la interfaz
        // pone en la campana, y sacarlo de la página daría un número equivocado en cuanto haya más
        // de una.
        var sinLeer = await contexto.Avisos
            .AsNoTracking()
            .CountAsync(a => a.PersonaId == personaId && !a.Leido, ct);

        return (elementos, sinLeer);
    }

    /// <summary>
    /// Marca como leído comprobando la propiedad **en la misma operación**.
    ///
    /// El filtro incluye la persona, así que un aviso de otro simplemente no se encuentra. Hacerlo
    /// en dos pasos —buscar, comprobar, escribir— dejaría una ventana entre la comprobación y la
    /// escritura, y además la comprobación se puede olvidar; de esta forma es imposible saltársela.
    /// </summary>
    public async Task<bool> MarcarLeidoAsync(Guid personaId, long avisoId, CancellationToken ct)
    {
        var aviso = await contexto.Avisos
            .FirstOrDefaultAsync(a => a.Id == avisoId && a.PersonaId == personaId, ct);

        if (aviso is null)
        {
            return false;
        }

        // Marcar dos veces no es un error: el cliente puede reintentar y el resultado es el mismo.
        aviso.MarcarLeido();
        await contexto.SaveChangesAsync(ct);

        return true;
    }
}
