using System.Globalization;

namespace UcoKudos.Application.Notificaciones.Catalogo;

/// <summary>Claves de los mensajes. Vocabulario cerrado, como el de la auditoría y el de los eventos.</summary>
public static class ClavesMensaje
{
    public const string KudoRecibido = "kudo.recibido";
    public const string ReplicaEnTuKudo = "replica.en_tu_kudo";
    public const string SolicitudAprobada = "solicitud.aprobada";
    public const string SolicitudRechazada = "solicitud.rechazada";
    public const string ContenidoVetado = "contenido.vetado";
    public const string CodigoVerificacion = "verificacion.codigo";
}

/// <summary>
/// Una plantilla de mensaje con sus variables declaradas.
/// </summary>
/// <param name="Clave">Clave del mensaje.</param>
/// <param name="Asunto">Asunto, para el correo.</param>
/// <param name="Titulo">Título corto, para la notificación push.</param>
/// <param name="Cuerpo">Cuerpo con marcadores <c>{0}</c>, <c>{1}</c>...</param>
/// <param name="Variables">Nombre de cada variable en orden, para documentar y para validar.</param>
/// <param name="PlantillaWhatsApp">Nombre de la plantilla aprobada en Meta, si este mensaje va por WhatsApp.</param>
public sealed record Mensaje(
    string Clave,
    string Asunto,
    string Titulo,
    string Cuerpo,
    IReadOnlyList<string> Variables,
    string? PlantillaWhatsApp);

/// <summary>
/// Catálogo de los textos en español de Colombia (ADR-0006).
///
/// Los textos viven aquí y no incrustados en los casos de uso, así que corregir una redacción no es
/// cambiar lógica. Cada mensaje declara qué variables espera, y
/// <see cref="Formatear"/> falla si recibe otra cantidad: es lo que evita que el catálogo y el código
/// se desincronicen en silencio y salga un mensaje con un hueco sin rellenar.
/// </summary>
public static class CatalogoMensajes
{
    private static readonly Dictionary<string, Mensaje> Mensajes = new(StringComparer.Ordinal)
    {
        [ClavesMensaje.KudoRecibido] = new(
            ClavesMensaje.KudoRecibido,
            "Recibiste un Kudo en UCO Kudos",
            "Recibiste un Kudo",
            "{0} te reconoció en la categoría {1}. Entra a UCO Kudos para verlo.",
            ["autor", "categoria"],
            "kudo_recibido"),

        [ClavesMensaje.ReplicaEnTuKudo] = new(
            ClavesMensaje.ReplicaEnTuKudo,
            "Alguien respondió en tu Kudo",
            "Nueva respuesta",
            "{0} respondió en un Kudo que te involucra.",
            ["autor"],
            null),

        [ClavesMensaje.SolicitudAprobada] = new(
            ClavesMensaje.SolicitudAprobada,
            "Tu cuenta de UCO Kudos fue aprobada",
            "Cuenta aprobada",
            "Tu solicitud para el rol {0} fue aprobada. Ya puedes entrar a UCO Kudos.",
            ["rol"],
            null),

        [ClavesMensaje.SolicitudRechazada] = new(
            ClavesMensaje.SolicitudRechazada,
            "Tu solicitud de UCO Kudos no fue aprobada",
            "Solicitud no aprobada",
            "Tu solicitud para el rol {0} no fue aprobada. Motivo: {1}",
            ["rol", "motivo"],
            null),

        [ClavesMensaje.ContenidoVetado] = new(
            ClavesMensaje.ContenidoVetado,
            "Un contenido tuyo fue retirado de UCO Kudos",
            "Contenido retirado",
            "Un contenido que publicaste fue retirado por moderación. Motivo: {0}",
            ["motivo"],
            null),

        [ClavesMensaje.CodigoVerificacion] = new(
            ClavesMensaje.CodigoVerificacion,
            "Código de verificación de UCO Kudos",
            "Código de verificación",
            "Tu código de verificación es {0}. Vence en {1} minutos.",
            ["codigo", "minutos"],

            // **Sin plantilla de WhatsApp, y es una decisión, no un olvido.**
            //
            // Meta clasifica los códigos de verificación en la categoría Autenticación, y las cuentas
            // de WhatsApp Business sin verificación comercial no pueden crear plantillas de esa
            // categoría. Se intentó el 2026-11-09 y el panel lo rechaza con «esta cuenta no tiene
            // permiso para crear una plantilla de mensaje». Conseguir la verificación comercial exige
            // documentos y un plazo que no controlamos.
            //
            // El mensaje sale igual, por correo y por el aviso dentro de la aplicación. Que esto se
            // arregle cambiando un valor de este catálogo, sin tocar el dominio, los casos de uso ni
            // el gateway, es precisamente lo que ADR-0006 perseguía al separar el mensaje del canal.
            //
            // Si algún día se verifica el negocio, se crea la plantilla y se vuelve a poner su nombre
            // aquí: el canal se reactiva solo para este mensaje.
            null)
    };

    public static Mensaje Por(string clave) =>
        Mensajes.TryGetValue(clave, out var mensaje)
            ? mensaje
            : throw new InvalidOperationException("No existe el mensaje con clave " + clave + " en el catálogo.");

    public static IReadOnlyCollection<Mensaje> Todos => Mensajes.Values;

    /// <summary>
    /// Formatea el cuerpo con sus variables. Falla si la cantidad no coincide con la declarada: es
    /// la comprobación que evita enviar un mensaje con un marcador sin rellenar.
    /// </summary>
    public static string Formatear(Mensaje mensaje, IReadOnlyList<string> valores)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        ArgumentNullException.ThrowIfNull(valores);

        if (valores.Count != mensaje.Variables.Count)
        {
            throw new InvalidOperationException(
                "El mensaje " + mensaje.Clave + " espera " + mensaje.Variables.Count
                + " variables (" + string.Join(", ", mensaje.Variables) + ") y recibió " + valores.Count + ".");
        }

        return string.Format(CultureInfo.GetCultureInfo("es-CO"), mensaje.Cuerpo, [.. valores]);
    }
}
