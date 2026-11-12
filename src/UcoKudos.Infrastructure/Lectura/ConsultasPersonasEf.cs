using Microsoft.EntityFrameworkCore;
using UcoKudos.Application.Identidad.Entrada.Dto;
using UcoKudos.Application.Identidad.Salida.Puertos;
using UcoKudos.Infrastructure.Persistencia;

namespace UcoKudos.Infrastructure.Lectura;

/// <summary>
/// Lado de lectura de personas (ADR-0010).
/// </summary>
public sealed class ConsultasPersonasEf(UcoKudosDbContext contexto) : IConsultasPersonas
{
    /// <summary>
    /// Letras acentuadas y su equivalente sin acento, para comparar sin tildes.
    ///
    /// Las dos cadenas se pasan a <c>translate</c> de PostgreSQL, que sustituye carácter a
    /// carácter, así que **tienen que medir lo mismo**. Hay una prueba que lo comprueba: si
    /// alguien añade una letra a una sola de las dos, `translate` empieza a borrar caracteres en
    /// silencio en lugar de fallar, y la búsqueda devolvería resultados raros sin que nada avise.
    /// </summary>
    private const string Acentuadas = "áéíóúüñ";
    private const string SinAcento = "aeiouun";

    public async Task<IReadOnlyList<PersonaBuscadaDto>> BuscarAsync(
        string texto, Guid exceptoPersonaId, int tamano, CancellationToken ct)
    {
        var patron = $"%{EscaparComodines(texto)}%";

        // **Por qué va en SQL escrito a mano y no en LINQ.**
        //
        // Buscar «Benitez» tiene que encontrar «Benítez». En Colombia nadie escribe las tildes en
        // una caja de búsqueda, así que una comparación literal haría el buscador inútil justo
        // para los apellidos más comunes. `ILIKE` resuelve mayúsculas pero no acentos.
        //
        // La forma habitual es la extensión `unaccent`, que exige crearla en la base y por tanto
        // una migración y permisos de superusuario. `translate` hace el mismo trabajo para el
        // alfabeto español, no necesita extensión ni migración, y es SQL estándar de PostgreSQL.
        // A cambio hay que mantener a mano la tabla de letras de arriba.
        var personas = await contexto.Personas
            .FromSql(
                $"""
                 SELECT * FROM nucleo.persona
                 WHERE estado_cuenta = 'Activa'
                   AND id <> {exceptoPersonaId}
                   AND translate(lower(nombre_completo), {Acentuadas}, {SinAcento})
                       LIKE translate(lower({patron}), {Acentuadas}, {SinAcento}) ESCAPE '\'
                 ORDER BY nombre_completo
                 LIMIT {tamano}
                 """)
            .AsNoTracking()
            .ToListAsync(ct);

        // La proyección se hace aquí y no en la consulta porque `FromSql` devuelve la entidad
        // completa. Lo que sale del método sigue siendo solo el nombre: el correo y el celular se
        // quedan en el servidor.
        return [.. personas.Select(p => new PersonaBuscadaDto(p.Id, p.NombreCompleto))];
    }

    /// <summary>
    /// Neutraliza los comodines de LIKE en el texto que escribe el usuario.
    ///
    /// Sin esto, escribir <c>%</c> devuelve a todo el mundo y <c>_</c> actúa como comodín de un
    /// carácter. No es un agujero de seguridad —el texto viaja como parámetro, así que no hay
    /// inyección— pero sí un buscador que se comporta de forma inexplicable.
    ///
    /// La barra invertida se escapa primero; hacerlo al final volvería a escapar las barras que
    /// este mismo método acaba de añadir.
    /// </summary>
    private static string EscaparComodines(string texto) =>
        texto.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);
}
