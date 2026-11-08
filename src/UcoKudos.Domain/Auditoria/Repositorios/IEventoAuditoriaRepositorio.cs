namespace UcoKudos.Domain.Auditoria.Repositorios;

/// <summary>
/// Puerto de la auditoría (RF-17). No tiene método de borrado ni de actualización, y es deliberado:
/// la tabla es de solo inserción y lo garantiza un disparador en la base, así que ni la propia
/// aplicación puede alterarla (ADR-0013).
/// </summary>
public interface IEventoAuditoriaRepositorio
{
    void Agregar(EventoAuditoria evento);

    /// <summary>Hash del último evento, para encadenar el siguiente. Null si la cadena está vacía.</summary>
    Task<string?> UltimoHashAsync(CancellationToken ct);

    /// <summary>Los eventos en orden de inserción, para verificar la cadena.</summary>
    Task<IReadOnlyList<EventoAuditoria>> EnOrdenAsync(int maximo, CancellationToken ct);

    /// <summary>Los eventos de una misma petición: es cómo se reconstruye una publicación (QA-OBS-01).</summary>
    Task<IReadOnlyList<EventoAuditoria>> PorCorrelacionAsync(string correlacionId, CancellationToken ct);
}
