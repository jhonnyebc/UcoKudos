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
/// La carga es en tres pasos, y **el archivo pasa por la API en las dos direcciones**:
///
///   1. **Intención**: la API valida lo que declara el cliente, genera la clave y reserva el adjunto.
///   2. **Contenido**: el cliente sube el archivo a la API, con su sesión.
///   3. **Confirmación**: la API lee los primeros bytes del objeto, detecta el tipo **real** y
///      calcula el hash.
///
/// La primera versión usaba URL prefirmadas contra el almacenamiento (ADR-0012), y falló en
/// producción: el servidor de objetos valida las firmas con un secreto fijo y público, así que
/// firmar con un secreto de verdad hacía que rechazara todas las subidas, y usar el público habría
/// dejado a cualquiera fabricar firmas válidas. Pasar por la API cuesta el ancho de banda de unos
/// pocos megabytes, acotados, y a cambio la autorización y la auditoría ocurren en cada subida y en
/// cada descarga, y el almacenamiento no tiene ninguna ruta pública.
///
/// Que el tipo se determine por el contenido y no por la extensión importa porque las dos las
/// controla quien sube el archivo.
/// </summary>
public sealed class AdjuntosCasosUso(
    IKudoRepositorio kudos,
    IAlmacenObjetos almacen,
    IUnidadTrabajo unidadTrabajo,
    ServicioAuditoria auditoria,
    TimeProvider reloj)
{
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

        await unidadTrabajo.ConfirmarAsync(ct);

        return new SubidaAdjuntoDto(adjunto.Id);
    }

    /// <summary>
    /// Recibe el contenido de un adjunto ya reservado y lo guarda. No lo da por bueno: eso lo hace la
    /// confirmación, que inspecciona lo que de verdad quedó guardado.
    ///
    /// **El tamaño se acota aquí, mientras se lee**, y no solo con lo que declaró el cliente: quien
    /// declara 1 KB puede mandar 1 GB. Se corta en cuanto se pasa del máximo, sin llegar a leer el
    /// resto, así que un cuerpo desmesurado no agota la memoria de la aplicación.
    /// </summary>
    public async Task GuardarContenidoAsync(
        ContextoPeticion contexto, Guid kudoId, Guid adjuntoId, Stream cuerpo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(cuerpo);

        var kudo = await kudos.PorIdAsync(kudoId, ct)
                   ?? throw new ExcepcionDominio("El Kudo indicado no existe.");

        if (kudo.AutorId != contexto.PersonaId)
            throw new ExcepcionAutorizacion("Solo el autor del Kudo puede subir sus adjuntos.");

        var adjunto = kudo.Adjuntos.FirstOrDefault(a => a.Id == adjuntoId)
                      ?? throw new ExcepcionDominio("Este Kudo no tiene el adjunto indicado.");

        // Un adjunto confirmado tiene su hash registrado: reemplazar el contenido lo dejaría
        // desmentido, y la trazabilidad de ADR-0012 dejaría de significar nada.
        if (adjunto.Confirmado)
            throw new ExcepcionDominio("Este adjunto ya está confirmado y su contenido no se puede reemplazar.");

        var contenido = await LeerAcotadoAsync(cuerpo, ct);

        if (contenido.Length == 0)
            throw new ExcepcionDominio("El archivo está vacío.");

        await almacen.GuardarAsync(adjunto.ClaveObjeto, contenido, ct);
    }

    private static async Task<byte[]> LeerAcotadoAsync(Stream cuerpo, CancellationToken ct)
    {
        using var memoria = new MemoryStream();
        var buffer = new byte[81_920];

        int leidos;
        while ((leidos = await cuerpo.ReadAsync(buffer, ct)) > 0)
        {
            if (memoria.Length + leidos > Adjunto.TamanoMaximoBytes)
                throw new ExcepcionDominio(
                    "El adjunto supera el máximo de " + (Adjunto.TamanoMaximoBytes / 1024 / 1024) + " MB.");

            memoria.Write(buffer, 0, leidos);
        }

        return memoria.ToArray();
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
    /// Entrega el archivo. La autorización ocurre **aquí**, y es lo que hace que cada descarga sea de
    /// alguien: el almacenamiento no sabe nada de la jerarquía ni tiene ruta pública.
    ///
    /// Cada descarga se audita, que es lo que permite responder «quién vio qué archivo y cuándo».
    /// </summary>
    public async Task<ArchivoAdjuntoDto> DescargarAsync(ContextoPeticion contexto, Guid kudoId, Guid adjuntoId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var ahora = reloj.GetUtcNow();
        var kudo = await kudos.PorIdAsync(kudoId, ct)
                   ?? throw new ExcepcionDominio("El Kudo indicado no existe.");

        if (kudo.Vetado)
            throw new ExcepcionAutorizacion("Este contenido fue retirado por moderación.");

        var adjunto = kudo.Adjuntos.FirstOrDefault(a => a.Id == adjuntoId && a.EsServible)
                      ?? throw new ExcepcionDominio("Este Kudo no tiene el adjunto indicado.");

        var contenido = await almacen.LeerAsync(adjunto.ClaveObjeto, ct)
                        ?? throw new ExcepcionDominio("El archivo no está disponible en el almacenamiento.");

        await auditoria.RegistrarAsync(
            contexto,
            AccionesAuditoria.AdjuntoDescargar,
            nameof(Adjunto),
            adjunto.Id,
            new { kudoId },
            ahora,
            ct);

        await unidadTrabajo.ConfirmarAsync(ct);

        // El tipo es el DETECTADO por el contenido al confirmar, nunca el que declaró quien subió el
        // archivo: es lo que se le dice al navegador, y un tipo mentiroso sería un vector de ataque.
        return new ArchivoAdjuntoDto(
            contenido, adjunto.TipoDetectado ?? "application/octet-stream", adjunto.NombreOriginal);
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

    /// <summary>
    /// Los adjuntos servibles de un Kudo. Solo metadatos: listar no da acceso a ningún archivo, y
    /// por eso no deja rastro de auditoría. El acceso es descargar, y eso sí se audita.
    /// </summary>
    public async Task<IReadOnlyList<AdjuntoDto>> DeKudoAsync(Guid kudoId, CancellationToken ct)
    {
        var kudo = await kudos.PorIdAsync(kudoId, ct);
        if (kudo is null)
            return [];

        return
        [
            .. kudo.Adjuntos
                .Where(a => a.EsServible)
                .Select(a => new AdjuntoDto(a.Id, a.NombreOriginal, a.TipoDetectado, a.TamanoBytes))
        ];
    }
}
