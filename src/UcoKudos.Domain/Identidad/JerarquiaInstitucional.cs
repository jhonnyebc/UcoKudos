using UcoKudos.Domain.Transversal;

namespace UcoKudos.Domain.Identidad;

/// <summary>
/// Ámbito concreto en el que un usuario ejerce su rol: el rol más la facultad o el programa que le
/// corresponden. Es lo que el servicio de la jerarquía necesita para responder sus dos preguntas.
/// </summary>
public sealed record AmbitoUsuario(Guid PersonaId, Rol Rol, Guid? FacultadId, Guid? ProgramaId)
{
    public static AmbitoUsuario De(AsignacionRol asignacion) =>
        new(asignacion.PersonaId, asignacion.Rol, asignacion.FacultadId, asignacion.ProgramaId);
}

/// <summary>
/// Datos de la persona sobre la que se quiere actuar, con el ámbito donde está.
/// </summary>
public sealed record SujetoAfectado(Guid PersonaId, Rol Rol, Guid? FacultadId, Guid? ProgramaId);

/// <summary>
/// Servicio de dominio de la jerarquía institucional. Responde exactamente dos preguntas:
///
///   1. ¿Quién aprueba a quién?       (RF-03)
///   2. ¿Sobre quién manda este usuario? (RF-04 a RF-06, RF-12)
///
/// Vive en el dominio, y no en los claims de un token ni en la configuración del proveedor de
/// identidad, por tres razones (ADR-0004): es la parte más característica del enunciado, el ámbito
/// por facultad y programa no cabe de forma mantenible en un token, y aquí se puede probar con
/// pruebas unitarias rápidas sin levantar nada.
///
/// No tiene estado y no consulta la base: recibe los ámbitos ya resueltos. Eso es lo que lo hace
/// comprobable con una tabla de casos.
/// </summary>
public static class JerarquiaInstitucional
{
    /// <summary>
    /// RF-03: qué rol aprueba las solicitudes de cada rol. La tabla del enunciado, literal:
    ///
    ///   Estudiante               → Coordinador de Programa
    ///   Docente                  → Coordinador de Programa
    ///   Coordinador de Programa  → Coordinador de Facultad
    ///   Coordinador de Facultad  → Coordinador Académico
    ///   Coordinador Académico    → Rector
    ///   Rector                   → Administrador
    ///   Administrador            → Administrador  (se aprueba a sí mismo)
    /// </summary>
    public static Rol AprobadorDe(Rol rolSolicitado)
    {
        if (rolSolicitado == Rol.Estudiante || rolSolicitado == Rol.Docente)
            return Rol.CoordinadorPrograma;
        if (rolSolicitado == Rol.CoordinadorPrograma)
            return Rol.CoordinadorFacultad;
        if (rolSolicitado == Rol.CoordinadorFacultad)
            return Rol.CoordinadorAcademico;
        if (rolSolicitado == Rol.CoordinadorAcademico)
            return Rol.Rector;

        // Rector y Administrador los aprueba el Administrador. El caso del Administrador que se
        // aprueba a sí mismo está en el enunciado y es deliberado: alguien tiene que poder empezar.
        return Rol.Administrador;
    }

    /// <summary>
    /// ¿Puede este usuario aprobar la solicitud de ese rol, en ese ámbito? Exige dos cosas: que su
    /// rol sea el aprobador que corresponde, y que el sujeto esté dentro de su ámbito.
    /// </summary>
    public static bool PuedeAprobar(AmbitoUsuario aprobador, Rol rolSolicitado, Guid? facultadDelSujeto, Guid? programaDelSujeto)
    {
        if (aprobador.Rol != AprobadorDe(rolSolicitado))
            return false;

        return aprobador.Rol.Ambito switch
        {
            AmbitoRol.Universidad => true,
            AmbitoRol.Facultad => facultadDelSujeto is not null && facultadDelSujeto == aprobador.FacultadId,
            AmbitoRol.Programa => programaDelSujeto is not null && programaDelSujeto == aprobador.ProgramaId,
            _ => false
        };
    }

    /// <summary>
    /// ¿Manda este usuario sobre esa persona? Dos condiciones: su rol está por encima en la
    /// jerarquía, y el sujeto cae dentro de su ámbito.
    ///
    /// Esta es la regla que impide que un Coordinador de Programa administre estudiantes de otro
    /// programa, que es el caso que el enunciado detalla.
    /// </summary>
    public static bool MandaSobre(AmbitoUsuario usuario, SujetoAfectado sujeto)
    {
        if (usuario.PersonaId == sujeto.PersonaId)
            return false;

        if (usuario.Rol.Nivel <= sujeto.Rol.Nivel)
            return false;

        return usuario.Rol.Ambito switch
        {
            AmbitoRol.Universidad => true,
            AmbitoRol.Facultad => sujeto.FacultadId is not null && sujeto.FacultadId == usuario.FacultadId,
            AmbitoRol.Programa => sujeto.ProgramaId is not null && sujeto.ProgramaId == usuario.ProgramaId,
            _ => false
        };
    }

    /// <summary>RF-06: administrar categorías. Solo Administrador, Rector y Coordinador Académico.</summary>
    public static bool PuedeAdministrarCategorias(AmbitoUsuario usuario) => usuario.Rol.AdministraCategorias;

    /// <summary>
    /// ¿Administra este usuario a esa persona? La cadena del enunciado, literal —es la misma de las
    /// aprobaciones—: cada rol administra a quienes aprueba, dentro de su ámbito. Nadie se administra
    /// a sí mismo.
    /// </summary>
    public static bool PuedeAdministrar(
        AmbitoUsuario usuario, Guid personaId, Rol rolDeLaPersona, Guid? facultadDeLaPersona, Guid? programaDeLaPersona) =>
        usuario.PersonaId != personaId
        && PuedeAprobar(usuario, rolDeLaPersona, facultadDeLaPersona, programaDeLaPersona);

    /// <summary>Los roles que un rol administra: los que él aprueba, salvo que el Administrador no es su propio superior.</summary>
    public static IReadOnlyList<Rol> RolesQueAdministra(Rol rolDelUsuario) =>
        [.. Rol.Todos.Where(r => AprobadorDe(r) == rolDelUsuario)];

    /// <summary>
    /// Ver los indicadores de gestión: desde el Coordinador de Programa hacia arriba. Docentes y
    /// estudiantes no: son una herramienta para quien gestiona, no un dato de consulta.
    /// </summary>
    public static bool PuedeVerIndicadores(AmbitoUsuario usuario) => usuario.Rol.Nivel >= Rol.CoordinadorPrograma.Nivel;

    /// <summary>
    /// Administrar facultades y programas. El enunciado no lo fija: se asigna a los mismos tres roles
    /// que administran las categorías, por ser decisiones de toda la universidad.
    /// </summary>
    public static bool PuedeAdministrarEstructura(AmbitoUsuario usuario) => usuario.Rol.AdministraCategorias;

    /// <summary>¿Cae este programa dentro del ámbito del usuario?</summary>
    public static bool AlcanzaPrograma(AmbitoUsuario usuario, Guid programaId, Guid facultadDelPrograma) =>
        usuario.Rol.Ambito switch
        {
            AmbitoRol.Universidad => true,
            AmbitoRol.Facultad => usuario.FacultadId is not null && usuario.FacultadId == facultadDelPrograma,
            AmbitoRol.Programa => usuario.ProgramaId is not null && usuario.ProgramaId == programaId,
            _ => false
        };

    /// <summary>
    /// RF-05: vincular a una persona con un programa. Quien lo hace tiene que estar por encima de ella
    /// en la jerarquía —nadie se vincula a sí mismo— y alcanzar el programa del vínculo.
    /// </summary>
    public static bool PuedeGestionarVinculos(
        AmbitoUsuario usuario, Guid personaId, Rol rolDeLaPersona, Guid programaId, Guid facultadDelPrograma) =>
        usuario.PersonaId != personaId
        && usuario.Rol.Nivel > rolDeLaPersona.Nivel
        && AlcanzaPrograma(usuario, programaId, facultadDelPrograma);

    /// <summary>
    /// ¿Puede el usuario ver los programas de esa persona? Ella misma sí; otro, solo si está por
    /// encima en la jerarquía y alcanza al menos uno de los programas de la persona.
    /// </summary>
    public static bool PuedeVerProgramasDe(
        AmbitoUsuario usuario, Guid personaId, Rol rolDeLaPersona, IEnumerable<(Guid ProgramaId, Guid FacultadId)> programas)
    {
        if (usuario.PersonaId == personaId)
            return true;

        return usuario.Rol.Nivel > rolDeLaPersona.Nivel
               && programas.Any(p => AlcanzaPrograma(usuario, p.ProgramaId, p.FacultadId));
    }

    /// <summary>
    /// RF-12: vetar contenido. Solo Rector y Coordinador Académico.
    ///
    /// El Administrador del Sistema **no** puede vetar, aunque sea el rol más alto. Es
    /// contraintuitivo y está así en el enunciado: moderar contenido es una atribución académica,
    /// no técnica.
    /// </summary>
    public static bool PuedeVetar(AmbitoUsuario usuario) => usuario.Rol.PuedeVetar;

    /// <summary>
    /// Lanza si el usuario no puede administrar categorías. Los casos de uso llaman a esto en lugar
    /// de comprobar la bandera, para que el mensaje de error sea uno solo.
    /// </summary>
    public static void ExigirAdministracionDeCategorias(AmbitoUsuario usuario)
    {
        if (!PuedeAdministrarCategorias(usuario))
            throw new ExcepcionAutorizacion("El rol " + usuario.Rol.Nombre + " no administra categorías.");
    }

    /// <summary>Lanza si el usuario no puede vetar contenido.</summary>
    public static void ExigirVeto(AmbitoUsuario usuario)
    {
        if (!PuedeVetar(usuario))
            throw new ExcepcionAutorizacion("El rol " + usuario.Rol.Nombre + " no puede vetar contenido.");
    }
}
