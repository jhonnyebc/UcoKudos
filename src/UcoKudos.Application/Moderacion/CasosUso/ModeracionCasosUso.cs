using System.Text.Json;
using UcoKudos.Application.Comun;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Application.Interaccion.Entrada.Dto;
using UcoKudos.Application.Notificaciones.CasosUso;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Identidad.Repositorios;
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
/// La bandeja de quien modera: ver los reportes pendientes y descartar los que no proceden.
///
/// Solo la ven los roles que pueden vetar (Rector y Coordinador Académico). Un reporte es una queja
/// de alguien, no una decisión: el contenido sigue visible hasta que alguien con autoridad lo veta.
/// </summary>
public sealed class GestionarReportesCasoUso(
    IKudoRepositorio kudos,
    IReplicaRepositorio replicas,
    IModeracionRepositorio moderacion,
    IPersonaRepositorio personas,
    IUnidadTrabajo unidadTrabajo,
    ServicioAuditoria auditoria,
    TimeProvider reloj)
{
    public async Task<IReadOnlyList<ReportePendienteDto>> PendientesAsync(ContextoPeticion contexto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        await ExigirModeradorAsync(contexto, "ver los reportes", Guid.Empty, ct);

        var resultado = new List<ReportePendienteDto>();

        foreach (var reporte in await moderacion.ReportesPendientesAsync(ct))
        {
            var (contenido, autorId, vetado) = reporte.TipoContenido == TipoContenido.Kudo
                ? await DatosDelKudoAsync(reporte.ContenidoId, ct)
                : await DatosDeLaReplicaAsync(reporte.ContenidoId, ct);

            // Lo que ya está vetado no necesita decisión, aunque conserve un reporte pendiente: por
            // ejemplo, una réplica que se ocultó en cascada al vetar el Kudo.
            if (contenido is null || vetado)
                continue;

            var reportante = await personas.PorIdAsync(reporte.PersonaId, ct);
            var autor = await personas.PorIdAsync(autorId, ct);

            resultado.Add(new ReportePendienteDto(
                reporte.Id,
                reporte.TipoContenido.ToString(),
                reporte.ContenidoId,
                reporte.Motivo,
                reportante?.NombreCompleto ?? "Persona desconocida",
                autor?.NombreCompleto ?? "Persona desconocida",
                contenido,
                reporte.PresentadoEn));
        }

        return resultado;
    }

    public async Task DescartarAsync(ContextoPeticion contexto, Guid reporteId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        await ExigirModeradorAsync(contexto, "descartar un reporte", reporteId, ct);

        var reporte = await moderacion.ReportePorIdAsync(reporteId, ct)
                      ?? throw new ExcepcionDominio("El reporte indicado no existe.");

        reporte.Resolver(seVeto: false);

        await auditoria.RegistrarAsync(
            contexto, AccionesAuditoria.ReporteDescartar, nameof(Reporte), reporte.Id,
            new { tipo = reporte.TipoContenido.ToString(), contenidoId = reporte.ContenidoId },
            reloj.GetUtcNow(), ct);

        await unidadTrabajo.ConfirmarAsync(ct);
    }

    private async Task<(string? Contenido, Guid AutorId, bool Vetado)> DatosDelKudoAsync(Guid id, CancellationToken ct)
    {
        var kudo = await kudos.PorIdAsync(id, ct);
        return kudo is null ? (null, Guid.Empty, false) : (kudo.Contenido, kudo.AutorId, kudo.Vetado);
    }

    private async Task<(string? Contenido, Guid AutorId, bool Vetado)> DatosDeLaReplicaAsync(Guid id, CancellationToken ct)
    {
        var replica = await replicas.PorIdAsync(id, ct);
        return replica is null ? (null, Guid.Empty, false) : (replica.Contenido, replica.AutorId, replica.Vetado);
    }

    private async Task ExigirModeradorAsync(ContextoPeticion contexto, string queIntento, Guid entidadId, CancellationToken ct)
    {
        if (JerarquiaInstitucional.PuedeVetar(contexto.Ambito))
            return;

        await auditoria.RegistrarAccesoRechazadoAsync(contexto, queIntento, entidadId, reloj.GetUtcNow(), ct);
        await unidadTrabajo.ConfirmarAsync(ct);

        throw new ExcepcionAutorizacion("El rol " + contexto.Rol.Nombre + " no modera contenido.");
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

        // Los reportes pendientes sobre este contenido quedan resueltos, y aceptados: se vetó. Sin
        // esto la bandeja de quien modera seguiría mostrando como pendiente algo que ya se decidió.
        foreach (var reporte in await moderacion.ReportesPendientesDeAsync(tipo, datos.ContenidoId, ct))
            reporte.Resolver(seVeto: true);

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

        // Se toma la lista de las que están visibles ANTES de vetarlas: contarlas después daba siempre
        // cero, y el resultado decía «1 contenido afectado» al vetar un Kudo con todo su hilo. Lo
        // encontró la primera prueba de punta a punta del veto.
        var visibles = (await replicas.HiloDeAsync(kudoId, ct)).Where(r => !r.Vetado).ToList();
        foreach (var replica in visibles)
            replica.Vetar();

        return (1 + visibles.Count, kudo.AutorId);
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
