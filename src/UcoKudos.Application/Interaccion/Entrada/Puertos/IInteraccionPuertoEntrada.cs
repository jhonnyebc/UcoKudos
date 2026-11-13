using UcoKudos.Application.Comun;
using UcoKudos.Application.Interaccion.Entrada.Dto;

namespace UcoKudos.Application.Interaccion.Entrada.Puertos;

/// <summary>Puerto de entrada de la interacción: réplicas y calificaciones (RF-09, RF-10).</summary>
public interface IInteraccionPuertoEntrada
{
    Task<ReplicaDto> ResponderAsync(ContextoPeticion contexto, Guid kudoId, ResponderDto datos, CancellationToken ct);

    /// <summary>El hilo completo, ya en el orden en que se debe pintar.</summary>
    Task<IReadOnlyList<ReplicaDto>> HiloAsync(Guid kudoId, CancellationToken ct);

    Task CalificarAsync(ContextoPeticion contexto, CalificarDto datos, CancellationToken ct);
}

/// <summary>Puerto de entrada de la moderación (RF-11, RF-12).</summary>
public interface IModeracionPuertoEntrada
{
    /// <summary>Reportar. Lo puede hacer cualquier usuario (RF-11).</summary>
    Task ReportarAsync(ContextoPeticion contexto, ReportarDto datos, CancellationToken ct);

    /// <summary>Vetar. Solo Rector y Coordinador Académico (RF-12).</summary>
    Task<ResultadoVetoDto> VetarAsync(ContextoPeticion contexto, VetarDto datos, CancellationToken ct);

    /// <summary>Los reportes que esperan decisión. Solo quien puede vetar los ve.</summary>
    Task<IReadOnlyList<ReportePendienteDto>> ReportesPendientesAsync(ContextoPeticion contexto, CancellationToken ct);

    /// <summary>Descarta un reporte sin vetar. Solo quien puede vetar.</summary>
    Task DescartarReporteAsync(ContextoPeticion contexto, Guid reporteId, CancellationToken ct);
}

/// <summary>
/// Puerto de entrada de la auditoría (RF-17).
///
/// La verificación de la cadena se expone como consulta de la plataforma y no como proceso oculto, a
/// propósito: es lo que se ejecuta en vivo para demostrar que el registro es inalterable (ADR-0013).
/// </summary>
public interface IAuditoriaPuertoEntrada
{
    Task<VerificacionDto> VerificarCadenaAsync(ContextoPeticion contexto, int maximo, CancellationToken ct);

    /// <summary>Los eventos de una misma petición: es cómo se reconstruye una publicación (QA-OBS-01).</summary>
    Task<IReadOnlyList<EventoAuditoriaDto>> PorCorrelacionAsync(ContextoPeticion contexto, string correlacionId, CancellationToken ct);
}

/// <summary>Resultado de verificar la cadena de hashes.</summary>
public sealed record VerificacionDto(int EventosRevisados, bool Intacta, long? PrimerEventoAlterado, string Detalle);

/// <summary>Un evento de auditoría visto desde la API.</summary>
public sealed record EventoAuditoriaDto(
    long Id,
    Guid? ActorId,
    string Accion,
    string TipoEntidad,
    Guid EntidadId,
    string Datos,
    string? CorrelacionId,
    DateTimeOffset OcurridoEn);
