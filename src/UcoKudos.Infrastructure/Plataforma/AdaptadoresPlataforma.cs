using System.Threading.Channels;
using UcoKudos.Application.Comun;
using UcoKudos.Application.Comun.Entrada.Puertos;
using UcoKudos.Application.Notificaciones.CasosUso;
using UcoKudos.Application.Notificaciones.Entrada.Dto;
using UcoKudos.Application.Notificaciones.Salida.Puertos;
using UcoKudos.Infrastructure.Notificaciones;
using UcoKudos.Infrastructure.Persistencia;

namespace UcoKudos.Infrastructure.Plataforma;

/// <summary>
/// Adaptador del puerto de avisos: envuelve el canal de SSE y la configuración de Web Push, de modo
/// que la capa de interfaces no conozca ninguno de los dos (ADR-0001, regla 4).
/// </summary>
public sealed class AvisosAdaptadorEntrada(
    AvisosEnVivoSse avisos,
    OpcionesPush opciones,
    ConsultarAvisosCasoUso bandeja,
    ISuscripcionesPush suscripciones) : IAvisosPuertoEntrada
{
    public (Guid ConexionId, ChannelReader<string> Lector) Abrir(Guid personaId) => avisos.Abrir(personaId);

    public void Cerrar(Guid personaId, Guid conexionId) => avisos.Cerrar(personaId, conexionId);

    public string? ClavePublicaPush() =>
        string.IsNullOrWhiteSpace(opciones.ClavePublica) ? null : opciones.ClavePublica;

    public Task<PaginaAvisosDto> MisAvisosAsync(ContextoPeticion contexto, int? tamano, CancellationToken ct) =>
        bandeja.MisAvisosAsync(contexto, tamano, ct);

    public Task MarcarLeidoAsync(ContextoPeticion contexto, long avisoId, CancellationToken ct) =>
        bandeja.MarcarLeidoAsync(contexto, avisoId, ct);

    public Task SuscribirPushAsync(ContextoPeticion contexto, SuscripcionPushDto datos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(datos);

        return suscripciones.RegistrarAsync(
            contexto.Ambito.PersonaId, datos.Endpoint, datos.ClaveP256dh, datos.ClaveAuth, ct);
    }

    public Task CancelarPushAsync(ContextoPeticion contexto, string endpoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        return suscripciones.DarDeBajaAsync(contexto.Ambito.PersonaId, endpoint, ct);
    }
}

/// <summary>
/// Adaptador del puerto de salud. Comprueba lo que la instancia necesita para trabajar.
///
/// Devuelve un booleano y no el detalle del fallo a propósito: un punto de salud consultado por la
/// infraestructura no debe revelar qué componente está caído.
/// </summary>
public sealed class SaludAdaptadorEntrada(UcoKudosDbContext contexto) : ISaludPuertoEntrada
{
    public async Task<bool> EstaListoAsync(CancellationToken ct)
    {
        try
        {
            return await contexto.Database.CanConnectAsync(ct);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return false;
        }
    }
}
