namespace UcoKudos.Infrastructure.Persistencia.Outbox;

/// <summary>
/// Una fila de la bandeja de salida (ADR-0005).
///
/// Es un tipo de **infraestructura**, no de dominio: el dominio no sabe que existe una tabla de
/// eventos pendientes, solo conoce el puerto que los programa.
///
/// Lleva el contexto de traza de la petición que lo originó, y eso es lo que hace que la traza cruce
/// la frontera asíncrona: sin él, la publicación de un Kudo y sus tres avisos serían trazas
/// inconexas y QA-OBS-01 no se podría cumplir (ADR-0014).
/// </summary>
public sealed class MensajeOutbox
{
    /// <summary>
    /// Intentos antes de descartar. Con espera creciente, el último intento ocurre bastante después
    /// del primero, que es tiempo de sobra para que un proveedor caído vuelva.
    /// </summary>
    public const int MaximoIntentos = 8;

    private MensajeOutbox()
    {
        TipoEvento = null!;
        Payload = null!;
    }

    public MensajeOutbox(Guid eventoId, string tipoEvento, string payload, DateTimeOffset ocurridoEn, DateTimeOffset proximoIntento, string? contextoTraza)
    {
        Id = Guid.CreateVersion7(ocurridoEn);
        EventoId = eventoId;
        TipoEvento = tipoEvento;
        Payload = payload;
        OcurridoEn = ocurridoEn;
        ProximoIntento = proximoIntento;
        ContextoTraza = contextoTraza;
    }

    public Guid Id { get; private set; }

    /// <summary>Identificador del evento de negocio. Es la clave de la idempotencia (QA-CONF-02).</summary>
    public Guid EventoId { get; private set; }

    public string TipoEvento { get; private set; }
    public string Payload { get; private set; }
    public DateTimeOffset OcurridoEn { get; private set; }

    /// <summary>
    /// Cuándo se puede volver a intentar. Permite el reintento con espera creciente y también los
    /// eventos programados a futuro, así que un recordatorio no necesita un planificador aparte.
    /// </summary>
    public DateTimeOffset ProximoIntento { get; private set; }

    public short Intentos { get; private set; }
    public string? UltimoError { get; private set; }
    public string? ContextoTraza { get; private set; }

    /// <summary>Null mientras está pendiente. Se marca dentro de la misma transacción que lo atiende.</summary>
    public DateTimeOffset? ProcesadoEn { get; private set; }

    public void MarcarProcesado(DateTimeOffset ahora) => ProcesadoEn = ahora;

    /// <summary>
    /// Registra un fallo y calcula el próximo intento con espera creciente. Al agotar los intentos el
    /// evento se marca procesado para que no se reintente eternamente, y queda constancia del error.
    /// </summary>
    public void RegistrarFallo(string error, DateTimeOffset ahora)
    {
        Intentos++;
        UltimoError = error.Length > 500 ? error[..500] : error;

        if (Intentos >= MaximoIntentos)
        {
            ProcesadoEn = ahora;
            return;
        }

        // 2, 4, 8, 16... segundos. Acotado para que un pico de fallos no empuje el reintento a días.
        var espera = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, Intentos), 900));
        ProximoIntento = ahora.Add(espera);
    }
}

/// <summary>
/// Registro de lo enviado por cada canal. Es donde vive la idempotencia de las notificaciones: la
/// combinación de evento, destinatario y canal es única en la base, así que reprocesar un evento no
/// produce un segundo correo ni un segundo WhatsApp (QA-CONF-02).
/// </summary>
public sealed class RegistroNotificacion
{
    private RegistroNotificacion()
    {
        Canal = null!;
        ClaveMensaje = null!;
        Estado = null!;
    }

    public RegistroNotificacion(
        Guid eventoId, Guid destinatarioId, string canal, string claveMensaje,
        string estado, string? idExterno, string? error, DateTimeOffset ahora)
    {
        Id = Guid.CreateVersion7(ahora);
        EventoId = eventoId;
        DestinatarioId = destinatarioId;
        Canal = canal;
        ClaveMensaje = claveMensaje;
        Estado = estado;
        IdExterno = idExterno;
        Error = error;
        EnviadaEn = ahora;
    }

    public Guid Id { get; private set; }
    public Guid EventoId { get; private set; }
    public Guid DestinatarioId { get; private set; }
    public string Canal { get; private set; }
    public string ClaveMensaje { get; private set; }
    public string Estado { get; private set; }

    /// <summary>Identificador que devolvió el proveedor. Es la evidencia de envío que pide la entrega.</summary>
    public string? IdExterno { get; private set; }

    public string? Error { get; private set; }
    public DateTimeOffset EnviadaEn { get; private set; }
}

/// <summary>Suscripción de un navegador a Web Push (ADR-0009).</summary>
public sealed class SuscripcionPushFila
{
    private SuscripcionPushFila()
    {
        Endpoint = null!;
        ClaveP256dh = null!;
        ClaveAuth = null!;
    }

    public SuscripcionPushFila(Guid personaId, string endpoint, string claveP256dh, string claveAuth, DateTimeOffset ahora)
    {
        Id = Guid.CreateVersion7(ahora);
        PersonaId = personaId;
        Endpoint = endpoint;
        ClaveP256dh = claveP256dh;
        ClaveAuth = claveAuth;
        Activa = true;
        RegistradaEn = ahora;
    }

    public Guid Id { get; private set; }
    public Guid PersonaId { get; private set; }
    public string Endpoint { get; private set; }
    public string ClaveP256dh { get; private set; }
    public string ClaveAuth { get; private set; }
    public bool Activa { get; private set; }
    public DateTimeOffset RegistradaEn { get; private set; }

    /// <summary>
    /// Se desactiva cuando el servicio del navegador la rechaza. Una tabla de suscripciones muertas
    /// generaría un fallo permanente en cada publicación.
    /// </summary>
    public void Desactivar() => Activa = false;

    /// <summary>
    /// Pasa la suscripción a otra persona y actualiza sus claves.
    ///
    /// El endpoint identifica al **navegador**, no a la persona, y un navegador puede cambiar de
    /// dueño: una sala de cómputo de la universidad es el caso evidente. Si no se reasignara, quien
    /// usara ese equipo después recibiría los avisos del anterior.
    ///
    /// Reactivarla también es deliberado: quien vuelve a dar permiso espera volver a recibir
    /// avisos, no quedarse con una suscripción apagada por un rechazo antiguo.
    /// </summary>
    public void Reasignar(Guid personaId, string claveP256dh, string claveAuth)
    {
        PersonaId = personaId;
        ClaveP256dh = claveP256dh;
        ClaveAuth = claveAuth;
        Activa = true;
    }
}

/// <summary>
/// Aviso persistido (ADR-0009). Se guarda **siempre**, además de intentar entregarlo: un canal en
/// vivo es efímero, y si el destinatario no estaba conectado ni tenía suscripción, el aviso sigue
/// aquí y lo ve al entrar.
///
/// La clave es una secuencia numérica y no un identificador aleatorio, porque el envío en vivo
/// necesita saber qué avisos son posteriores al último entregado.
/// </summary>
public sealed class AvisoFila
{
    private AvisoFila()
    {
        ClaveMensaje = null!;
        Variables = null!;
    }

    public AvisoFila(Guid personaId, string claveMensaje, string variablesJson, DateTimeOffset ahora)
    {
        PersonaId = personaId;
        ClaveMensaje = claveMensaje;
        Variables = variablesJson;
        GeneradoEn = ahora;
    }

    public long Id { get; private set; }
    public Guid PersonaId { get; private set; }
    public string ClaveMensaje { get; private set; }
    public string Variables { get; private set; }
    public bool Leido { get; private set; }
    public DateTimeOffset GeneradoEn { get; private set; }

    public void MarcarLeido() => Leido = true;
}
