using UcoKudos.Application.Comun;
using UcoKudos.Application.Identidad.Entrada.Dto;
using UcoKudos.Application.Identidad.Salida.Puertos;

namespace UcoKudos.Application.Identidad.CasosUso;

/// <summary>
/// Buscar a quién reconocer (apoya RF-07).
///
/// Existe porque publicar un Kudo necesita el identificador del destinatario, y pedírselo al
/// usuario es inaceptable: nadie conoce de memoria un UUID. Sin esta consulta la funcionalidad
/// obligatoria es inusable aunque funcione.
/// </summary>
public sealed class BuscarPersonasCasoUso(IConsultasPersonas consultas)
{
    /// <summary>
    /// Mínimo de caracteres antes de consultar.
    ///
    /// Con una sola letra la respuesta serían cientos de filas que no ayudan a elegir, y además
    /// convertiría el buscador en una forma cómoda de pasearse por el directorio entero. Dos
    /// caracteres es el equilibrio: deja buscar apellidos cortos sin abrir la puerta a eso.
    /// </summary>
    public const int MinimoCaracteres = 2;

    private const int TamanoPorOmision = 10;
    private const int TamanoMaximo = 25;

    public async Task<IReadOnlyList<PersonaBuscadaDto>> EjecutarAsync(
        ContextoPeticion contexto, string? texto, int? tamano, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var limpio = (texto ?? string.Empty).Trim();

        // Una búsqueda demasiado corta devuelve vacío en lugar de error. Es lo que pasa mientras
        // alguien escribe la primera letra, y responder 400 a cada pulsación llenaría el registro
        // de errores que no lo son.
        if (limpio.Length < MinimoCaracteres)
        {
            return [];
        }

        // El tamaño se acota aquí y no se confía al cliente, igual que en la bandeja de avisos.
        var cuantos = Math.Clamp(tamano ?? TamanoPorOmision, 1, TamanoMaximo);

        return await consultas.BuscarAsync(limpio, contexto.Ambito.PersonaId, cuantos, ct);
    }
}
