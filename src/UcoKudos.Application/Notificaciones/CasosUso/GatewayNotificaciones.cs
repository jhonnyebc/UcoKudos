using System.Text.Json;
using UcoKudos.Application.Notificaciones.Catalogo;
using UcoKudos.Application.Notificaciones.Salida.Puertos;
using UcoKudos.Domain.Identidad.Repositorios;

namespace UcoKudos.Application.Notificaciones.CasosUso;

/// <summary>Canales por los que se puede avisar.</summary>
public enum Canal
{
    Correo = 1,
    WhatsApp = 2,
    TiempoReal = 3
}

/// <summary>
/// Política de canal por tipo de aviso (ADR-0006). Es una **tabla explícita** y no una cadena de
/// condicionales repartida por los casos de uso: así se ve de un vistazo qué le llega a quién.
/// </summary>
public static class PoliticaCanal
{
    private static readonly Dictionary<string, Canal[]> Politica = new(StringComparer.Ordinal)
    {
        // RF-07: los tres canales. Es la funcionalidad obligatoria.
        [ClavesMensaje.KudoRecibido] = [Canal.Correo, Canal.WhatsApp, Canal.TiempoReal],

        [ClavesMensaje.ReplicaEnTuKudo] = [Canal.TiempoReal],
        [ClavesMensaje.SolicitudAprobada] = [Canal.Correo, Canal.TiempoReal],
        [ClavesMensaje.SolicitudRechazada] = [Canal.Correo],
        [ClavesMensaje.ContenidoVetado] = [Canal.Correo, Canal.TiempoReal],
        // Iba por WhatsApp, que es lo natural para verificar un celular, y se cambió el 2026-11-09
        // por una restricción del proveedor: Meta no permite crear plantillas de categoría
        // Autenticación en una cuenta sin verificación comercial. El motivo completo está en el
        // catálogo, junto al mensaje. Sale por correo y queda como aviso dentro de la aplicación.
        [ClavesMensaje.CodigoVerificacion] = [Canal.Correo, Canal.TiempoReal]
    };

    public static IReadOnlyList<Canal> Para(string claveMensaje) =>
        Politica.TryGetValue(claveMensaje, out var canales)
            ? canales
            : throw new InvalidOperationException("No hay política de canal para el mensaje " + claveMensaje + ".");
}

/// <summary>Qué notificar y a quién. Los módulos emisores no construyen esto: lo hace el gateway.</summary>
/// <param name="EventoId">Clave de la idempotencia, junto con el destinatario y el canal.</param>
/// <param name="DestinatarioId">Identificador interno. **Nunca** un correo ni un teléfono.</param>
/// <param name="ClaveMensaje">Qué mensaje del catálogo.</param>
/// <param name="Variables">Valores de las variables del mensaje, en orden.</param>
/// <param name="Url">Enlace al contenido, si el aviso lleva uno.</param>
public sealed record SolicitudNotificacion(
    Guid EventoId,
    Guid DestinatarioId,
    string ClaveMensaje,
    IReadOnlyList<string> Variables,
    string? Url);

/// <summary>Cómo fue cada canal, para registrarlo y decidir si el evento se reintenta.</summary>
public sealed record ResultadoNotificacion(Canal Canal, ResultadoEnvio Resultado);

/// <summary>
/// Punto **único** de salida de las notificaciones (ADR-0006). Los módulos de negocio no conocen los
/// canales: expresan intención y el gateway decide.
///
/// Dos cosas que conviene tener claras al sustentar:
///
///  - **El destinatario llega como identificador interno**, nunca como correo o teléfono. El gateway
///    lo resuelve contra la base. Así una falla de autorización en un módulo no se convierte en un
///    envío a la dirección equivocada.
///
///  - **Los canales son independientes.** Si WhatsApp falla, el correo y el tiempo real salen igual:
///    un canal caído degrada el servicio, no lo interrumpe (QA-DISP-01).
/// </summary>
public sealed class GatewayNotificaciones(
    IPersonaRepositorio personas,
    IEnviadorCorreo correo,
    IWhatsAppGateway whatsApp,
    IEnviadorPush push,
    IAvisosEnVivo avisos,
    IRepositorioAvisos repositorioAvisos)
{
    /// <summary>Minutos que el código de verificación sigue siendo válido. Va en el texto del mensaje.</summary>
    public const int MinutosDeVigenciaDelCodigo = 10;

    public async Task<IReadOnlyList<ResultadoNotificacion>> NotificarAsync(
        SolicitudNotificacion solicitud,
        DateTimeOffset ahora,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        var destinatario = await personas.PorIdAsync(solicitud.DestinatarioId, ct);
        if (destinatario is null)
            return [];

        var mensaje = CatalogoMensajes.Por(solicitud.ClaveMensaje);
        var cuerpo = CatalogoMensajes.Formatear(mensaje, solicitud.Variables);
        var resultados = new List<ResultadoNotificacion>();

        foreach (var canal in PoliticaCanal.Para(solicitud.ClaveMensaje))
        {
            // Cada canal va en su propio intento: uno caído no cancela los demás.
            var resultado = canal switch
            {
                Canal.Correo => await EnviarCorreoAsync(destinatario.CorreoInstitucional, mensaje, cuerpo, ct),
                Canal.WhatsApp => await EnviarWhatsAppAsync(destinatario.CelularE164, mensaje, solicitud.Variables, ct),
                Canal.TiempoReal => await EnviarTiempoRealAsync(solicitud, mensaje, cuerpo, ahora, ct),
                _ => ResultadoEnvio.Permanente("Canal desconocido.")
            };

            resultados.Add(new ResultadoNotificacion(canal, resultado));
        }

        return resultados;
    }

    private async Task<ResultadoEnvio> EnviarCorreoAsync(string destino, Mensaje mensaje, string cuerpo, CancellationToken ct)
    {
        // El HTML lleva estilos en línea y ancho adaptado a pantalla de celular: el 90 % de los
        // destinatarios lo abrirá en el teléfono (DRV-001, ADR-0008).
        var html = PlantillaCorreo.Componer(mensaje.Titulo, cuerpo);
        return await correo.EnviarAsync(destino, mensaje.Asunto, html, cuerpo, ct);
    }

    private async Task<ResultadoEnvio> EnviarWhatsAppAsync(
        string telefono, Mensaje mensaje, IReadOnlyList<string> variables, CancellationToken ct)
    {
        return mensaje.PlantillaWhatsApp is null
            ? ResultadoEnvio.Permanente("El mensaje " + mensaje.Clave + " no tiene plantilla aprobada de WhatsApp.")
            : await whatsApp.EnviarPlantillaAsync(telefono, mensaje.PlantillaWhatsApp, "es", variables, ct);
    }

    /// <summary>
    /// Tiempo real con la política de preferencia de ADR-0009: si hay conexión abierta se entrega por
    /// ahí y **no** se envía Web Push, para no notificar dos veces lo mismo. Si no la hay, Web Push.
    /// Si tampoco hay suscripción, el aviso queda persistido y se ve al entrar.
    /// </summary>
    private async Task<ResultadoEnvio> EnviarTiempoRealAsync(
        SolicitudNotificacion solicitud, Mensaje mensaje, string cuerpo, DateTimeOffset ahora, CancellationToken ct)
    {
        // El aviso se persiste SIEMPRE, antes de intentar entregarlo: un canal en vivo es efímero.
        repositorioAvisos.AgregarAviso(
            solicitud.DestinatarioId,
            solicitud.ClaveMensaje,
            JsonSerializer.Serialize(solicitud.Variables),
            ahora);

        if (avisos.EstaConectado(solicitud.DestinatarioId))
        {
            var datos = new AvisoEnVivo(mensaje.Titulo, cuerpo, solicitud.Url);
            if (await avisos.EntregarAsync(solicitud.DestinatarioId, solicitud.ClaveMensaje, datos, ct))
                return ResultadoEnvio.Enviado("sse");
        }

        var suscripciones = await repositorioAvisos.SuscripcionesActivasAsync(solicitud.DestinatarioId, ct);
        if (suscripciones.Count == 0)
        {
            // No es un fallo: el aviso quedó guardado y lo verá al entrar.
            return ResultadoEnvio.Enviado("persistido");
        }

        var ultimo = ResultadoEnvio.Permanente("Sin suscripciones utilizables.");
        foreach (var suscripcion in suscripciones)
        {
            ultimo = await push.EnviarAsync(suscripcion, mensaje.Titulo, cuerpo, solicitud.Url, ct);

            // Una suscripción que el navegador ya rechazó se desactiva: si no, generaría un fallo
            // permanente en cada publicación futura.
            if (ultimo.Estado == EstadoEnvio.Permanente)
                await repositorioAvisos.DesactivarSuscripcionAsync(suscripcion.Id, ct);
        }

        return ultimo;
    }
}

/// <summary>Lo que viaja por la conexión en vivo hacia el cliente.</summary>
public sealed record AvisoEnVivo(string Titulo, string Cuerpo, string? Url);
