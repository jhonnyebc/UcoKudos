namespace UcoKudos.Application.Notificaciones.Salida.Puertos;

/// <summary>
/// Resultado de intentar enviar por un canal. La clasificación en transitorio y permanente es lo que
/// permite al proceso de la bandeja decidir si reintenta (ADR-0005): reintentar un número inválido
/// mil veces no lo vuelve válido.
///
/// Los adaptadores devuelven esto en lugar de lanzar excepciones distintas cada uno, para que sean
/// sustituibles sin cambiar el significado del resultado.
/// </summary>
public sealed record ResultadoEnvio(EstadoEnvio Estado, string? IdExterno, string? Error)
{
    public static ResultadoEnvio Enviado(string idExterno) => new(EstadoEnvio.Enviado, idExterno, null);

    /// <summary>Error pasajero: tiempo de espera, límite de tasa, 5xx o falla de red. Se reintenta.</summary>
    public static ResultadoEnvio Transitorio(string error) => new(EstadoEnvio.Transitorio, null, error);

    /// <summary>Error definitivo: número inválido, plantilla rechazada, dirección inexistente. No se reintenta.</summary>
    public static ResultadoEnvio Permanente(string error) => new(EstadoEnvio.Permanente, null, error);

    public bool FueBien => Estado == EstadoEnvio.Enviado;

    public bool SeReintenta => Estado == EstadoEnvio.Transitorio;
}

public enum EstadoEnvio
{
    Enviado = 1,
    Transitorio = 2,
    Permanente = 3
}

/// <summary>
/// Canal de correo (ADR-0008). El puerto habla de SMTP y no de la API de un proveedor concreto: SMTP
/// es un protocolo estándar, así que cambiar de proveedor es cambiar tres variables de entorno.
/// </summary>
public interface IEnviadorCorreo
{
    Task<ResultadoEnvio> EnviarAsync(string destino, string asunto, string cuerpoHtml, string cuerpoTexto, CancellationToken ct);
}

/// <summary>
/// Canal de WhatsApp (ADR-0007). Solo salida y **siempre por plantilla**: el destinatario de un Kudo
/// nunca escribió primero, así que la plataforma está siempre fuera de la ventana de conversación
/// de 24 horas y un mensaje de texto libre sería rechazado.
/// </summary>
public interface IWhatsAppGateway
{
    Task<ResultadoEnvio> EnviarPlantillaAsync(
        string telefonoE164,
        string plantilla,
        string idioma,
        IReadOnlyList<string> parametros,
        CancellationToken ct);
}

/// <summary>
/// Canal de notificación push para quien no está conectado (ADR-0009). Web Push con VAPID, que es un
/// estándar abierto: no ata la solución a ningún servicio de terceros.
/// </summary>
public interface IEnviadorPush
{
    Task<ResultadoEnvio> EnviarAsync(SuscripcionPush suscripcion, string titulo, string cuerpo, string? url, CancellationToken ct);
}

/// <summary>Suscripción de un navegador a Web Push.</summary>
public sealed record SuscripcionPush(Guid Id, string Endpoint, string ClaveP256dh, string ClaveAuth);

/// <summary>
/// Canal de avisos en vivo por SSE, para quien **sí** está conectado (ADR-0009).
///
/// Los avisos se persisten siempre, además de enviarse: un canal en vivo es efímero, y si el
/// destinatario no estaba conectado ni tenía suscripción, el aviso sigue ahí y lo ve al entrar.
/// </summary>
public interface IAvisosEnVivo
{
    /// <summary>¿Hay una conexión abierta para esa persona?</summary>
    bool EstaConectado(Guid personaId);

    /// <summary>Entrega el aviso por la conexión abierta. Devuelve false si ya no había ninguna.</summary>
    Task<bool> EntregarAsync(Guid personaId, string clave, object datos, CancellationToken ct);
}

/// <summary>
/// Almacenamiento de objetos para los adjuntos (ADR-0012). La API es compatible con S3 por decisión
/// explícita: migrar a un servicio gestionado es cambiar configuración, no reescribir el adaptador.
/// </summary>
public interface IAlmacenObjetos
{
    /// <summary>
    /// URL prefirmada para que el cliente **suba** el archivo directo al almacenamiento. Así los
    /// megabytes no atraviesan la API, que es lo que importa en red móvil (DRV-001).
    /// </summary>
    Task<string> UrlDeSubidaAsync(string clave, TimeSpan validez, CancellationToken ct);

    /// <summary>
    /// URL prefirmada de **descarga**, de vida corta. La emite la API solo después de comprobar
    /// permisos: el almacenamiento no sabe nada de la jerarquía, solo honra una firma (QA-SEG-02).
    /// </summary>
    Task<string> UrlDeDescargaAsync(string clave, TimeSpan validez, CancellationToken ct);

    /// <summary>
    /// Inspecciona el objeto subido: tipo real del contenido, tamaño y hash.
    ///
    /// El tipo se determina por los primeros bytes del archivo, **no** por la extensión ni por lo que
    /// declaró el cliente: las dos las controla quien sube el archivo.
    /// </summary>
    Task<DatosObjeto?> InspeccionarAsync(string clave, CancellationToken ct);

    Task EliminarAsync(string clave, CancellationToken ct);
}

/// <summary>Lo que el almacenamiento sabe de un objeto ya subido.</summary>
public sealed record DatosObjeto(string TipoDetectado, long TamanoBytes, string HashContenido);

/// <summary>Persistencia de las suscripciones a Web Push y de los avisos (ADR-0009).</summary>
public interface IRepositorioAvisos
{
    Task<IReadOnlyList<SuscripcionPush>> SuscripcionesActivasAsync(Guid personaId, CancellationToken ct);

    /// <summary>
    /// Desactiva una suscripción que el servicio del navegador rechazó. Una tabla de suscripciones
    /// muertas genera fallos permanentes en cada publicación.
    /// </summary>
    Task DesactivarSuscripcionAsync(Guid suscripcionId, CancellationToken ct);

    void AgregarAviso(Guid personaId, string claveMensaje, string variablesJson, DateTimeOffset ahora);
}

/// <summary>
/// Lado de lectura de los avisos (ADR-0010).
///
/// Está separado de <see cref="IRepositorioAvisos"/> a propósito, y no por simetría: aquel escribe
/// dentro de la transacción del gateway, este solo lee para pintar una pantalla. Mezclarlos haría
/// que una consulta de lectura arrastrara el seguimiento de cambios de la unidad de trabajo, que es
/// trabajo desperdiciado en el camino que más se recorre.
///
/// Devuelve filas planas y no entidades: lo que la pantalla necesita, nada más.
/// </summary>
public interface IConsultasAvisos
{
    /// <summary>
    /// Los avisos de una persona, del más reciente al más antiguo.
    ///
    /// Se pide un tamaño porque una bandeja vieja puede tener miles y nadie baja hasta el final.
    /// Devuelve también cuántos quedan sin leer **en total**, no solo en esta página.
    /// </summary>
    Task<(IReadOnlyList<FilaAviso> Elementos, int SinLeer)> DeLaPersonaAsync(
        Guid personaId, int tamano, CancellationToken ct);

    /// <summary>
    /// Marca un aviso como leído, **solo si es de esa persona**.
    ///
    /// La comprobación de propiedad va en la misma operación y no en una consulta previa: entre
    /// comprobar y escribir hay una ventana, y además así es imposible olvidarla. Devuelve false si
    /// el aviso no existe o es de otra persona, que desde fuera es lo mismo: nadie debe poder
    /// averiguar qué avisos tienen los demás.
    /// </summary>
    Task<bool> MarcarLeidoAsync(Guid personaId, long avisoId, CancellationToken ct);
}

/// <summary>Un aviso tal como está guardado: la clave del mensaje y sus variables, sin componer.</summary>
public sealed record FilaAviso(
    long Id,
    string ClaveMensaje,
    string VariablesJson,
    bool Leido,
    DateTimeOffset GeneradoEn);
