using UcoKudos.Application.Comun;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Application.Kudos.Entrada.Dto;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Kudos;
using UcoKudos.Domain.Kudos.Repositorios;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.Application.Kudos.CasosUso;

/// <summary>
/// Administración de categorías (RF-06). Solo Administrador, Rector y Coordinador Académico.
///
/// La comprobación la hace <see cref="JerarquiaInstitucional"/> y no un condicional aquí, para que
/// la regla esté en un solo sitio y se pueda probar sin levantar nada (ADR-0004). Y cuando falla, el
/// intento **queda auditado**: un acceso rechazado suele informar más que uno exitoso (QA-SEG-01).
/// </summary>
public sealed class CategoriasCasosUso(
    ICategoriaRepositorio categorias,
    IUnidadTrabajo unidadTrabajo,
    ICacheConsultas cache,
    ServicioAuditoria auditoria,
    TimeProvider reloj)
{
    /// <summary>
    /// Las categorías activas, para elegir al publicar. Van por caché porque cambian muy poco y se
    /// consultan en cada pantalla de publicación (ADR-0010).
    /// </summary>
    public async Task<IReadOnlyList<CategoriaDto>> ActivasAsync(CancellationToken ct)
    {
        const string clave = GruposCache.Categorias + ":activas";

        var enCache = await cache.ObtenerAsync<List<CategoriaDto>>(clave, ct);
        if (enCache is not null)
            return enCache;

        var lista = (await categorias.ActivasAsync(ct)).Select(Convertir).ToList();
        await cache.GuardarAsync(clave, lista, ct);
        return lista;
    }

    /// <summary>Todas, incluidas las inactivas. Es la vista de administración.</summary>
    public async Task<IReadOnlyList<CategoriaDto>> TodasAsync(ContextoPeticion contexto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        await ExigirPermisoAsync(contexto, "consultar todas las categorías", Guid.Empty, ct);

        return [.. (await categorias.TodasAsync(ct)).Select(Convertir)];
    }

    public async Task<CategoriaDto> CrearAsync(ContextoPeticion contexto, GuardarCategoriaDto datos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(datos);

        var ahora = reloj.GetUtcNow();
        await ExigirPermisoAsync(contexto, "crear una categoría", Guid.Empty, ct);

        if (await categorias.ExisteNombreAsync(datos.Nombre, ct))
            throw new ExcepcionDominio("Ya existe una categoría con el nombre " + datos.Nombre + ".");

        var categoria = new Categoria(datos.Nombre, datos.Descripcion, contexto.PersonaId, ahora);
        categorias.Agregar(categoria);

        await auditoria.RegistrarAsync(
            contexto, AccionesAuditoria.CategoriaCrear, nameof(Categoria), categoria.Id,
            new { nombre = categoria.Nombre }, ahora, ct);

        await unidadTrabajo.ConfirmarAsync(ct);
        await cache.InvalidarGrupoAsync(GruposCache.Categorias, ct);

        return Convertir(categoria);
    }

    public async Task ActualizarAsync(ContextoPeticion contexto, Guid id, GuardarCategoriaDto datos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(datos);

        var ahora = reloj.GetUtcNow();
        await ExigirPermisoAsync(contexto, "actualizar una categoría", id, ct);

        var categoria = await categorias.PorIdAsync(id, ct)
                        ?? throw new ExcepcionDominio("La categoría indicada no existe.");

        categoria.Renombrar(datos.Nombre);
        categoria.CambiarDescripcion(datos.Descripcion);

        // Se registra QUÉ cambió, no los valores anteriores: la auditoría es permanente y no debe
        // acumular datos que no hacen falta (ADR-0013).
        await auditoria.RegistrarAsync(
            contexto, AccionesAuditoria.CategoriaActualizar, nameof(Categoria), id,
            new { campos = new[] { "nombre", "descripcion" } }, ahora, ct);

        await unidadTrabajo.ConfirmarAsync(ct);
        await cache.InvalidarGrupoAsync(GruposCache.Categorias, ct);
    }

    /// <summary>
    /// Desactiva en lugar de borrar: los Kudos ya publicados conservan su categoría, así que borrarla
    /// dejaría reconocimientos sin clasificar.
    /// </summary>
    public async Task CambiarEstadoAsync(ContextoPeticion contexto, Guid id, bool activa, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var ahora = reloj.GetUtcNow();
        await ExigirPermisoAsync(contexto, activa ? "activar una categoría" : "desactivar una categoría", id, ct);

        var categoria = await categorias.PorIdAsync(id, ct)
                        ?? throw new ExcepcionDominio("La categoría indicada no existe.");

        if (activa)
            categoria.Activar();
        else
            categoria.Desactivar();

        await auditoria.RegistrarAsync(
            contexto, AccionesAuditoria.CategoriaDesactivar, nameof(Categoria), id, new { activa }, ahora, ct);

        await unidadTrabajo.ConfirmarAsync(ct);
        await cache.InvalidarGrupoAsync(GruposCache.Categorias, ct);
    }

    /// <summary>
    /// Comprueba el permiso y, si falta, **audita el intento** antes de lanzar. Es la mitad que se
    /// suele olvidar: sin ella no habría forma de detectar a alguien tanteando permisos.
    /// </summary>
    private async Task ExigirPermisoAsync(ContextoPeticion contexto, string queIntento, Guid entidadId, CancellationToken ct)
    {
        if (JerarquiaInstitucional.PuedeAdministrarCategorias(contexto.Ambito))
            return;

        var ahora = reloj.GetUtcNow();
        await auditoria.RegistrarAccesoRechazadoAsync(contexto, queIntento, entidadId, ahora, ct);
        await unidadTrabajo.ConfirmarAsync(ct);

        throw new ExcepcionAutorizacion("El rol " + contexto.Rol.Nombre + " no administra categorías.");
    }

    private static CategoriaDto Convertir(Categoria categoria) =>
        new(categoria.Id, categoria.Nombre, categoria.Descripcion, categoria.Activa);
}
