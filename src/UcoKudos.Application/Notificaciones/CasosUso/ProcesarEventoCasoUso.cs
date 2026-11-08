using System.Text.Json;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Application.Kudos.CasosUso;
using UcoKudos.Application.Notificaciones.Catalogo;
using UcoKudos.Application.Notificaciones.Entrada.Puertos;
using UcoKudos.Domain.Auditoria;

namespace UcoKudos.Application.Notificaciones.CasosUso;

/// <summary>
/// Convierte un evento de la bandeja en notificaciones. Es la segunda mitad del flujo obligatorio:
/// los pasos 2, 3 y 4 del enunciado (correo, WhatsApp y push) ocurren aquí, no en la transacción que
/// publicó el Kudo.
///
/// Lo llama el proceso en segundo plano, que es quien se encarga de tomar el evento con bloqueo, de
/// reintentar y de marcarlo procesado. Este caso de uso solo traduce evento a avisos, lo que lo hace
/// probable sin levantar el proceso.
/// </summary>
public sealed class ProcesarEventoCasoUso(
    GatewayNotificaciones gateway,
    IRegistroNotificaciones registro,
    ServicioAuditoria auditoria,
    TimeProvider reloj) : INotificacionesPuertoEntrada
{
    public async Task<bool> ProcesarAsync(
        Guid eventoId, string tipoEvento, string carga, DateTimeOffset ocurridoEn, string? correlacionId, CancellationToken ct)
    {
        var solicitud = Traducir(eventoId, tipoEvento, carga);
        if (solicitud is null)
            return false;

        var ahora = reloj.GetUtcNow();

        // Idempotencia (QA-CONF-02): si este evento ya se notificó a este destinatario, no se repite.
        // La garantía real la da un índice único sobre evento, destinatario y canal; esta consulta
        // evita el trabajo y el error cuando el reproceso es el caso normal.
        if (await registro.YaNotificadoAsync(eventoId, solicitud.DestinatarioId, ct))
            return false;

        var resultados = await gateway.NotificarAsync(solicitud, ahora, ct);

        foreach (var resultado in resultados)
        {
            await registro.RegistrarAsync(
                eventoId,
                solicitud.DestinatarioId,
                resultado.Canal.ToString(),
                solicitud.ClaveMensaje,
                resultado.Resultado,
                ahora,
                ct);

            // Cada envío se audita, bien o mal. Es la evidencia que pide la entrega, y permite
            // mostrarla desde la propia plataforma y no solo desde el celular.
            await auditoria.RegistrarDelSistemaAsync(
                resultado.Resultado.FueBien ? AccionesAuditoria.NotificacionEnviar : AccionesAuditoria.NotificacionFallar,
                "Notificacion",
                eventoId,
                new
                {
                    canal = resultado.Canal.ToString(),
                    destinatarioId = solicitud.DestinatarioId,
                    clave = solicitud.ClaveMensaje,
                    estado = resultado.Resultado.Estado.ToString(),
                    idExterno = resultado.Resultado.IdExterno,
                    error = resultado.Resultado.Error
                },
                correlacionId,
                ahora,
                ct);
        }

        // Si algún canal falló de forma transitoria, el proceso devolverá el evento a la cola.
        return resultados.Any(r => r.Resultado.SeReintenta) == false;
    }

    /// <summary>
    /// Traduce el evento al aviso que corresponde. Es el único lugar donde se decide qué mensaje
    /// produce cada evento; el canal lo decide la política del gateway.
    /// </summary>
    private static SolicitudNotificacion? Traducir(Guid eventoId, string tipoEvento, string carga) => tipoEvento switch
    {
        TiposEvento.KudoPublicado => DeKudoPublicado(eventoId, carga),
        TiposEvento.ReplicaPublicada => DeReplicaPublicada(eventoId, carga),
        TiposEvento.SolicitudAprobada => DeSolicitud(eventoId, carga, ClavesMensaje.SolicitudAprobada),
        TiposEvento.SolicitudRechazada => DeSolicitud(eventoId, carga, ClavesMensaje.SolicitudRechazada),
        TiposEvento.ContenidoVetado => DeContenidoVetado(eventoId, carga),
        TiposEvento.VerificacionCelularSolicitada => DeVerificacion(eventoId, carga),
        _ => null
    };

    private static SolicitudNotificacion? DeKudoPublicado(Guid eventoId, string carga)
    {
        var datos = JsonSerializer.Deserialize<CargaKudoPublicado>(carga);
        return datos is null
            ? null
            : new SolicitudNotificacion(
                eventoId,
                datos.DestinatarioId,
                ClavesMensaje.KudoRecibido,
                [datos.AutorNombre, datos.CategoriaNombre],
                "/kudos/" + datos.KudoId.ToString("N"));
    }

    private static SolicitudNotificacion? DeReplicaPublicada(Guid eventoId, string carga)
    {
        var datos = JsonSerializer.Deserialize<CargaReplicaPublicada>(carga);
        return datos is null
            ? null
            : new SolicitudNotificacion(
                eventoId,
                datos.DestinatarioId,
                ClavesMensaje.ReplicaEnTuKudo,
                [datos.AutorNombre],
                "/kudos/" + datos.KudoId.ToString("N"));
    }

    private static SolicitudNotificacion? DeSolicitud(Guid eventoId, string carga, string clave)
    {
        var datos = JsonSerializer.Deserialize<CargaSolicitud>(carga);
        if (datos is null)
            return null;

        IReadOnlyList<string> variables = clave == ClavesMensaje.SolicitudAprobada
            ? [datos.RolNombre]
            : [datos.RolNombre, datos.Motivo ?? "sin motivo registrado"];

        return new SolicitudNotificacion(eventoId, datos.PersonaId, clave, variables, "/");
    }

    private static SolicitudNotificacion? DeContenidoVetado(Guid eventoId, string carga)
    {
        var datos = JsonSerializer.Deserialize<CargaContenidoVetado>(carga);
        return datos is null
            ? null
            : new SolicitudNotificacion(eventoId, datos.AutorId, ClavesMensaje.ContenidoVetado, [datos.Motivo], null);
    }

    private static SolicitudNotificacion? DeVerificacion(Guid eventoId, string carga)
    {
        var datos = JsonSerializer.Deserialize<CargaVerificacion>(carga);
        return datos is null
            ? null
            : new SolicitudNotificacion(
                eventoId,
                datos.PersonaId,
                ClavesMensaje.CodigoVerificacion,
                [datos.Codigo, GatewayNotificaciones.MinutosDeVigenciaDelCodigo.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                null);
    }
}

/// <summary>Carga del evento de réplica publicada.</summary>
public sealed record CargaReplicaPublicada(Guid ReplicaId, Guid KudoId, Guid AutorId, string AutorNombre, Guid DestinatarioId);

/// <summary>Carga de los eventos de solicitud aprobada o rechazada.</summary>
public sealed record CargaSolicitud(Guid PersonaId, string RolNombre, string? Motivo);

/// <summary>Carga del evento de contenido vetado.</summary>
public sealed record CargaContenidoVetado(Guid ContenidoId, string TipoContenido, Guid AutorId, string Motivo);

/// <summary>Carga del evento de verificación de celular.</summary>
public sealed record CargaVerificacion(Guid PersonaId, string Codigo);
