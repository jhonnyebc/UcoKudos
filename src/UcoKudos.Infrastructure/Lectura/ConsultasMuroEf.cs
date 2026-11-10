using Microsoft.EntityFrameworkCore;
using UcoKudos.Domain.Comun;
using UcoKudos.Domain.Kudos.Repositorios;
using UcoKudos.Infrastructure.Persistencia;

namespace UcoKudos.Infrastructure.Lectura;

/// <summary>
/// Lado de lectura del muro (ADR-0010). Es la implementación que responde a DRV-002.
///
/// Tres cosas que hacen que esto sea el lado de lectura y no «consultar por el repositorio»:
///
///  1. **No materializa entidades de dominio.** Proyecta directamente a la fila que la pantalla
///     necesita, con una sola consulta y sus uniones. Cargar un agregado completo para mostrar una
///     línea de una lista es trabajo desperdiciado.
///  2. **Lee los contadores ya materializados.** Ni una agregación: el total de réplicas y el
///     promedio son columnas del Kudo.
///  3. **Pagina por cursor.** El filtro es «anterior a este instante, o del mismo instante con
///     identificador menor», que va por el mismo índice que el orden. Así la página 50 cuesta lo
///     mismo que la 1, y no se repiten ni se saltan Kudos recién publicados.
/// </summary>
public sealed class ConsultasMuroEf(UcoKudosDbContext contexto) : IConsultasMuro
{
    public Task<Pagina<FilaMuro>> MuroAsync(Cursor? cursor, int tamano, CancellationToken ct) =>
        EjecutarAsync(contexto.Kudos.AsNoTracking(), cursor, tamano, ct);

    public Task<Pagina<FilaMuro>> RecibidosAsync(Guid destinatarioId, Cursor? cursor, int tamano, CancellationToken ct) =>
        EjecutarAsync(
            contexto.Kudos.AsNoTracking().Where(k => k.DestinatarioId == destinatarioId), cursor, tamano, ct);

    public Task<Pagina<FilaMuro>> PorCategoriaAsync(Guid categoriaId, Cursor? cursor, int tamano, CancellationToken ct) =>
        EjecutarAsync(
            contexto.Kudos.AsNoTracking().Where(k => k.CategoriaId == categoriaId), cursor, tamano, ct);

    public async Task<FilaMuro?> DetalleAsync(Guid kudoId, CancellationToken ct) =>
        await Proyectar(contexto.Kudos.AsNoTracking().Where(k => k.Id == kudoId)).FirstOrDefaultAsync(ct);

    /// <summary>
    /// La proyección. Las uniones al autor, al destinatario y a la categoría van aquí, en una sola
    /// consulta: es lo que evita el problema de las N+1 consultas que DRV-002 obliga a evitar.
    ///
    /// **Recibe una consulta sobre la entidad, ya filtrada y lista para ordenar, y proyecta al
    /// final.** El orden importa, y no es un detalle de estilo: la primera versión proyectaba
    /// primero y ordenaba después sobre el resultado proyectado, y EF no supo traducir eso a SQL.
    /// La consulta compilaba, pasaba las pruebas unitarias y reventaba en ejecución con un 500 en
    /// cuanto se pedía el muro. Filtrar y ordenar sobre las columnas de la tabla, y proyectar al
    /// final, es además lo que permite al motor usar el índice.
    ///
    /// El filtro de contenido vetado coincide con el de los índices parciales, que es lo que
    /// permite que el motor los use.
    /// </summary>
    private IQueryable<FilaMuro> Proyectar(IQueryable<Domain.Kudos.Kudo> kudos) =>
        from kudo in kudos
        join autor in contexto.Personas.AsNoTracking() on kudo.AutorId equals autor.Id
        join destinatario in contexto.Personas.AsNoTracking() on kudo.DestinatarioId equals destinatario.Id
        join categoria in contexto.Categorias.AsNoTracking() on kudo.CategoriaId equals categoria.Id
        where !kudo.Vetado
        orderby kudo.PublicadoEn descending, kudo.Id descending
        select new FilaMuro(
            kudo.Id,
            autor.Id,
            autor.NombreCompleto,
            destinatario.Id,
            destinatario.NombreCompleto,
            categoria.Id,
            categoria.Nombre,
            kudo.Contenido,
            kudo.TotalReplicas,
            kudo.PromedioCalificacion,
            kudo.TotalCalificaciones,
            contexto.Adjuntos.Count(a => a.KudoId == kudo.Id && a.Confirmado && !a.Eliminado),
            kudo.PublicadoEn);

    private async Task<Pagina<FilaMuro>> EjecutarAsync(
        IQueryable<Domain.Kudos.Kudo> kudos, Cursor? cursor, int tamano, CancellationToken ct)
    {
        if (cursor is { } posicion)
        {
            // La condición del cursor: estrictamente anterior por instante, o del mismo instante con
            // identificador menor. Coincide con el orden, así que usa el mismo índice.
            kudos = kudos.Where(k =>
                k.PublicadoEn < posicion.Instante
                || (k.PublicadoEn == posicion.Instante && k.Id.CompareTo(posicion.Id) < 0));
        }

        // Se pide uno más de los que se devuelven: así se sabe si hay página siguiente sin contar el
        // total, que sería recorrer toda la tabla.
        var filas = await Proyectar(kudos).Take(tamano + 1).ToListAsync(ct);

        var hayMas = filas.Count > tamano;
        var elementos = hayMas ? filas.Take(tamano).ToList() : filas;

        var siguiente = hayMas && elementos.Count > 0
            ? new Cursor(elementos[^1].PublicadoEn, elementos[^1].Id).Codificar()
            : null;

        return new Pagina<FilaMuro>(elementos, siguiente);
    }
}
