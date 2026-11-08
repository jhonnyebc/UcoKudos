using System.Text;

namespace UcoKudos.Application.Notificaciones.Catalogo;

/// <summary>
/// Compone el HTML del correo (ADR-0008).
///
/// Dos decisiones de diseño, las dos por DRV-001: estilos **en línea**, porque los clientes de
/// correo ignoran las hojas de estilo externas y muchos descartan incluso un bloque de estilos; y
/// ancho máximo con etiqueta de viewport, porque el 90 % de los destinatarios lo abrirá en el
/// teléfono. Un correo que se ve bien en el monitor y mal en el celular incumple el driver en el
/// 90 % de los casos.
/// </summary>
public static class PlantillaCorreo
{
    public static string Componer(string titulo, string cuerpo)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.Append("<title>").Append(Escapar(titulo)).Append("</title></head>");
        html.Append("<body style=\"margin:0;padding:24px 16px;background:#f6f6f6;color:#1a1a1a;");
        html.Append("font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Helvetica,Arial,sans-serif\">");
        html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" width=\"100%\">");
        html.Append("<tr><td align=\"center\">");
        html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" ");
        html.Append("style=\"max-width:520px;width:100%;background:#ffffff;border-radius:8px\">");
        html.Append("<tr><td style=\"padding:24px\">");
        html.Append("<h1 style=\"margin:0 0 12px;font-size:20px;font-weight:600;line-height:1.3\">");
        html.Append(Escapar(titulo));
        html.Append("</h1>");
        html.Append("<p style=\"margin:0;font-size:16px;line-height:1.5\">");
        html.Append(Escapar(cuerpo));
        html.Append("</p>");
        html.Append("<p style=\"margin:24px 0 0;font-size:13px;line-height:1.4;color:#666666\">");
        html.Append("UCO Kudos — Universidad Católica de Oriente");
        html.Append("</p>");
        html.Append("</td></tr></table></td></tr></table></body></html>");
        return html.ToString();
    }

    /// <summary>
    /// Escapa el texto antes de ponerlo en el HTML. El contenido viene de personas, así que puede
    /// contener etiquetas: sin escapar, el nombre de alguien podría inyectar marcado en el correo.
    /// </summary>
    private static string Escapar(string texto) =>
        (texto ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
