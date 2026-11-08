using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using UcoKudos.Application.Notificaciones.Salida.Puertos;

namespace UcoKudos.Infrastructure.Canal;

/// <summary>Configuración del canal de WhatsApp (ADR-0007).</summary>
public sealed class OpcionesWhatsApp
{
    public const string Seccion = "UcoKudos:WhatsApp";
    public const string ProveedorSimulado = "Simulado";
    public const string ProveedorMetaCloud = "MetaCloud";

    /// <summary><c>Simulado</c> (por omisión, sin red) o <c>MetaCloud</c>.</summary>
    public string Proveedor { get; set; } = ProveedorSimulado;

    /// <summary>Identificador del número en Meta. No es secreto.</summary>
    public string? PhoneNumberId { get; set; }

    public string UrlGraph { get; set; } = "https://graph.facebook.com";

    public string VersionGraph { get; set; } = "v23.0";

    /// <summary>
    /// SECRETO. Token de **usuario del sistema**, no el temporal del portal: ese caduca en horas y
    /// hace perder tiempo depurando un error que no existe (ADR-0007).
    /// </summary>
    public string? AccessToken { get; set; }

    public bool EsSimulado => string.Equals(Proveedor, ProveedorSimulado, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Adaptador de WhatsApp Business Cloud API (ADR-0007).
///
/// **Solo salida y siempre por plantilla.** El destinatario de un Kudo nunca escribió al negocio, así
/// que la plataforma está siempre fuera de la ventana de conversación de 24 horas y un mensaje de
/// texto libre sería rechazado por Meta.
///
/// No hay webhook, y eso es una decisión y no una carencia: elimina el punto de entrada público, la
/// verificación de firma y la bandeja de entrada idempotente. Es superficie que no existe en lugar de
/// superficie protegida.
/// </summary>
public sealed class WhatsAppMetaAdapter(OpcionesWhatsApp opciones, HttpClient http) : IWhatsAppGateway
{
    public async Task<ResultadoEnvio> EnviarPlantillaAsync(
        string telefonoE164,
        string plantilla,
        string idioma,
        IReadOnlyList<string> parametros,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parametros);

        if (string.IsNullOrWhiteSpace(opciones.AccessToken) || string.IsNullOrWhiteSpace(opciones.PhoneNumberId))
            return ResultadoEnvio.Permanente("El canal de WhatsApp no está configurado.");

        var cuerpo = new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = telefonoE164.TrimStart('+'),
            type = "template",
            template = new
            {
                name = plantilla,
                language = new { code = idioma },
                components = parametros.Count == 0
                    ? Array.Empty<object>()
                    : [new { type = "body", parameters = parametros.Select(p => new { type = "text", text = p }).ToArray() }]
            }
        };

        var ruta = opciones.UrlGraph.TrimEnd('/') + "/" + opciones.VersionGraph + "/" + opciones.PhoneNumberId + "/messages";

        using var peticion = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(cuerpo) };
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opciones.AccessToken);

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await http.SendAsync(peticion, ct);
        }
        catch (HttpRequestException error)
        {
            // Falla de red: transitorio. El evento vuelve a la cola.
            return ResultadoEnvio.Transitorio("Red: " + error.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ResultadoEnvio.Transitorio("Tiempo de espera agotado con la Graph API.");
        }

        using (respuesta)
        {
            var texto = await respuesta.Content.ReadAsStringAsync(ct);

            if (respuesta.IsSuccessStatusCode)
            {
                return IdMensaje(texto) is { } id
                    ? ResultadoEnvio.Enviado(id)
                    : ResultadoEnvio.Permanente("La Graph API no devolvió el identificador del mensaje.");
            }

            var detalle = "HTTP " + (int)respuesta.StatusCode + ": " + ErrorDeMeta(texto);

            // La clasificación que permite al proceso decidir si reintenta: tiempo de espera, límite
            // de tasa y errores del servidor son pasajeros. Un número inválido o una plantilla
            // rechazada, no: reintentarlos mil veces no los arregla.
            return respuesta.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                   || (int)respuesta.StatusCode >= 500
                ? ResultadoEnvio.Transitorio(detalle)
                : ResultadoEnvio.Permanente(detalle);
        }
    }

    private static string? IdMensaje(string cuerpo)
    {
        try
        {
            using var documento = JsonDocument.Parse(cuerpo);
            return documento.RootElement.TryGetProperty("messages", out var mensajes)
                   && mensajes.ValueKind == JsonValueKind.Array
                   && mensajes.GetArrayLength() > 0
                   && mensajes[0].TryGetProperty("id", out var id)
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ErrorDeMeta(string cuerpo)
    {
        try
        {
            using var documento = JsonDocument.Parse(cuerpo);
            if (documento.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var mensaje))
            {
                var codigo = error.TryGetProperty("code", out var c) ? c.ToString() : "?";
                return codigo + " " + mensaje.GetString();
            }
        }
        catch (JsonException)
        {
            // Si no es JSON, se devuelve el texto recortado.
        }

        return cuerpo.Length > 200 ? cuerpo[..200] : cuerpo;
    }
}

/// <summary>
/// Adaptador simulado del mismo puerto (ADR-0007).
///
/// Es lo que usan las pruebas automatizadas y el desarrollo sin red, y tiene dos beneficios
/// concretos: el pipeline puede probar el flujo completo de RF-07 sin tocar internet ni gastar
/// cuota, y la demostración se puede ensayar tantas veces como haga falta.
///
/// <see cref="FallarProximos"/> permite simular los errores del proveedor, que es cómo se prueba el
/// escenario QA-DISP-01 sin depender de que Meta se caiga.
/// </summary>
public sealed class WhatsAppSimuladoAdapter : IWhatsAppGateway
{
    private int _secuencia;

    /// <summary>Lo que se habría enviado, para que las pruebas lo comprueben.</summary>
    public ConcurrentQueue<(string Telefono, string Plantilla, IReadOnlyList<string> Parametros, string IdExterno)> Enviados { get; } = new();

    /// <summary>Resultados a devolver en los próximos envíos, en orden. Para probar los fallos.</summary>
    public ConcurrentQueue<ResultadoEnvio> FallarProximos { get; } = new();

    public Task<ResultadoEnvio> EnviarPlantillaAsync(
        string telefonoE164, string plantilla, string idioma, IReadOnlyList<string> parametros, CancellationToken ct)
    {
        if (FallarProximos.TryDequeue(out var fallo))
            return Task.FromResult(fallo);

        var id = "wamid.simulado." + Interlocked.Increment(ref _secuencia);
        Enviados.Enqueue((telefonoE164, plantilla, parametros, id));

        return Task.FromResult(ResultadoEnvio.Enviado(id));
    }
}
