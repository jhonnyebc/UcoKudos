using UcoKudos.Domain.Comun;

namespace UcoKudos.Domain.Kudos.Repositorios;

/// <summary>Puerto de persistencia de Kudos y sus adjuntos.</summary>
public interface IKudoRepositorio
{
    /// <summary>Carga el Kudo con sus adjuntos, para operar sobre el agregado completo.</summary>
    Task<Kudo?> PorIdAsync(Guid id, CancellationToken ct);

    void Agregar(Kudo kudo);

    /// <summary>Recalcula el total de réplicas desde la fuente, para verificar los contadores materializados.</summary>
    Task<int> ContarReplicasAsync(Guid kudoId, CancellationToken ct);
}

/// <summary>Puerto de persistencia de categorías (RF-06).</summary>
public interface ICategoriaRepositorio
{
    Task<Categoria?> PorIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<Categoria>> ActivasAsync(CancellationToken ct);

    Task<IReadOnlyList<Categoria>> TodasAsync(CancellationToken ct);

    Task<bool> ExisteNombreAsync(string nombre, CancellationToken ct);

    void Agregar(Categoria categoria);
}

/// <summary>
/// Lado de lectura del muro (ADR-0010). Está separado del repositorio a propósito: las lecturas no
/// materializan entidades de dominio, devuelven directamente lo que la pantalla necesita, en una
/// sola consulta y leyendo los contadores ya materializados.
/// </summary>
public interface IConsultasMuro
{
    /// <summary>Muro general, paginado por cursor y sin contenido vetado.</summary>
    Task<Pagina<FilaMuro>> MuroAsync(Cursor? cursor, int tamano, CancellationToken ct);

    /// <summary>Kudos recibidos por una persona.</summary>
    Task<Pagina<FilaMuro>> RecibidosAsync(Guid destinatarioId, Cursor? cursor, int tamano, CancellationToken ct);

    /// <summary>Muro filtrado por categoría.</summary>
    Task<Pagina<FilaMuro>> PorCategoriaAsync(Guid categoriaId, Cursor? cursor, int tamano, CancellationToken ct);

    Task<FilaMuro?> DetalleAsync(Guid kudoId, CancellationToken ct);
}

/// <summary>
/// Una fila del muro, con lo que la pantalla muestra y nada más. No es una entidad de dominio: es el
/// resultado de una consulta de lectura.
/// </summary>
public sealed record FilaMuro(
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
