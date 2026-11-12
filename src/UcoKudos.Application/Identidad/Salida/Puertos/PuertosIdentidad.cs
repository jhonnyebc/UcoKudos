using UcoKudos.Application.Identidad.Entrada.Dto;

namespace UcoKudos.Application.Identidad.Salida.Puertos;

/// <summary>
/// Lado de lectura de personas (ADR-0010).
///
/// Es un puerto aparte del repositorio de dominio a propósito: el repositorio carga entidades
/// completas para modificarlas, y aquí solo se pinta una lista. Mezclar los dos haría que una
/// pantalla de búsqueda arrastrase el agregado entero por cada fila.
/// </summary>
public interface IConsultasPersonas
{
    /// <summary>
    /// Personas activas cuyo nombre contiene el texto buscado, ordenadas alfabéticamente.
    ///
    /// Excluye a <paramref name="exceptoPersonaId"/>, que es quien busca. No es un capricho de la
    /// interfaz: el dominio prohíbe reconocerse a uno mismo, así que ofrecerse como destinatario
    /// solo sirve para llevar a un error evitable. Es más barato que la opción no exista.
    /// </summary>
    Task<IReadOnlyList<PersonaBuscadaDto>> BuscarAsync(
        string texto, Guid exceptoPersonaId, int tamano, CancellationToken ct);
}
