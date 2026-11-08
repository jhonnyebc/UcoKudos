using UcoKudos.Application.Comun;
using UcoKudos.Application.Interaccion.CasosUso;
using UcoKudos.Application.Interaccion.Entrada.Dto;
using UcoKudos.Application.Interaccion.Entrada.Puertos;
using UcoKudos.Application.Moderacion.CasosUso;

namespace UcoKudos.Application.Interaccion.Entrada.Adaptadores;

/// <summary>Adaptador de entrada de la interacción.</summary>
public sealed class InteraccionAdaptadorEntrada(
    ResponderCasoUso responder,
    CalificarCasoUso calificar) : IInteraccionPuertoEntrada
{
    public Task<ReplicaDto> ResponderAsync(ContextoPeticion contexto, Guid kudoId, ResponderDto datos, CancellationToken ct) =>
        responder.EjecutarAsync(contexto, kudoId, datos, ct);

    public Task<IReadOnlyList<ReplicaDto>> HiloAsync(Guid kudoId, CancellationToken ct) =>
        responder.HiloAsync(kudoId, ct);

    public Task CalificarAsync(ContextoPeticion contexto, CalificarDto datos, CancellationToken ct) =>
        calificar.EjecutarAsync(contexto, datos, ct);
}

/// <summary>Adaptador de entrada de la moderación.</summary>
public sealed class ModeracionAdaptadorEntrada(
    ReportarContenidoCasoUso reportar,
    VetarContenidoCasoUso vetar) : IModeracionPuertoEntrada
{
    public Task ReportarAsync(ContextoPeticion contexto, ReportarDto datos, CancellationToken ct) =>
        reportar.EjecutarAsync(contexto, datos, ct);

    public Task<ResultadoVetoDto> VetarAsync(ContextoPeticion contexto, VetarDto datos, CancellationToken ct) =>
        vetar.EjecutarAsync(contexto, datos, ct);
}
