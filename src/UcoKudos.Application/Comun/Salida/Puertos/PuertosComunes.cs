namespace UcoKudos.Application.Comun.Salida.Puertos;

/// <summary>
/// Unidad de trabajo (ADR-0005). Es la pieza que hace que publicar un Kudo sea atómico: el cambio de
/// estado, su evento de auditoría y su evento pendiente se confirman **juntos** o no se confirma
/// ninguno.
///
/// Por eso los repositorios no guardan por su cuenta. Si uno lo hiciera, la atomicidad se rompería
/// sin que nada avisara, y reaparecería la ventana «commit exitoso, envío perdido» que el patrón
/// Outbox existe para cerrar.
/// </summary>
public interface IUnidadTrabajo
{
    /// <summary>
    /// Confirma todo lo acumulado en una transacción. Lanza
    /// <see cref="Domain.Transversal.ExcepcionConflictoConcurrencia"/> si otra operación ganó la
    /// carrera, para que el usuario pueda reintentar en lugar de sobrescribir en silencio.
    /// </summary>
    Task ConfirmarAsync(CancellationToken ct);

    /// <summary>Descarta lo acumulado. Se usa cuando el caso de uso decide no seguir.</summary>
    void Descartar();
}

/// <summary>
/// Bandeja de salida (ADR-0005). El caso de uso deposita aquí el evento y devuelve el control: no
/// habla con ningún proveedor externo.
///
/// Es la frontera asíncrona del Manifiesto Reactivo, y la que da las tres propiedades que importan:
/// el emisor no sabe qué canales existen, un fallo de envío no alcanza la transacción confirmada, y
/// si el proceso muere el evento sigue pendiente.
/// </summary>
public interface IBandejaSalida
{
    /// <summary>
    /// Programa un evento para despacharlo cuanto antes. Se escribe en la **misma transacción** que
    /// el cambio que lo origina.
    /// </summary>
    void Programar(EventoPendiente evento);

    /// <summary>
    /// Programa un evento para un instante futuro. Es lo que permite que un recordatorio no necesite
    /// un planificador aparte.
    /// </summary>
    void ProgramarPara(EventoPendiente evento, DateTimeOffset cuando);
}

/// <summary>
/// Un evento que espera ser despachado.
/// </summary>
/// <param name="EventoId">
/// Identificador del evento de negocio. Es la clave de la idempotencia: el gateway deduplica por
/// este valor y por destinatario, así que reprocesar no envía dos veces (QA-CONF-02).
/// </param>
/// <param name="TipoEvento">Qué ocurrió, del vocabulario de <see cref="TiposEvento"/>.</param>
/// <param name="Carga">Datos del evento en JSON, lo mínimo para que el gateway resuelva el aviso.</param>
/// <param name="OcurridoEn">Cuándo ocurrió el hecho, no cuándo se despacha.</param>
/// <param name="ContextoTraza">
/// Contexto de la traza de la petición que lo originó. Es lo que hace que la traza cruce la frontera
/// asíncrona: sin esto, la publicación y sus avisos serían trazas inconexas (ADR-0014).
/// </param>
public sealed record EventoPendiente(
    Guid EventoId,
    string TipoEvento,
    string Carga,
    DateTimeOffset OcurridoEn,
    string? ContextoTraza);

/// <summary>Vocabulario cerrado de eventos de la bandeja, por la misma razón que el de la auditoría.</summary>
public static class TiposEvento
{
    public const string KudoPublicado = "kudo.publicado";
    public const string ReplicaPublicada = "replica.publicada";
    public const string SolicitudAprobada = "solicitud.aprobada";
    public const string SolicitudRechazada = "solicitud.rechazada";
    public const string ContenidoVetado = "contenido.vetado";
    public const string VerificacionCelularSolicitada = "verificacion.celular";
}

/// <summary>
/// Caché del lado de lectura (ADR-0010). La invalidación es **por evento** y no por expiración: una
/// expiración por tiempo obliga a elegir entre mostrar datos viejos y que la caché sirva de poco, y
/// aquí no hace falta ese compromiso porque la bandeja ya produce los eventos.
///
/// Está detrás de un puerto para que pasar a una caché distribuida, cuando haya más de una
/// instancia, sea cambiar el adaptador y nada más.
/// </summary>
public interface ICacheConsultas
{
    Task<T?> ObtenerAsync<T>(string clave, CancellationToken ct) where T : class;

    Task GuardarAsync<T>(string clave, T valor, CancellationToken ct) where T : class;

    /// <summary>Invalida todas las entradas de un grupo. Lo llaman los casos de uso que escriben.</summary>
    Task InvalidarGrupoAsync(string grupo, CancellationToken ct);
}

/// <summary>Grupos de caché. Declararlos aquí evita que la invalidación quede dispersa por el código.</summary>
public static class GruposCache
{
    /// <summary>Primera página del muro. La invalida publicar o vetar un Kudo.</summary>
    public const string Muro = "muro";

    /// <summary>Catálogo de categorías. La invalida administrar una categoría.</summary>
    public const string Categorias = "categorias";

    /// <summary>Estructura académica. La invalida crear una facultad o un programa.</summary>
    public const string EstructuraAcademica = "academico";
}
