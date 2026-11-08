using UcoKudos.Domain.Identidad;

namespace UcoKudos.Application.Comun;

/// <summary>
/// Quién hace la petición y con qué identificador de correlación.
///
/// Se pasa como parámetro a los casos de uso en lugar de resolverse dentro, por la misma razón por
/// la que el tiempo entra como parámetro (ADR-0001): un caso de uso que consulta por su cuenta quién
/// es el usuario no se puede probar sin montar una petición HTTP.
///
/// El <see cref="CorrelacionId"/> viaja hasta la auditoría y hasta los eventos de la bandeja, y es
/// lo que permite reconstruir una publicación concreta de punta a punta (QA-OBS-01, ADR-0014).
/// </summary>
/// <param name="Ambito">Rol y ámbito del usuario, ya resueltos contra la base (ADR-0004).</param>
/// <param name="CorrelacionId">Identificador de la petición, generado en el borde de entrada.</param>
public sealed record ContextoPeticion(AmbitoUsuario Ambito, string CorrelacionId)
{
    public Guid PersonaId => Ambito.PersonaId;

    public Rol Rol => Ambito.Rol;
}
