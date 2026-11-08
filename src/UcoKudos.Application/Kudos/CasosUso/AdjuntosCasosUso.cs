using UcoKudos.Application.Comun;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Application.Kudos.Entrada.Dto;
using UcoKudos.Application.Notificaciones.Salida.Puertos;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Kudos;
using UcoKudos.Domain.Kudos.Repositorios;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.Application.Kudos.CasosUso;

/// <summary>
/// Adjuntos de un Kudo (RF-08, DRV-004), con las cuatro estrategias de ADR-0012.
///
/// La carga es en dos pasos a propósito:
///
///   1. **Intención**: la API valida, genera la clave y devuelve una URL prefirmada.
///   2. El cliente sube el archivo **directo al almacenamiento**, sin pasar por la API.
///   3. **Confirmación**: la API lee los primeros bytes del objeto, detecta el tipo **real** y
///      calcula el hash.
///
/// Que los megabytes no atraviesen la aplicación es lo que importa en la red móvil del 90 % de los
/// usuarios. Y que el tipo se determine por el contenido y no por la extensión importa porque las
/// dos las controla quien sube el archivo.
/// </summary>
public sealed class AdjuntosCasosUso(
    IKudoRepositorio kudos,
    IAlmacenObjetos almacen,
    IUnidadTrabajo unidadTrabajo,
    ServicioAuditoria auditoria,
    TimeProvider reloj)
{
    private static readonly TimeSpan ValidezDeSubida = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Vida de la URL de descarga. Corta a propósito: compartir el enlace fuera no debe dar acceso
    /// permanente (QA-SEG-02).
    /// </summary>
    private static readonly TimeSpan ValidezDeDescarga = TimeSpan.FromMinutes(5);

    public async Task<SubidaAdjuntoDto> PrepararSubidaAsync(
        ContextoPeticion contexto, Guid kudoId, IntencionAdjuntoDto datos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(datos);

        var ahora = reloj.GetUtcNow();
        var kudo = await kudos.PorIdAsync(kudoId, ct)
                   ?? throw new ExcepcionDominio("El Kudo indicado no existe.");

        if (kudo.AutorId != contexto.PersonaId)
            throw new ExcepcionAutorizacion("Solo el autor del Kudo puede adjuntarle archivos.");

        // Validación temprana con lo que declara el cliente, para no hacerle subir en vano un archivo
        // que se va a rechazar. NO se confía en ella: al confirmar se comprueba el contenido real.
        if (!Adjunto.TiposPermitidos.Contains(datos.TipoDeclarado))
            throw new ExcepcionDominio("El tipo de archivo " + datos.TipoDeclarado + " no está permitido.");

        if (datos.TamanoDeclarado > Adjunto.TamanoMaximoBytes)
            throw new ExcepcionDominio("El adjunto supera el máximo de " + (Adjunto.TamanoMaximoBytes / 1024 / 1024) + " MB.");

        // Clave generada, nunca el nombre del usuario: un nombre que controla quien sube el archivo
        // es un vector de recorrido de rutas y de colisiones.
        var clave = "kudos/" + kudoId.ToString("N") + "/" + Guid.CreateVersion7(ahora).ToString("N");

        var adjunto = kudo.AgregarAdjunto(clave, datos.NombreArchivo, contexto.PersonaId, ahora);
        var url = await almacen.UrlDeSubidaAsync(clave, ValidezDeSubida, ct);

        await unidadTrabajo.ConfirmarAsync(ct);

        return new SubidaAdjuntoDto(adjunto.Id, url, (int)ValidezDeSubida.TotalMinutes);
    }

    /// <summary>
    /// Confirma la carga leyendo el objeto. Aquí se rechaza un archivo cuyo tipo real no está
    /// permitido, aunque la extensión dijera otra cosa.
    /// </summary>
    public async Task ConfirmarSubidaAsync(ContextoPeticion contexto, Guid kudoId, Guid adjuntoId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var ahora = reloj.GetUtcNow();
        var kudo = await kudos.PorIdAsync(kudoId, ct)
                   ?? throw new ExcepcionDominio("El Kudo indicado no existe.");

        if (kudo.AutorId != contexto.PersonaId)
            throw new ExcepcionAutorizacion("Solo el autor del Kudo puede confirmar sus adjuntos.");

        var adjunto = kudo.Adjuntos.FirstOrDefault(a => a.Id == adjuntoId)
                      ?? throw new ExcepcionDominio("Este Kudo no tiene el adjunto indicado.");

        var datos = await almacen.InspeccionarAsync(adjunto.ClaveObjeto, ct)
                    ?? throw new ExcepcionDominio("El archivo no llegó al almacenamiento.");

        // El dominio vuelve a validar tipo, tamaño y hash sobre los datos REALES del objeto.
        adjunto.Confirmar(datos.TipoDetectado, datos.TamanoBytes, datos.HashContenido);

        await auditoria.RegistrarAsync(
            contexto,
            AccionesAuditoria.AdjuntoCargar,
            nameof(Adjunto),
            adjunto.Id,
            new { kudoId, tipo = datos.TipoDetectado, tamano = datos.TamanoBytes, hash = datos.HashContenido },
            ahora,
            ct);

        await unidadTrabajo.ConfirmarAsync(ct);
    }

    /// <summary>
    /// Enlace de descarga. La autorización ocurre **aquí**, antes de firmar: el almacenamiento no
    /// sabe nada de la jerarquía, solo honra una firma.
    ///
    /// Cada descarga se audita, que es lo que permite responder «quién vio qué archivo y cuándo».
    /// </summary>
    public async Task<string> UrlDeDescargaAsync(ContextoPeticion contexto, Guid kudoId, Guid adjuntoId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var ahora = reloj.GetUtcNow();
        var kudo = await kudos.PorIdAsync(kudoId, ct)
                   ?? throw new ExcepcionDominio("El Kudo indicado no existe.");

        if (kudo.Vetado)
            throw new ExcepcionAutorizacion("Este contenido fue retirado por moderación.");

        var adjunto = kudo.Adjuntos.FirstOrDefault(a => a.Id == adjuntoId && a.EsServible)
                      ?? throw new ExcepcionDominio("Este Kudo no tiene el adjunto indicado.");

        var url = await almacen.UrlDeDescargaAsync(adjunto.ClaveObjeto, ValidezDeDescarga, ct);

        await auditoria.RegistrarAsync(
            contexto,
            AccionesAuditoria.AdjuntoDescargar,
            nameof(Adjunto),
            adjunto.Id,
            new { kudoId },
            ahora,
            ct);

        await unidadTrabajo.ConfirmarAsync(ct);

        return url;
    }

    /// <summary>
    /// Elimina un adjunto. Marca en lugar de borrar: un borrado accidental es reversible y la
    /// limpieza definitiva del objeto es un proceso aparte (ADR-0012).
    /// </summary>
    public async Task EliminarAsync(ContextoPeticion contexto, Guid kudoId, Guid adjuntoId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var ahora = reloj.GetUtcNow();
        var kudo = await kudos.PorIdAsync(kudoId, ct)
                   ?? throw new ExcepcionDominio("El Kudo indicado no existe.");

        if (kudo.AutorId != contexto.PersonaId)
            throw new ExcepcionAutorizacion("Solo el autor del Kudo puede eliminar sus adjuntos.");

        kudo.QuitarAdjunto(adjuntoId);

        await auditoria.RegistrarAsync(
            contexto, AccionesAuditoria.AdjuntoEliminar, nameof(Adjunto), adjuntoId, new { kudoId }, ahora, ct);

        await unidadTrabajo.ConfirmarAsync(ct);
    }

    /// <summary>Los adjuntos servibles de un Kudo, cada uno con su enlace de vida corta.</summary>
    public async Task<IReadOnlyList<AdjuntoDto>> DeKudoAsync(Guid kudoId, CancellationToken ct)
    {
        var kudo = await kudos.PorIdAsync(kudoId, ct);
        if (kudo is null)
            return [];

        var resultado = new List<AdjuntoDto>();
        foreach (var adjunto in kudo.Adjuntos.Where(a => a.EsServible))
        {
            var url = await almacen.UrlDeDescargaAsync(adjunto.ClaveObjeto, ValidezDeDescarga, ct);
            resultado.Add(new AdjuntoDto(adjunto.Id, adjunto.NombreOriginal, adjunto.TipoDetectado, adjunto.TamanoBytes, url));
        }

        return resultado;
    }
}
