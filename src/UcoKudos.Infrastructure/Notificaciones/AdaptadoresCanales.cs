using System.Collections.Concurrent;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using UcoKudos.Application.Notificaciones.Entrada.Puertos;
using UcoKudos.Application.Notificaciones.Salida.Puertos;
using UcoKudos.Infrastructure.Persistencia;
using UcoKudos.Infrastructure.Persistencia.Outbox;

namespace UcoKudos.Infrastructure.Notificaciones;

/// <summary>Configuración del canal de correo (ADR-0008).</summary>
public sealed class OpcionesCorreo
{
    public const string Seccion = "UcoKudos:Correo";
    public const string ProveedorSimulado = "Simulado";
    public const string ProveedorSmtp = "Smtp";

    public string Proveedor { get; set; } = ProveedorSimulado;

    public string Host { get; set; } = "localhost";

    public int Puerto { get; set; } = 1025;

    public string Remitente { get; set; } = "no-responder@ucokudos.local";

    public string? Usuario { get; set; }

    /// <summary>SECRETO.</summary>
    public string? Password { get; set; }

    /// <summary>
    /// En el contenedor de captura local no hay TLS; con un proveedor real, sí. Se configura en lugar
    /// de deducirse, para que nadie acabe enviando credenciales en claro sin darse cuenta.
    /// </summary>
    public bool UsarTls { get; set; }

    public bool EsSimulado => string.Equals(Proveedor, ProveedorSimulado, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Adaptador de correo por SMTP (ADR-0008).
///
/// El puerto habla SMTP y no la API propietaria de un proveedor: SMTP es un protocolo estándar, así
/// que cambiar de proveedor es cambiar tres variables de entorno y no escribir código. Es la forma
/// más literal de QA-MANT-01.
/// </summary>
public sealed class EnviadorCorreoSmtp(OpcionesCorreo opciones) : IEnviadorCorreo
{
    public async Task<ResultadoEnvio> EnviarAsync(
        string destino, string asunto, string cuerpoHtml, string cuerpoTexto, CancellationToken ct)
    {
        try
        {
            using var cliente = new SmtpClient(opciones.Host, opciones.Puerto) { EnableSsl = opciones.UsarTls };

            if (!string.IsNullOrWhiteSpace(opciones.Usuario))
                cliente.Credentials = new System.Net.NetworkCredential(opciones.Usuario, opciones.Password);

            using var mensaje = new MailMessage(opciones.Remitente, destino)
            {
                Subject = asunto,
                Body = cuerpoHtml,
                IsBodyHtml = true
            };

            // Alternativa en texto plano: algunos clientes la prefieren y los filtros de correo no
            // deseado desconfían de un mensaje que solo trae HTML.
            mensaje.AlternateViews.Add(
                AlternateView.CreateAlternateViewFromString(cuerpoTexto, null, "text/plain"));

            await cliente.SendMailAsync(mensaje, ct);

            // SMTP no devuelve identificador de mensaje: lo que se sabe es que el servidor aceptó.
            return ResultadoEnvio.Enviado("smtp:aceptado");
        }
        catch (SmtpException error)
        {
            // Un rechazo temporal del servidor se reintenta; un buzón inexistente, no.
            return error.StatusCode is SmtpStatusCode.MailboxBusy
                   or SmtpStatusCode.ServiceNotAvailable
                   or SmtpStatusCode.TransactionFailed
                ? ResultadoEnvio.Transitorio("SMTP " + error.StatusCode + ": " + error.Message)
                : ResultadoEnvio.Permanente("SMTP " + error.StatusCode + ": " + error.Message);
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException)
        {
            return ResultadoEnvio.Permanente(error.Message);
        }
    }
}

/// <summary>
/// Adaptador de correo simulado. Guarda en memoria lo que se habría enviado, así que las pruebas
/// comprueban el contenido del mensaje sin red y sin gastar cuota.
/// </summary>
public sealed class EnviadorCorreoSimulado : IEnviadorCorreo
{
    private int _secuencia;

    public ConcurrentQueue<(string Destino, string Asunto, string Html, string Texto)> Enviados { get; } = new();

    public ConcurrentQueue<ResultadoEnvio> FallarProximos { get; } = new();

    public Task<ResultadoEnvio> EnviarAsync(string destino, string asunto, string cuerpoHtml, string cuerpoTexto, CancellationToken ct)
    {
        if (FallarProximos.TryDequeue(out var fallo))
            return Task.FromResult(fallo);

        Enviados.Enqueue((destino, asunto, cuerpoHtml, cuerpoTexto));
        return Task.FromResult(ResultadoEnvio.Enviado("simulado:" + Interlocked.Increment(ref _secuencia)));
    }
}

/// <summary>Configuración de Web Push (ADR-0009).</summary>
public sealed class OpcionesPush
{
    public const string Seccion = "UcoKudos:Push";

    public string Asunto { get; set; } = "mailto:no-responder@ucokudos.local";

    /// <summary>
    /// **No es secreta.** El cliente la necesita para suscribirse, así que se expone en un endpoint.
    /// </summary>
    public string? ClavePublica { get; set; }

    /// <summary>
    /// SECRETO. Con esta clave cualquiera podría enviar notificaciones a los usuarios de la
    /// plataforma. Confundirla con la pública es el error típico de Web Push.
    /// </summary>
    public string? ClavePrivada { get; set; }

    public bool EstaConfigurado =>
        !string.IsNullOrWhiteSpace(ClavePublica) && !string.IsNullOrWhiteSpace(ClavePrivada);
}

/// <summary>
/// Adaptador de Web Push simulado, que es el que se usa mientras no haya claves VAPID configuradas.
///
/// Está aquí, y no solo en las pruebas, porque la plataforma tiene que funcionar sin este canal: si
/// no hay claves, el aviso queda persistido y el usuario lo ve al entrar. Un canal sin configurar
/// degrada el servicio, no lo interrumpe.
/// </summary>
public sealed class EnviadorPushSimulado : IEnviadorPush
{
    private int _secuencia;

    public ConcurrentQueue<(Guid SuscripcionId, string Titulo, string Cuerpo)> Enviados { get; } = new();

    public ConcurrentQueue<ResultadoEnvio> FallarProximos { get; } = new();

    public Task<ResultadoEnvio> EnviarAsync(
        SuscripcionPush suscripcion, string titulo, string cuerpo, string? url, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(suscripcion);

        if (FallarProximos.TryDequeue(out var fallo))
            return Task.FromResult(fallo);

        Enviados.Enqueue((suscripcion.Id, titulo, cuerpo));
        return Task.FromResult(ResultadoEnvio.Enviado("push:simulado:" + Interlocked.Increment(ref _secuencia)));
    }
}

/// <summary>
/// Persistencia de las suscripciones y de los avisos (ADR-0009).
/// </summary>
public sealed class RepositorioAvisosEf(UcoKudosDbContext contexto) : IRepositorioAvisos
{
    public async Task<IReadOnlyList<SuscripcionPush>> SuscripcionesActivasAsync(Guid personaId, CancellationToken ct) =>
        await contexto.SuscripcionesPush
            .Where(s => s.PersonaId == personaId && s.Activa)
            .Select(s => new SuscripcionPush(s.Id, s.Endpoint, s.ClaveP256dh, s.ClaveAuth))
            .ToListAsync(ct);

    /// <summary>
    /// Desactiva una suscripción que el servicio del navegador rechazó. Sin esto, una tabla de
    /// suscripciones muertas generaría un fallo permanente en cada publicación futura.
    /// </summary>
    public async Task DesactivarSuscripcionAsync(Guid suscripcionId, CancellationToken ct)
    {
        var suscripcion = await contexto.SuscripcionesPush.FirstOrDefaultAsync(s => s.Id == suscripcionId, ct);
        suscripcion?.Desactivar();
    }

    public void AgregarAviso(Guid personaId, string claveMensaje, string variablesJson, DateTimeOffset ahora) =>
        contexto.Avisos.Add(new AvisoFila(personaId, claveMensaje, variablesJson, ahora));
}

/// <summary>
/// Registro de notificaciones (QA-CONF-02). La idempotencia real la da el índice único sobre evento,
/// destinatario y canal; esta consulta evita el trabajo cuando el reproceso es el caso normal.
/// </summary>
public sealed class RegistroNotificacionesEf(UcoKudosDbContext contexto) : IRegistroNotificaciones
{
    public Task<bool> YaNotificadoAsync(Guid eventoId, Guid destinatarioId, CancellationToken ct) =>
        contexto.RegistrosNotificacion
            .AnyAsync(n => n.EventoId == eventoId && n.DestinatarioId == destinatarioId && n.Estado == "Enviado", ct);

    public Task RegistrarAsync(
        Guid eventoId,
        Guid destinatarioId,
        string canal,
        string claveMensaje,
        ResultadoEnvio resultado,
        DateTimeOffset ahora,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(resultado);

        contexto.RegistrosNotificacion.Add(new RegistroNotificacion(
            eventoId, destinatarioId, canal, claveMensaje,
            resultado.Estado.ToString(), resultado.IdExterno, resultado.Error, ahora));

        Observabilidad.Instrumentacion.EnviosPorCanal.Add(1,
            new KeyValuePair<string, object?>("canal", canal),
            new KeyValuePair<string, object?>("estado", resultado.Estado.ToString()));

        return Task.CompletedTask;
    }
}
