using UcoKudos.Domain.Transversal;

namespace UcoKudos.Domain.Identidad;

/// <summary>
/// Rol que una persona ejerce, con su ámbito. Es la entidad que materializa las dos restricciones
/// organizacionales del enunciado (RF-04, RN-04):
///
///   Exclusividad: una persona tiene un único rol activo.
///   Unicidad: un Rector y un Coordinador Académico activos en la universidad, uno por facultad y
///             uno por programa.
///
/// El dominio las defiende, y además las defienden índices únicos parciales en la base (ADR-0003):
/// una validación en la aplicación no resiste dos peticiones simultáneas, un índice único sí.
/// </summary>
public sealed class AsignacionRol
{
    private AsignacionRol()
    {
        // Solo para el materializador de la persistencia.
    }

    public AsignacionRol(Guid personaId, Rol rol, Guid? facultadId, Guid? programaId, DateTimeOffset ahora)
    {
        ExigirAmbitoCoherente(rol, facultadId, programaId);

        Id = Guid.CreateVersion7(ahora);
        PersonaId = personaId;
        RolId = rol.Id;
        FacultadId = facultadId;
        ProgramaId = programaId;
        Activa = true;
        Desde = ahora;
    }

    public Guid Id { get; private set; }
    public Guid PersonaId { get; private set; }
    public short RolId { get; private set; }
    public Guid? FacultadId { get; private set; }
    public Guid? ProgramaId { get; private set; }
    public bool Activa { get; private set; }
    public DateTimeOffset Desde { get; private set; }
    public DateTimeOffset? Hasta { get; private set; }

    public Rol Rol => Rol.Por(RolId);

    public void Revocar(DateTimeOffset ahora)
    {
        if (!Activa)
            throw new ExcepcionDominio("Esta asignación de rol ya estaba revocada.");

        Activa = false;
        Hasta = ahora;
    }

    /// <summary>
    /// El ámbito tiene que corresponder al rol: uno de programa exige programa, uno de facultad
    /// exige facultad, y uno de universidad no admite ninguno de los dos. Sin esta regla se podría
    /// crear un Rector «de la facultad de ingeniería», que no significa nada.
    /// </summary>
    private static void ExigirAmbitoCoherente(Rol rol, Guid? facultadId, Guid? programaId)
    {
        switch (rol.Ambito)
        {
            case AmbitoRol.Universidad when facultadId is not null || programaId is not null:
                throw new ExcepcionDominio("El rol " + rol.Nombre + " es de toda la universidad: no lleva facultad ni programa.");
            case AmbitoRol.Facultad when facultadId is null:
                throw new ExcepcionDominio("El rol " + rol.Nombre + " necesita la facultad que acompaña.");
            case AmbitoRol.Facultad when programaId is not null:
                throw new ExcepcionDominio("El rol " + rol.Nombre + " es de facultad: no lleva programa.");
            case AmbitoRol.Programa when programaId is null:
                throw new ExcepcionDominio("El rol " + rol.Nombre + " necesita el programa que acompaña.");
            default:
                break;
        }
    }
}
