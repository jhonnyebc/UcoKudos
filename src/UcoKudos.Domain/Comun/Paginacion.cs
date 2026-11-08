namespace UcoKudos.Domain.Comun;

/// <summary>
/// Cursor de paginación (ADR-0010): posición del último elemento visto, no un número de página.
///
/// Dos razones para no usar desplazamiento y límite: el motor tendría que recorrer y descartar
/// todas las filas anteriores, así que la página 50 costaría mucho más que la 1; y con contenido
/// que se inserta constantemente, el desplazamiento repite o se salta elementos al pasar de página.
/// </summary>
/// <param name="Instante">Instante del último elemento visto.</param>
/// <param name="Id">Identificador del último elemento visto, para desempatar instantes iguales.</param>
public readonly record struct Cursor(DateTimeOffset Instante, Guid Id)
{
    private const char Separador = '|';

    /// <summary>Lo codifica para que viaje en la URL sin revelar su estructura.</summary>
    public string Codificar()
    {
        var texto = $"{Instante.UtcDateTime:O}{Separador}{Id:N}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(texto))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Decodifica un cursor. Devuelve null si el texto no es un cursor válido.</summary>
    public static Cursor? Decodificar(string? codificado)
    {
        if (string.IsNullOrWhiteSpace(codificado))
            return null;

        try
        {
            var normalizado = codificado.Replace('-', '+').Replace('_', '/');
            normalizado = normalizado.PadRight(normalizado.Length + ((4 - (normalizado.Length % 4)) % 4), '=');
            var texto = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(normalizado));
            var partes = texto.Split(Separador);
            return partes.Length == 2
                   && DateTimeOffset.TryParse(partes[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out var instante)
                   && Guid.TryParseExact(partes[1], "N", out var id)
                ? new Cursor(instante, id)
                : null;
        }
        catch (Exception error) when (error is FormatException or ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>
/// Página de resultados con el cursor para pedir la siguiente. El tamaño está acotado a propósito:
/// ninguna consulta puede devolver una cantidad arbitraria de datos, así que el tiempo de respuesta
/// tiene un techo conocido (ADR-0010).
/// </summary>
public sealed record Pagina<T>(IReadOnlyList<T> Elementos, string? CursorSiguiente)
{
    public const int TamanoPorDefecto = 20;
    public const int TamanoMaximo = 50;

    public static int NormalizarTamano(int? solicitado) => solicitado switch
    {
        null or < 1 => TamanoPorDefecto,
        > TamanoMaximo => TamanoMaximo,
        _ => solicitado.Value
    };

    public static Pagina<T> Vacia() => new([], null);
}
