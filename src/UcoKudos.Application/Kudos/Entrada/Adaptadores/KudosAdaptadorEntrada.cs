using UcoKudos.Application.Comun;
using UcoKudos.Application.Kudos.CasosUso;
using UcoKudos.Application.Kudos.Entrada.Dto;
using UcoKudos.Application.Kudos.Entrada.Puertos;

namespace UcoKudos.Application.Kudos.Entrada.Adaptadores;

/// <summary>
/// Adaptador de entrada del módulo de Kudos: implementa el puerto delegando en los casos de uso.
///
/// Su única responsabilidad es componer. No tiene lógica propia, y eso es lo que se quiere: si
/// apareciera una regla aquí, estaría en el sitio donde ninguna prueba de dominio la alcanzaría.
/// </summary>
public sealed class KudosAdaptadorEntrada(
    PublicarKudoCasoUso publicar,
    ConsultarMuroCasoUso consultar,
    AdjuntosCasosUso adjuntos) : IKudosPuertoEntrada
{
    public Task<KudoPublicadoDto> PublicarAsync(ContextoPeticion contexto, PublicarKudoDto datos, CancellationToken ct) =>
        publicar.EjecutarAsync(contexto, datos, ct);

    public Task<PaginaMuroDto> MuroAsync(string? cursor, int? tamano, CancellationToken ct) =>
        consultar.MuroAsync(cursor, tamano, ct);

    public Task<PaginaMuroDto> RecibidosAsync(Guid personaId, string? cursor, int? tamano, CancellationToken ct) =>
        consultar.RecibidosAsync(personaId, cursor, tamano, ct);

    public Task<PaginaMuroDto> PorCategoriaAsync(Guid categoriaId, string? cursor, int? tamano, CancellationToken ct) =>
        consultar.PorCategoriaAsync(categoriaId, cursor, tamano, ct);

    public async Task<DetalleKudoDto?> DetalleAsync(Guid kudoId, CancellationToken ct)
    {
        var kudo = await consultar.DetalleAsync(kudoId, ct);
        if (kudo is null)
            return null;

        var lista = await adjuntos.DeKudoAsync(kudoId, ct);
        return new DetalleKudoDto(kudo, lista);
    }
}

/// <summary>Adaptador de entrada de los adjuntos.</summary>
public sealed class AdjuntosAdaptadorEntrada(AdjuntosCasosUso casos) : IAdjuntosPuertoEntrada
{
    public Task<SubidaAdjuntoDto> PrepararSubidaAsync(ContextoPeticion contexto, Guid kudoId, IntencionAdjuntoDto datos, CancellationToken ct) =>
        casos.PrepararSubidaAsync(contexto, kudoId, datos, ct);

    public Task ConfirmarSubidaAsync(ContextoPeticion contexto, Guid kudoId, Guid adjuntoId, CancellationToken ct) =>
        casos.ConfirmarSubidaAsync(contexto, kudoId, adjuntoId, ct);

    public Task<string> UrlDeDescargaAsync(ContextoPeticion contexto, Guid kudoId, Guid adjuntoId, CancellationToken ct) =>
        casos.UrlDeDescargaAsync(contexto, kudoId, adjuntoId, ct);

    public Task EliminarAsync(ContextoPeticion contexto, Guid kudoId, Guid adjuntoId, CancellationToken ct) =>
        casos.EliminarAsync(contexto, kudoId, adjuntoId, ct);
}

/// <summary>Adaptador de entrada de las categorías.</summary>
public sealed class CategoriasAdaptadorEntrada(CategoriasCasosUso casos) : ICategoriasPuertoEntrada
{
    public Task<IReadOnlyList<CategoriaDto>> ActivasAsync(CancellationToken ct) =>
        casos.ActivasAsync(ct);

    public Task<IReadOnlyList<CategoriaDto>> TodasAsync(ContextoPeticion contexto, CancellationToken ct) =>
        casos.TodasAsync(contexto, ct);

    public Task<CategoriaDto> CrearAsync(ContextoPeticion contexto, GuardarCategoriaDto datos, CancellationToken ct) =>
        casos.CrearAsync(contexto, datos, ct);

    public Task ActualizarAsync(ContextoPeticion contexto, Guid id, GuardarCategoriaDto datos, CancellationToken ct) =>
        casos.ActualizarAsync(contexto, id, datos, ct);

    public Task CambiarEstadoAsync(ContextoPeticion contexto, Guid id, bool activa, CancellationToken ct) =>
        casos.CambiarEstadoAsync(contexto, id, activa, ct);
}
