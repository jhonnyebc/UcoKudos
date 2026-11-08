namespace UcoKudos.Domain.Interaccion.Repositorios;

/// <summary>Puerto de persistencia de réplicas (RF-09).</summary>
public interface IReplicaRepositorio
{
    Task<Replica?> PorIdAsync(Guid id, CancellationToken ct);

    void Agregar(Replica replica);

    /// <summary>
    /// El hilo completo de un Kudo, ordenado por ruta.
    ///
    /// Ese orden **ya es** el orden anidado de presentación (ADR-0011), así que el cliente lo pinta
    /// tal como lo recibe en lugar de reconstruir el árbol. Es una consulta y un recorrido de
    /// índice, por profundo que sea el hilo.
    /// </summary>
    Task<IReadOnlyList<Replica>> HiloDeAsync(Guid kudoId, CancellationToken ct);

    /// <summary>
    /// Veta una réplica y todos sus descendientes buscando por prefijo de ruta (RF-12). Devuelve
    /// cuántos contenidos quedaron ocultos, que es lo que el veto registra.
    /// </summary>
    Task<int> VetarEnCascadaAsync(RutaReplica ruta, CancellationToken ct);
}

/// <summary>Puerto de persistencia de calificaciones (RF-10).</summary>
public interface ICalificacionRepositorio
{
    /// <summary>La calificación activa de una persona sobre un contenido, si la hay.</summary>
    Task<Calificacion?> ActivaDeAsync(Guid personaId, TipoContenido tipo, Guid contenidoId, CancellationToken ct);

    void Agregar(Calificacion calificacion);

    /// <summary>Total y promedio de las calificaciones activas, para mantener los contadores.</summary>
    Task<ResumenCalificacion> ResumenAsync(TipoContenido tipo, Guid contenidoId, CancellationToken ct);
}

/// <summary>Total y promedio de las calificaciones activas de un contenido.</summary>
public sealed record ResumenCalificacion(int Total, decimal? Promedio);

/// <summary>Puerto de persistencia de la moderación (RF-11, RF-12).</summary>
public interface IModeracionRepositorio
{
    void AgregarReporte(Reporte reporte);

    void AgregarVeto(Veto veto);

    Task<IReadOnlyList<Reporte>> ReportesPendientesAsync(CancellationToken ct);
}
