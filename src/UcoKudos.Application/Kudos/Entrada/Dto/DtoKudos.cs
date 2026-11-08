namespace UcoKudos.Application.Kudos.Entrada.Dto;

/// <summary>
/// Datos para publicar un Kudo (RF-07). Los DTO viven en la capa de aplicación, no en el dominio:
/// una entidad nunca cruza hacia la API (ADR-0001, regla 4).
/// </summary>
public sealed record PublicarKudoDto(Guid DestinatarioId, Guid CategoriaId, string Contenido);

/// <summary>Lo que devuelve la publicación. Lo mínimo para que el cliente navegue al Kudo.</summary>
public sealed record KudoPublicadoDto(Guid Id, DateTimeOffset PublicadoEn);

/// <summary>Una categoría para elegir al publicar (RF-06).</summary>
public sealed record CategoriaDto(Guid Id, string Nombre, string Descripcion, bool Activa);

/// <summary>Datos para crear o renombrar una categoría. Solo tres roles pueden (RF-06).</summary>
public sealed record GuardarCategoriaDto(string Nombre, string Descripcion);

/// <summary>
/// Una fila del muro tal como la consume el cliente (RF-13). Es el reflejo del tipo de lectura del
/// dominio, con el cursor ya resuelto.
/// </summary>
public sealed record KudoDelMuroDto(
    Guid Id,
    Guid AutorId,
    string AutorNombre,
    Guid DestinatarioId,
    string DestinatarioNombre,
    Guid CategoriaId,
    string CategoriaNombre,
    string Contenido,
    int TotalReplicas,
    decimal? PromedioCalificacion,
    int TotalCalificaciones,
    int TotalAdjuntos,
    DateTimeOffset PublicadoEn);

/// <summary>
/// Una página del muro con el cursor de la siguiente (ADR-0010). El cliente no envía número de
/// página: devuelve este cursor, que es lo que hace que la página 50 cueste lo mismo que la 1.
/// </summary>
public sealed record PaginaMuroDto(IReadOnlyList<KudoDelMuroDto> Elementos, string? CursorSiguiente);

/// <summary>
/// Intención de cargar un adjunto (RF-08). La API devuelve una URL prefirmada y el cliente sube el
/// archivo **directo** al almacenamiento, sin pasar por la API (ADR-0012).
/// </summary>
public sealed record IntencionAdjuntoDto(string NombreArchivo, string TipoDeclarado, long TamanoDeclarado);

/// <summary>
/// Lo que el cliente necesita para subir: a dónde subir y con qué identificador confirmar después.
/// </summary>
public sealed record SubidaAdjuntoDto(Guid AdjuntoId, string UrlDeSubida, int ValidezMinutos);

/// <summary>Un adjunto ya confirmado, con su enlace de descarga de vida corta.</summary>
public sealed record AdjuntoDto(
    Guid Id,
    string NombreOriginal,
    string? TipoDetectado,
    long TamanoBytes,
    string? UrlDeDescarga);

/// <summary>Detalle de un Kudo con sus adjuntos.</summary>
public sealed record DetalleKudoDto(KudoDelMuroDto Kudo, IReadOnlyList<AdjuntoDto> Adjuntos);
