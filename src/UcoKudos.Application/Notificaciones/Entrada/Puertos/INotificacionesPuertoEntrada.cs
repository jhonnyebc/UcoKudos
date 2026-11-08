using UcoKudos.Application.Notificaciones.Salida.Puertos;

namespace UcoKudos.Application.Notificaciones.Entrada.Puertos;

/// <summary>
/// Puerto de entrada del módulo de notificaciones. Lo llama el proceso en segundo plano de la
/// infraestructura, que es quien toma los eventos de la bandeja con bloqueo y los reintenta.
///
/// Esa separación es deliberada: el proceso sabe de transacciones y de reintentos, y este puerto sabe
/// de avisos. Así el caso de uso se puede probar sin levantar el proceso.
/// </summary>
public interface INotificacionesPuertoEntrada
{
    /// <summary>
    /// Procesa un evento de la bandeja y produce sus notificaciones.
    /// </summary>
    /// <returns>
    /// true si el evento queda terminado; false si algún canal falló de forma transitoria y el
    /// proceso debe devolverlo a la cola.
    /// </returns>
    Task<bool> ProcesarAsync(
        Guid eventoId,
        string tipoEvento,
        string carga,
        DateTimeOffset ocurridoEn,
        string? correlacionId,
        CancellationToken ct);
}

/// <summary>
/// Registro de lo que se envió por cada canal. Es donde vive la **idempotencia** de las
/// notificaciones (QA-CONF-02): la combinación de evento, destinatario y canal es única, así que
/// reprocesar un evento no produce un segundo correo ni un segundo WhatsApp.
/// </summary>
public interface IRegistroNotificaciones
{
    Task<bool> YaNotificadoAsync(Guid eventoId, Guid destinatarioId, CancellationToken ct);

    Task RegistrarAsync(
        Guid eventoId,
        Guid destinatarioId,
        string canal,
        string claveMensaje,
        ResultadoEnvio resultado,
        DateTimeOffset ahora,
        CancellationToken ct);
}
