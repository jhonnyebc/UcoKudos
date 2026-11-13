namespace UcoKudos.Application.Interaccion.Entrada.Dto;

/// <summary>
/// Datos para responder (RF-09). Si <see cref="ReplicaPadreId"/> es null, la réplica cuelga
/// directamente del Kudo; si no, responde a otra réplica.
/// </summary>
public sealed record ResponderDto(string Contenido, Guid? ReplicaPadreId);

/// <summary>
/// Una réplica tal como la consume el cliente.
///
/// Lleva <see cref="Profundidad"/> porque el hilo llega ya ordenado por ruta, así que el cliente solo
/// necesita la profundidad para la sangría: no tiene que reconstruir el árbol (ADR-0011).
/// </summary>
public sealed record ReplicaDto(
    Guid Id,
    Guid KudoId,
    Guid? ReplicaPadreId,
    Guid AutorId,
    string AutorNombre,
    string Contenido,
    short Profundidad,
    int TotalReplicas,
    decimal? PromedioCalificacion,
    int TotalCalificaciones,
    DateTimeOffset PublicadaEn);

/// <summary>Datos para calificar (RF-10). El tipo es Kudo o Replica.</summary>
public sealed record CalificarDto(string TipoContenido, Guid ContenidoId, short Estrellas);

/// <summary>Datos para reportar contenido. Todos los usuarios pueden (RF-11).</summary>
public sealed record ReportarDto(string TipoContenido, Guid ContenidoId, string Motivo);

/// <summary>Datos para vetar. Solo Rector y Coordinador Académico (RF-12).</summary>
public sealed record VetarDto(string TipoContenido, Guid ContenidoId, string Motivo);

/// <summary>
/// Un reporte pendiente tal como lo ve quien modera: el motivo, quién lo presentó y **el contenido
/// reportado**, para decidir sin tener que buscarlo.
/// </summary>
public sealed record ReportePendienteDto(
    Guid Id,
    string TipoContenido,
    Guid ContenidoId,
    string Motivo,
    string ReportadoPor,
    string AutorDelContenido,
    string Contenido,
    DateTimeOffset PresentadoEn);

/// <summary>Resultado de un veto: cuántos contenidos quedaron ocultos, contando los descendientes.</summary>
public sealed record ResultadoVetoDto(int ContenidosAfectados, bool EnCascada);
