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
/// Intención de cargar un adjunto (RF-08): el primero de los tres pasos. La API valida lo que
/// declara el cliente y reserva el adjunto; después el cliente sube el contenido a la propia API y
/// confirma (ADR-0012, revisión del 2026-11-13).
/// </summary>
public sealed record IntencionAdjuntoDto(string NombreArchivo, string TipoDeclarado, long TamanoDeclarado);

/// <summary>Lo que el cliente necesita para seguir: el identificador del adjunto reservado.</summary>
public sealed record SubidaAdjuntoDto(Guid AdjuntoId);

/// <summary>
/// Un adjunto ya confirmado. **No lleva enlace de descarga**: el archivo se pide a la API con la
/// sesión, que es lo que permite comprobar permisos y auditar cada descarga. Antes llevaba una URL
/// prefirmada por adjunto, generada además cada vez que se listaba, sin dejar rastro de quién la
/// pidió.
/// </summary>
public sealed record AdjuntoDto(Guid Id, string NombreOriginal, string? TipoDetectado, long TamanoBytes);

/// <summary>El contenido de un adjunto, con lo necesario para servirlo.</summary>
public sealed record ArchivoAdjuntoDto(byte[] Contenido, string Tipo, string NombreOriginal);

/// <summary>Detalle de un Kudo con sus adjuntos.</summary>
public sealed record DetalleKudoDto(KudoDelMuroDto Kudo, IReadOnlyList<AdjuntoDto> Adjuntos);
