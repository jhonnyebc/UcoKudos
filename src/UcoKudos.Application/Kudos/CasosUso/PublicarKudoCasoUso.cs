using System.Text.Json;
using UcoKudos.Application.Comun;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Application.Kudos.Entrada.Dto;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Identidad.Repositorios;
using UcoKudos.Domain.Kudos;
using UcoKudos.Domain.Kudos.Repositorios;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.Application.Kudos.CasosUso;

/// <summary>
/// Publicar un Kudo: la funcionalidad obligatoria del enunciado (RF-07).
///
/// Los cinco pasos que pide el enunciado **no ocurren todos aquí**, y es deliberado. En este caso de
/// uso ocurren el paso 1 (persistencia) y el paso 5 (auditoría), dentro de una sola transacción. Los
/// pasos 2, 3 y 4 (correo, WhatsApp y push) los produce el proceso de notificaciones a partir del
/// evento que se deposita aquí.
///
/// La razón es doble (ADR-0005):
///
///  - Si los tres envíos fueran síncronos, el usuario esperaría a tres servicios externos en una red
///    móvil, que es el escenario del 90 % de los usuarios (DRV-001).
///  - Y un 503 de Meta haría fallar la publicación de un Kudo que no tiene nada de malo, lo que
///    contradice QA-DISP-01.
///
/// Lo que **sí** está garantizado es que no se pierde nada: el Kudo, su auditoría y su evento se
/// confirman juntos o no se confirma ninguno (QA-CONF-01).
/// </summary>
public sealed class PublicarKudoCasoUso(
    IKudoRepositorio kudos,
    ICategoriaRepositorio categorias,
    IPersonaRepositorio personas,
    IUnidadTrabajo unidadTrabajo,
    IBandejaSalida bandeja,
    ICacheConsultas cache,
    ServicioAuditoria auditoria,
    TimeProvider reloj)
{
    public async Task<KudoPublicadoDto> EjecutarAsync(ContextoPeticion contexto, PublicarKudoDto datos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(datos);

        var ahora = reloj.GetUtcNow();

        // 1. Validaciones de existencia y de estado. El dominio valida sus invariantes al construir;
        //    aquí se comprueba lo que depende de otras entidades.
        var destinatario = await personas.PorIdAsync(datos.DestinatarioId, ct)
                           ?? throw new ExcepcionDominio("El destinatario del Kudo no existe.");

        if (!destinatario.PuedeAcceder)
            throw new ExcepcionDominio("No se puede publicar un Kudo para una cuenta que no está activa.");

        var categoria = await categorias.PorIdAsync(datos.CategoriaId, ct)
                        ?? throw new ExcepcionDominio("La categoría indicada no existe.");

        if (!categoria.Activa)
            throw new ExcepcionDominio("La categoría " + categoria.Nombre + " ya no está disponible.");

        // 2. El dominio valida el resto: autor distinto del destinatario, contenido no vacío.
        var kudo = new Kudo(contexto.PersonaId, datos.DestinatarioId, datos.CategoriaId, datos.Contenido, ahora);
        kudos.Agregar(kudo);

        // 3. Auditoría (paso 5 del enunciado), en la misma transacción.
        await auditoria.RegistrarAsync(
            contexto,
            AccionesAuditoria.KudoPublicar,
            nameof(Kudo),
            kudo.Id,
            new { destinatarioId = kudo.DestinatarioId, categoriaId = kudo.CategoriaId, adjuntos = kudo.Adjuntos.Count },
            ahora,
            ct);

        // 4. El evento que disparará los pasos 2, 3 y 4. Se escribe en la MISMA transacción: es lo
        //    que cierra la ventana «commit exitoso, envío perdido».
        var autor = await personas.PorIdAsync(contexto.PersonaId, ct);
        var carga = JsonSerializer.Serialize(new CargaKudoPublicado(
            kudo.Id,
            kudo.AutorId,
            autor?.NombreCompleto ?? "Alguien",
            kudo.DestinatarioId,
            categoria.Nombre));

        bandeja.Programar(new EventoPendiente(
            EventoId: kudo.Id,
            TipoEvento: TiposEvento.KudoPublicado,
            Carga: carga,
            OcurridoEn: ahora,
            ContextoTraza: contexto.CorrelacionId));

        // 5. Una sola confirmación para los tres: Kudo, auditoría y evento.
        await unidadTrabajo.ConfirmarAsync(ct);

        // 6. Invalidación de la primera página del muro, después de confirmar (ADR-0010). Se hace por
        //    evento y no por expiración, así que un Kudo recién publicado aparece de inmediato.
        await cache.InvalidarGrupoAsync(GruposCache.Muro, ct);

        return new KudoPublicadoDto(kudo.Id, kudo.PublicadoEn);
    }
}

/// <summary>
/// Lo que viaja en el evento de la bandeja. Lleva el nombre del autor y de la categoría ya
/// resueltos para que el proceso de notificaciones no tenga que volver a consultarlos, y **no** lleva
/// el contenido del Kudo: puede ser extenso y es información de personas.
/// </summary>
public sealed record CargaKudoPublicado(
    Guid KudoId,
    Guid AutorId,
    string AutorNombre,
    Guid DestinatarioId,
    string CategoriaNombre);
