using Microsoft.EntityFrameworkCore;
using UcoKudos.Application.Notificaciones.Salida.Puertos;
using UcoKudos.Infrastructure.Persistencia;
using UcoKudos.Infrastructure.Persistencia.Outbox;

namespace UcoKudos.Infrastructure.Notificaciones;

/// <summary>
/// Alta y baja de suscripciones a Web Push, contra PostgreSQL (ADR-0009).
/// </summary>
public sealed class SuscripcionesPushEf(UcoKudosDbContext contexto, TimeProvider reloj) : ISuscripcionesPush
{
    public async Task RegistrarAsync(
        Guid personaId, string endpoint, string claveP256dh, string claveAuth, CancellationToken ct)
    {
        // El endpoint identifica al navegador y es lo que hace idempotente el registro. El cliente
        // lo envía en cada arranque, así que sin esta comprobación la tabla crecería una fila por
        // visita y cada Kudo produciría un envío por cada una de ellas, al mismo destino.
        var existente = await contexto.SuscripcionesPush
            .FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);

        if (existente is not null)
        {
            // Puede ser de otra persona si el equipo es compartido y antes lo usó alguien más: en
            // ese caso la suscripción pasa a quien está usando el navegador ahora. Dejar la
            // anterior sería mandarle a la persona equivocada los avisos de esta.
            existente.Reasignar(personaId, claveP256dh, claveAuth);
        }
        else
        {
            contexto.SuscripcionesPush.Add(
                new SuscripcionPushFila(personaId, endpoint, claveP256dh, claveAuth, reloj.GetUtcNow()));
        }

        await contexto.SaveChangesAsync(ct);
    }

    public async Task DarDeBajaAsync(Guid personaId, string endpoint, CancellationToken ct)
    {
        var suscripcion = await contexto.SuscripcionesPush
            .FirstOrDefaultAsync(s => s.Endpoint == endpoint && s.PersonaId == personaId, ct);

        if (suscripcion is null)
        {
            // Dar de baja algo que no está ya deja las cosas como se querían. No es un error, y
            // tratarlo como tal obligaría al cliente a distinguir dos casos que para él son uno.
            return;
        }

        suscripcion.Desactivar();
        await contexto.SaveChangesAsync(ct);
    }
}
