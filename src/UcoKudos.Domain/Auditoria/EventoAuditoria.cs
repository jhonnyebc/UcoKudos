using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.Domain.Auditoria;

/// <summary>
/// Vocabulario cerrado de acciones auditables. Está cerrado a propósito: un nombre de acción escrito
/// a mano en cada sitio acaba teniendo cuatro variantes, y entonces la auditoría no se puede
/// consultar.
/// </summary>
public static class AccionesAuditoria
{
    public const string KudoPublicar = "kudo.publicar";
    public const string AdjuntoCargar = "adjunto.cargar";
    public const string AdjuntoDescargar = "adjunto.descargar";
    public const string AdjuntoEliminar = "adjunto.eliminar";
    public const string ReplicaPublicar = "replica.publicar";
    public const string ContenidoCalificar = "contenido.calificar";
    public const string ContenidoReportar = "contenido.reportar";
    public const string ReporteDescartar = "reporte.descartar";
    public const string ContenidoVetar = "contenido.vetar";
    public const string CategoriaCrear = "categoria.crear";
    public const string CategoriaActualizar = "categoria.actualizar";
    public const string CategoriaDesactivar = "categoria.desactivar";
    public const string SolicitudPresentar = "solicitud.presentar";
    public const string SolicitudAprobar = "solicitud.aprobar";
    public const string ContactoVerificar = "contacto.verificar";
    public const string SolicitudRechazar = "solicitud.rechazar";
    public const string UsuarioCambiarRol = "usuario.cambiar_rol";
    public const string UsuarioDesactivar = "usuario.desactivar";
    public const string NotificacionEnviar = "notificacion.enviar";
    public const string NotificacionFallar = "notificacion.fallar";

    /// <summary>
    /// Acceso rechazado por falta de permiso. Se audita a propósito: un intento fallido suele
    /// informar más que uno exitoso, y es lo que permite detectar a alguien tanteando permisos
    /// (QA-SEG-01).
    /// </summary>
    public const string AccesoRechazado = "acceso.rechazado";
}

/// <summary>
/// Evento de auditoría (RF-17). Es un registro de negocio, no un log: sirve para rendir cuentas, no
/// para diagnosticar, y por eso vive en una tabla de solo inserción y no en un archivo que se rota.
///
/// Cada evento guarda el hash del anterior junto con el suyo, formando una cadena (ADR-0013). Eso
/// convierte la inmutabilidad en algo **demostrable**: alterar o eliminar un evento intermedio rompe
/// la cadena y lo detecta <see cref="CadenaAuditoria"/>.
/// </summary>
public sealed class EventoAuditoria
{
    private EventoAuditoria()
    {
        Accion = null!;
        TipoEntidad = null!;
        Datos = null!;
        Hash = null!;
    }

    public EventoAuditoria(
        Guid? actorId,
        string accion,
        string tipoEntidad,
        Guid entidadId,
        string datosJson,
        string? correlacionId,
        string? hashAnterior,
        DateTimeOffset ahora)
    {
        Accion = ExigirTexto(accion, "La acción auditada es obligatoria.");
        TipoEntidad = ExigirTexto(tipoEntidad, "El tipo de entidad auditada es obligatorio.");
        ActorId = actorId;
        EntidadId = entidadId;
        Datos = string.IsNullOrWhiteSpace(datosJson) ? "{}" : datosJson;
        CorrelacionId = correlacionId;
        HashAnterior = hashAnterior;

        // La fecha se trunca antes de calcular el hash. Sin esto, la verificación fallaría al releer
        // aunque nada se hubiera alterado: el motor guarda microsegundos y el valor recalculado no
        // coincidiría con el guardado (ADR-0013).
        OcurridoEn = Truncar(ahora);
        Hash = CalcularHash();
    }

    public long Id { get; private set; }

    /// <summary>Null en eventos del sistema, como el envío de una notificación por el proceso de fondo.</summary>
    public Guid? ActorId { get; private set; }

    public string Accion { get; private set; }
    public string TipoEntidad { get; private set; }
    public Guid EntidadId { get; private set; }

    /// <summary>
    /// Datos relevantes del cambio, en JSON canónico. Guarda **qué** cambió, no necesariamente los
    /// valores: la auditoría es permanente, así que meter datos personales innecesarios crea un
    /// problema perpetuo.
    /// </summary>
    public string Datos { get; private set; }

    /// <summary>Une este evento con la traza de la petición que lo originó (ADR-0014, QA-OBS-01).</summary>
    public string? CorrelacionId { get; private set; }

    /// <summary>Hash del evento anterior de la cadena. Null solo en el primero.</summary>
    public string? HashAnterior { get; private set; }

    public string Hash { get; private set; }
    public DateTimeOffset OcurridoEn { get; private set; }

    /// <summary>Recalcula el hash a partir de los datos guardados, para verificar la cadena.</summary>
    public string RecalcularHash() => CalcularHash();

    private string CalcularHash()
    {
        // El orden y el formato de los campos son parte del contrato del hash: si cambian, todos los
        // hashes anteriores dejan de verificar.
        var material = string.Join(
            "\u001f",
            HashAnterior ?? string.Empty,
            ActorId?.ToString("N") ?? string.Empty,
            Accion,
            TipoEntidad,
            EntidadId.ToString("N"),
            Datos,
            OcurridoEn.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    /// <summary>
    /// Trunca a milisegundos. El motor guarda microsegundos en su tipo de instante y el valor
    /// releído no sería idéntico al original, lo que rompería la verificación.
    /// </summary>
    private static DateTimeOffset Truncar(DateTimeOffset instante) =>
        new(instante.UtcDateTime.AddTicks(-(instante.UtcDateTime.Ticks % TimeSpan.TicksPerMillisecond)), TimeSpan.Zero);

    private static string ExigirTexto(string? valor, string mensajeSiFalta)
    {
        var limpio = (valor ?? string.Empty).Trim();
        return limpio.Length == 0 ? throw new ExcepcionDominio(mensajeSiFalta) : limpio;
    }
}
