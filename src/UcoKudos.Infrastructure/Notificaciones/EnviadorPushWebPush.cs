using System.Net;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using UcoKudos.Application.Notificaciones.Salida.Puertos;
using SuscripcionDelDominio = UcoKudos.Application.Notificaciones.Salida.Puertos.SuscripcionPush;

namespace UcoKudos.Infrastructure.Notificaciones;

/// <summary>
/// Adaptador real de Web Push con VAPID (ADR-0009).
///
/// Es el canal que llega **con la aplicación cerrada y el celular bloqueado**, que en un escenario
/// 90 % móvil es el caso normal. VAPID es un estándar abierto que los navegadores implementan de
/// forma nativa, así que no ata la solución a ningún servicio de terceros: por eso se descartó
/// Firebase.
///
/// Dos cosas que conviene tener claras:
///
///  - **La clave privada es un secreto; la pública no.** El cliente necesita la pública para
///    suscribirse. Con la privada, cualquiera podría enviar notificaciones a los usuarios de la
///    plataforma. Confundirlas es el error típico de Web Push (ADR-0018).
///
///  - **Una notificación aparece en la pantalla de bloqueo**, donde la lee cualquiera que tenga el
///    teléfono a la vista. Por eso el cuerpo lleva lo mínimo y nunca el contenido del Kudo.
/// </summary>
public sealed class EnviadorPushWebPush(OpcionesPush opciones, PushServiceClient cliente) : IEnviadorPush
{
    public async Task<ResultadoEnvio> EnviarAsync(
        SuscripcionDelDominio suscripcion, string titulo, string cuerpo, string? url, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(suscripcion);

        if (!opciones.EstaConfigurado)
            return ResultadoEnvio.Permanente("El canal de Web Push no está configurado.");

        var destino = new PushSubscription
        {
            Endpoint = suscripcion.Endpoint,
            Keys = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["p256dh"] = suscripcion.ClaveP256dh,
                ["auth"] = suscripcion.ClaveAuth
            }
        };

        var carga = JsonSerializer.Serialize(new CargaPush(titulo, cuerpo, url));
        var mensaje = new PushMessage(carga)
        {
            // Urgencia normal: un reconocimiento no es una alerta. Los navegadores pueden agrupar o
            // retrasar lo de urgencia baja, y lo de urgencia alta consume batería sin motivo.
            Urgency = PushMessageUrgency.Normal,

            // Si el navegador no puede entregarla en este plazo, la descarta en lugar de acumularla.
            // Un aviso de hace días no aporta, y además queda persistido en la plataforma.
            TimeToLive = (int)TimeSpan.FromHours(12).TotalSeconds
        };

        try
        {
            await cliente.RequestPushMessageDeliveryAsync(destino, mensaje, ct);
            return ResultadoEnvio.Enviado("webpush:" + Resumir(suscripcion.Endpoint));
        }
        catch (PushServiceClientException error)
        {
            return Clasificar(error);
        }
        catch (HttpRequestException error)
        {
            // Falla de red: pasajera. El evento vuelve a la cola.
            return ResultadoEnvio.Transitorio("Red: " + error.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ResultadoEnvio.Transitorio("Tiempo de espera agotado con el servicio de push.");
        }
    }

    /// <summary>
    /// Clasifica el error en transitorio o permanente, que es lo que permite al proceso de la
    /// bandeja decidir si reintenta (ADR-0005).
    ///
    /// El caso que importa es el <c>404</c> y el <c>410</c>: significan que la suscripción ya no
    /// existe, porque el usuario revocó el permiso o desinstaló la aplicación. Son **permanentes**, y
    /// el gateway las desactiva al recibirlos. Reintentarlas generaría un fallo en cada publicación
    /// futura.
    /// </summary>
    private static ResultadoEnvio Clasificar(PushServiceClientException error)
    {
        var detalle = "Push " + (int)error.StatusCode + ": " + error.Message;

        return error.StatusCode switch
        {
            HttpStatusCode.NotFound or HttpStatusCode.Gone => ResultadoEnvio.Permanente(detalle),
            HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests => ResultadoEnvio.Transitorio(detalle),
            >= HttpStatusCode.InternalServerError => ResultadoEnvio.Transitorio(detalle),
            _ => ResultadoEnvio.Permanente(detalle)
        };
    }

    /// <summary>
    /// Un trozo del endpoint para identificar el envío en los registros, sin guardarlo entero: el
    /// endpoint identifica un navegador concreto de una persona concreta.
    /// </summary>
    private static string Resumir(string endpoint) =>
        endpoint.Length <= 24 ? endpoint : endpoint[^24..];

    /// <summary>
    /// Lo que recibe el service worker. Deliberadamente escueto: una notificación se ve en la
    /// pantalla de bloqueo.
    /// </summary>
    private sealed record CargaPush(string Titulo, string Cuerpo, string? Url);
}

/// <summary>
/// Construye el cliente de Web Push con las credenciales VAPID.
///
/// El asunto es obligatorio en el estándar: identifica a quién contactar si el servicio de push
/// detecta un problema con los envíos de esta aplicación.
/// </summary>
public static class FabricaPushServiceClient
{
    public static PushServiceClient Crear(OpcionesPush opciones, HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(opciones);

        return new PushServiceClient(http)
        {
            DefaultAuthentication = new VapidAuthentication(opciones.ClavePublica, opciones.ClavePrivada)
            {
                Subject = opciones.Asunto
            }
        };
    }
}
