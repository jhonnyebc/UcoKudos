using System.Threading.Channels;
using UcoKudos.Application.Comun.Entrada.Puertos;
using UcoKudos.Infrastructure.Notificaciones;
using UcoKudos.Infrastructure.Persistencia;

namespace UcoKudos.Infrastructure.Plataforma;

/// <summary>
/// Adaptador del puerto de avisos: envuelve el canal de SSE y la configuración de Web Push, de modo
/// que la capa de interfaces no conozca ninguno de los dos (ADR-0001, regla 4).
/// </summary>
public sealed class AvisosAdaptadorEntrada(AvisosEnVivoSse avisos, OpcionesPush opciones) : IAvisosPuertoEntrada
{
    public (Guid ConexionId, ChannelReader<string> Lector) Abrir(Guid personaId) => avisos.Abrir(personaId);

    public void Cerrar(Guid personaId, Guid conexionId) => avisos.Cerrar(personaId, conexionId);

    public string? ClavePublicaPush() =>
        string.IsNullOrWhiteSpace(opciones.ClavePublica) ? null : opciones.ClavePublica;
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
