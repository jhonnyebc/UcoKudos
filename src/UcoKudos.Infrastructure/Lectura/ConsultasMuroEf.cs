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
        EjecutarAsync(ConsultaBase(), cursor, tamano, ct);

    public Task<Pagina<FilaMuro>> RecibidosAsync(Guid destinatarioId, Cursor? cursor, int tamano, CancellationToken ct) =>
        EjecutarAsync(ConsultaBase().Where(f => f.DestinatarioId == destinatarioId), cursor, tamano, ct);

    public Task<Pagina<FilaMuro>> PorCategoriaAsync(Guid categoriaId, Cursor? cursor, int tamano, CancellationToken ct) =>
        EjecutarAsync(ConsultaBase().Where(f => f.CategoriaId == categoriaId), cursor, tamano, ct);

    public async Task<FilaMuro?> DetalleAsync(Guid kudoId, CancellationToken ct) =>
        await ConsultaBase().FirstOrDefaultAsync(f => f.Id == kudoId, ct);

    /// <summary>
    /// La proyección. Las uniones al autor, al destinatario y a la categoría van aquí, en una sola
    /// consulta: es lo que evita el problema de las N+1 consultas que DRV-002 obliga a evitar.
    ///
    /// El filtro de contenido vetado coincide con el de los índices parciales, que es lo que permite
    /// que el motor los use.
    /// </summary>
    private IQueryable<FilaMuro> ConsultaBase() =>
        from kudo in contexto.Kudos.AsNoTracking()
        join autor in contexto.Personas.AsNoTracking() on kudo.AutorId equals autor.Id
        join destinatario in contexto.Personas.AsNoTracking() on kudo.DestinatarioId equals destinatario.Id
        join categoria in contexto.Categorias.AsNoTracking() on kudo.CategoriaId equals categoria.Id
        where !kudo.Vetado
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

    private static async Task<Pagina<FilaMuro>> EjecutarAsync(
        IQueryable<FilaMuro> consulta, Cursor? cursor, int tamano, CancellationToken ct)
    {
        if (cursor is { } posicion)
        {
            // La condición del cursor: estrictamente anterior por instante, o del mismo instante con
            // identificador menor. Coincide con el orden, así que usa el mismo índice.
            consulta = consulta.Where(f =>
                f.PublicadoEn < posicion.Instante
                || (f.PublicadoEn == posicion.Instante && f.Id.CompareTo(posicion.Id) < 0));
        }

        // Se pide uno más de los que se devuelven: así se sabe si hay página siguiente sin contar el
        // total, que sería recorrer toda la tabla.
        var filas = await consulta
            .OrderByDescending(f => f.PublicadoEn)
            .ThenByDescending(f => f.Id)
            .Take(tamano + 1)
            .ToListAsync(ct);

        var hayMas = filas.Count > tamano;
        var elementos = hayMas ? filas.Take(tamano).ToList() : filas;

        var siguiente = hayMas && elementos.Count > 0
            ? new Cursor(elementos[^1].PublicadoEn, elementos[^1].Id).Codificar()
            : null;

        return new Pagina<FilaMuro>(elementos, siguiente);
    }
}
