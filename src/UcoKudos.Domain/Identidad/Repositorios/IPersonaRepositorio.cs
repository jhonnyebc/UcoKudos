namespace UcoKudos.Domain.Identidad.Repositorios;

/// <summary>
/// Puerto de persistencia de personas y de sus roles. Está declarado **en el dominio**, no en
/// infraestructura: es la inversión de dependencias que permite que el dominio no sepa que existe
/// PostgreSQL (ADR-0001, regla 1).
///
/// Los métodos devuelven agregados o listas, nunca consultas componibles: un repositorio que expone
/// una consulta diferida filtra el ORM al dominio y anula el beneficio del patrón.
///
/// Ningún método guarda: la confirmación es de la unidad de trabajo, para que el cambio, su
/// auditoría y su evento se confirmen juntos (ADR-0005).
/// </summary>
public interface IPersonaRepositorio
{
    Task<Persona?> PorIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Resuelve la persona desde el correo del token. Es la consulta que corre en **cada** petición
    /// para comprobar que la cuenta sigue activa (ADR-0004), así que va por el índice único.
    /// </summary>
    Task<Persona?> PorCorreoAsync(string correoInstitucional, CancellationToken ct);

    Task<bool> ExisteCorreoAsync(string correoInstitucional, CancellationToken ct);

    void Agregar(Persona persona);

    /// <summary>La asignación de rol activa de una persona, con su ámbito. Null si no tiene ninguna.</summary>
    Task<AsignacionRol?> RolActivoAsync(Guid personaId, CancellationToken ct);

    /// <summary>
    /// ¿Hay ya alguien ejerciendo ese rol único en ese ámbito? (RF-04)
    ///
    /// El dominio lo comprueba por cortesía, para dar un mensaje claro. La garantía real la da el
    /// índice único parcial en la base: una comprobación en la aplicación no resiste dos peticiones
    /// simultáneas.
    /// </summary>
    Task<bool> ExisteRolActivoAsync(short rolId, Guid? facultadId, Guid? programaId, CancellationToken ct);

    void AgregarRol(AsignacionRol asignacion);

    /// <summary>El código de verificación más reciente de un tipo, sin consumir. Null si no hay.</summary>
    Task<VerificacionContacto?> VerificacionVigenteAsync(Guid personaId, TipoContacto tipo, CancellationToken ct);

    void AgregarVerificacion(VerificacionContacto verificacion);

    /// <summary>
    /// Las personas con un rol activo de esos, dentro del ámbito indicado (sin ámbito, todas). Es el
    /// listado de quien administra: el filtro final por jerarquía lo hace cada acción.
    /// </summary>
    Task<IReadOnlyList<(Persona Persona, AsignacionRol Rol)>> AdministradasAsync(
        IReadOnlyCollection<short> rolesAdministrados, Guid? facultadId, Guid? programaId, string? texto, int tamano, CancellationToken ct);

    /// <summary>Personas con una solicitud pendiente dentro de un ámbito, para que su aprobador las vea.</summary>
    Task<IReadOnlyList<Persona>> PendientesDeAprobacionAsync(Guid? facultadId, Guid? programaId, CancellationToken ct);
}
