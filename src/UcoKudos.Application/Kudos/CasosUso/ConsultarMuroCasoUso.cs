using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Application.Kudos.Entrada.Dto;
using UcoKudos.Domain.Comun;
using UcoKudos.Domain.Kudos.Repositorios;

namespace UcoKudos.Application.Kudos.CasosUso;

/// <summary>
/// Consultar el muro (RF-13). Es la operación más frecuente de la plataforma y la que responde a
/// DRV-002, así que concentra los mecanismos de ADR-0010:
///
///  - Va por el **lado de lectura**, no por el modelo de dominio: no materializa entidades, devuelve
///    directamente lo que la pantalla necesita, en una sola consulta.
///  - Lee los **contadores ya materializados**, así que no agrega sobre el árbol de réplicas.
///  - Pagina por **cursor**, así que la página 50 cuesta lo mismo que la 1 y no se repiten ni se
///    saltan Kudos recién publicados.
///  - La **primera página** va por caché, invalidada por evento y no por expiración.
///
/// Solo la primera página se cachea. Las siguientes son mucho menos frecuentes y cachearlas llenaría
/// la caché de combinaciones que nadie vuelve a pedir.
/// </summary>
public sealed class ConsultarMuroCasoUso(IConsultasMuro consultas, ICacheConsultas cache)
{
    public async Task<PaginaMuroDto> MuroAsync(string? cursorCodificado, int? tamanoSolicitado, CancellationToken ct)
    {
        var tamano = Pagina<FilaMuro>.NormalizarTamano(tamanoSolicitado);
        var cursor = Cursor.Decodificar(cursorCodificado);

        // La primera página es la que se pide una y otra vez: es la única que vale cachear.
        if (cursor is null)
        {
            var clave = GruposCache.Muro + ":primera:" + tamano;
            var enCache = await cache.ObtenerAsync<PaginaMuroDto>(clave, ct);
            if (enCache is not null)
                return enCache;

            var primera = Convertir(await consultas.MuroAsync(null, tamano, ct));
            await cache.GuardarAsync(clave, primera, ct);
            return primera;
        }

        return Convertir(await consultas.MuroAsync(cursor, tamano, ct));
    }

    public async Task<PaginaMuroDto> RecibidosAsync(Guid personaId, string? cursorCodificado, int? tamanoSolicitado, CancellationToken ct)
    {
        var tamano = Pagina<FilaMuro>.NormalizarTamano(tamanoSolicitado);
        var cursor = Cursor.Decodificar(cursorCodificado);

        return Convertir(await consultas.RecibidosAsync(personaId, cursor, tamano, ct));
    }

    public async Task<PaginaMuroDto> PorCategoriaAsync(Guid categoriaId, string? cursorCodificado, int? tamanoSolicitado, CancellationToken ct)
    {
        var tamano = Pagina<FilaMuro>.NormalizarTamano(tamanoSolicitado);
        var cursor = Cursor.Decodificar(cursorCodificado);

        return Convertir(await consultas.PorCategoriaAsync(categoriaId, cursor, tamano, ct));
    }

    public async Task<KudoDelMuroDto?> DetalleAsync(Guid kudoId, CancellationToken ct)
    {
        var fila = await consultas.DetalleAsync(kudoId, ct);
        return fila is null ? null : Convertir(fila);
    }

    private static PaginaMuroDto Convertir(Pagina<FilaMuro> pagina) =>
        new([.. pagina.Elementos.Select(Convertir)], pagina.CursorSiguiente);

    private static KudoDelMuroDto Convertir(FilaMuro fila) =>
        new(
            fila.Id,
            fila.AutorId,
            fila.AutorNombre,
            fila.DestinatarioId,
            fila.DestinatarioNombre,
            fila.CategoriaId,
            fila.CategoriaNombre,
            fila.Contenido,
            fila.TotalReplicas,
            fila.PromedioCalificacion,
            fila.TotalCalificaciones,
            fila.TotalAdjuntos,
            fila.PublicadoEn);
}
