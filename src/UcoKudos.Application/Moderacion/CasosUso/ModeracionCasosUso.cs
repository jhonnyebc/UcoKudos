using System.Text.Json;
using UcoKudos.Application.Comun;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Application.Interaccion.Entrada.Dto;
using UcoKudos.Application.Notificaciones.CasosUso;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Interaccion;
using UcoKudos.Domain.Interaccion.Repositorios;
using UcoKudos.Domain.Kudos.Repositorios;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.Application.Moderacion.CasosUso;

/// <summary>
/// Reportar contenido (RF-11). Lo puede hacer **cualquier** usuario, que es lo que el enunciado dice
/// y lo que distingue reportar de vetar.
/// </summary>
public sealed class ReportarContenidoCasoUso(
    IKudoRepositorio kudos,
    IReplicaRepositorio replicas,
    IModeracionRepositorio moderacion,
    IUnidadTrabajo unidadTrabajo,
    ServicioAuditoria auditoria,
    TimeProvider reloj)
{
    public async Task EjecutarAsync(ContextoPeticion contexto, ReportarDto datos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(datos);

        var ahora = reloj.GetUtcNow();
        var tipo = Enum.Parse<TipoContenido>(datos.TipoContenido, ignoreCase: true);

        var existe = tipo == TipoContenido.Kudo
            ? await kudos.PorIdAsync(datos.ContenidoId, ct) is not null
            : await replicas.PorIdAsync(datos.ContenidoId, ct) is not null;

        if (!existe)
            throw new ExcepcionDominio("El contenido que intenta reportar no existe.");

        moderacion.AgregarReporte(new Reporte(contexto.PersonaId, tipo, datos.ContenidoId, datos.Motivo, ahora));

        await auditoria.RegistrarAsync(
            contexto, AccionesAuditoria.ContenidoReportar, tipo.ToString(), datos.ContenidoId,
            new { motivo = datos.Motivo }, ahora, ct);

        await unidadTrabajo.ConfirmarAsync(ct);
    }
}

/// <summary>
/// Vetar contenido (RF-12). Solo Rector y Coordinador Académico: el Administrador del Sistema **no**,
/// aunque sea el rol más alto, porque moderar es una atribución académica y no técnica.
///
/// Vetar una réplica oculta también a sus descendientes. Es una sola operación gracias a la ruta
/// materializada (ADR-0011): no tendría sentido ocultar un comentario y dejar visibles las respuestas
/// que lo citan.
/// </summary>
public sealed class VetarContenidoCasoUso(
    IKudoRepositorio kudos,
    IReplicaRepositorio replicas,
    IModeracionRepositorio moderacion,
    IUnidadTrabajo unidadTrabajo,
    IBandejaSalida bandeja,
    ICacheConsultas cache,
    ServicioAuditoria auditoria,
    TimeProvider reloj)
{
    public async Task<ResultadoVetoDto> EjecutarAsync(ContextoPeticion contexto, VetarDto datos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(datos);

        var ahora = reloj.GetUtcNow();
        var tipo = Enum.Parse<TipoContenido>(datos.TipoContenido, ignoreCase: true);

        await ExigirPuedeVetarAsync(contexto, datos.ContenidoId, ct);

        var (afectados, autorId) = tipo == TipoContenido.Kudo
            ? await VetarKudoAsync(datos.ContenidoId, ct)
            : await VetarReplicaAsync(datos.ContenidoId, ct);

        moderacion.AgregarVeto(new Veto(contexto.PersonaId, tipo, datos.ContenidoId, datos.Motivo, afectados, ahora));

        await auditoria.RegistrarAsync(
            contexto, AccionesAuditoria.ContenidoVetar, tipo.ToString(), datos.ContenidoId,
            new { motivo = datos.Motivo, afectados }, ahora, ct);

        // Se avisa al autor de que su contenido fue retirado, y por qué.
        bandeja.Programar(new EventoPendiente(
            EventoId: Guid.CreateVersion7(ahora),
            TipoEvento: TiposEvento.ContenidoVetado,
            Carga: JsonSerializer.Serialize(new CargaContenidoVetado(
                datos.ContenidoId, tipo.ToString(), autorId, datos.Motivo)),
            OcurridoEn: ahora,
            ContextoTraza: contexto.CorrelacionId));

        await unidadTrabajo.ConfirmarAsync(ct);
        await cache.InvalidarGrupoAsync(GruposCache.Muro, ct);

        return new ResultadoVetoDto(afectados, afectados > 1);
    }

    /// <summary>
    /// Vetar un Kudo oculta el Kudo y **todo** su hilo: las réplicas de un Kudo retirado no tienen
    /// sentido por sí solas.
    /// </summary>
    private async Task<(int Afectados, Guid AutorId)> VetarKudoAsync(Guid kudoId, CancellationToken ct)
    {
        var kudo = await kudos.PorIdAsync(kudoId, ct)
                   ?? throw new ExcepcionDominio("El Kudo indicado no existe.");

        kudo.Vetar();

        var hilo = await replicas.HiloDeAsync(kudoId, ct);
        foreach (var replica in hilo.Where(r => !r.Vetado))
            replica.Vetar();

        return (1 + hilo.Count(r => !r.Vetado), kudo.AutorId);
    }

    /// <summary>
    /// Vetar una réplica oculta sus descendientes buscando por prefijo de ruta: una sola operación,
    /// por profundo que sea el subárbol.
    /// </summary>
    private async Task<(int Afectados, Guid AutorId)> VetarReplicaAsync(Guid replicaId, CancellationToken ct)
    {
        var replica = await replicas.PorIdAsync(replicaId, ct)
                      ?? throw new ExcepcionDominio("La réplica indicada no existe.");

        var afectados = await replicas.VetarEnCascadaAsync(replica.RutaMaterializada, ct);

        // El contador del Kudo se pone al día, porque el hilo visible acaba de encogerse.
        var kudo = await kudos.PorIdAsync(replica.KudoId, ct);
        if (kudo is not null)
            kudo.FijarTotalReplicas(Math.Max(0, kudo.TotalReplicas - afectados));

        return (afectados, replica.AutorId);
    }

    private async Task ExigirPuedeVetarAsync(ContextoPeticion contexto, Guid contenidoId, CancellationToken ct)
    {
        if (JerarquiaInstitucional.PuedeVetar(contexto.Ambito))
            return;

        var ahora = reloj.GetUtcNow();
        await auditoria.RegistrarAccesoRechazadoAsync(contexto, "vetar contenido", contenidoId, ahora, ct);
        await unidadTrabajo.ConfirmarAsync(ct);

        throw new ExcepcionAutorizacion("El rol " + contexto.Rol.Nombre + " no puede vetar contenido.");
    }
}
