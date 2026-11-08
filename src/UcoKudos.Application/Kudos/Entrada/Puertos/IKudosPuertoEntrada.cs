using UcoKudos.Application.Comun;
using UcoKudos.Application.Kudos.Entrada.Dto;

namespace UcoKudos.Application.Kudos.Entrada.Puertos;

/// <summary>
/// Puerto de entrada del módulo de Kudos: lo único que la API puede llamar (ADR-0001, regla 4).
///
/// Agrupa varios casos de uso detrás de una interfaz para que la capa de interfaces tenga **una**
/// dependencia por módulo en lugar de una por operación. Y es lo que hace verificable la regla 4:
/// un endpoint depende de esta interfaz, nunca de un caso de uso concreto.
/// </summary>
public interface IKudosPuertoEntrada
{
    /// <summary>Publicar un Kudo (RF-07). Es la funcionalidad obligatoria.</summary>
    Task<KudoPublicadoDto> PublicarAsync(ContextoPeticion contexto, PublicarKudoDto datos, CancellationToken ct);

    /// <summary>Muro general, paginado por cursor (RF-13).</summary>
    Task<PaginaMuroDto> MuroAsync(string? cursor, int? tamano, CancellationToken ct);

    /// <summary>Kudos recibidos por una persona.</summary>
    Task<PaginaMuroDto> RecibidosAsync(Guid personaId, string? cursor, int? tamano, CancellationToken ct);

    /// <summary>Muro filtrado por categoría.</summary>
    Task<PaginaMuroDto> PorCategoriaAsync(Guid categoriaId, string? cursor, int? tamano, CancellationToken ct);

    /// <summary>Detalle de un Kudo con sus adjuntos y sus enlaces de descarga.</summary>
    Task<DetalleKudoDto?> DetalleAsync(Guid kudoId, CancellationToken ct);
}

/// <summary>Puerto de entrada de los adjuntos (RF-08).</summary>
public interface IAdjuntosPuertoEntrada
{
    Task<SubidaAdjuntoDto> PrepararSubidaAsync(ContextoPeticion contexto, Guid kudoId, IntencionAdjuntoDto datos, CancellationToken ct);

    Task ConfirmarSubidaAsync(ContextoPeticion contexto, Guid kudoId, Guid adjuntoId, CancellationToken ct);

    Task<string> UrlDeDescargaAsync(ContextoPeticion contexto, Guid kudoId, Guid adjuntoId, CancellationToken ct);

    Task EliminarAsync(ContextoPeticion contexto, Guid kudoId, Guid adjuntoId, CancellationToken ct);
}

/// <summary>Puerto de entrada de las categorías (RF-06).</summary>
public interface ICategoriasPuertoEntrada
{
    Task<IReadOnlyList<CategoriaDto>> ActivasAsync(CancellationToken ct);

    Task<IReadOnlyList<CategoriaDto>> TodasAsync(ContextoPeticion contexto, CancellationToken ct);

    Task<CategoriaDto> CrearAsync(ContextoPeticion contexto, GuardarCategoriaDto datos, CancellationToken ct);

    Task ActualizarAsync(ContextoPeticion contexto, Guid id, GuardarCategoriaDto datos, CancellationToken ct);

    Task CambiarEstadoAsync(ContextoPeticion contexto, Guid id, bool activa, CancellationToken ct);
}
