using UcoKudos.Domain.Identidad;

namespace UcoKudos.UnitTests.Domain.Identidad;

/// <summary>
/// Las reglas de alcance de la gestión académica (RF-05): quién alcanza qué programa y a quién puede
/// vincular o ver. Son las que impiden que un coordinador administre a las personas de otro programa.
/// </summary>
public sealed class JerarquiaAcademicaTests
{
    private static readonly Guid FacultadA = Guid.NewGuid();
    private static readonly Guid FacultadB = Guid.NewGuid();
    private static readonly Guid ProgramaA = Guid.NewGuid();
    private static readonly Guid ProgramaB = Guid.NewGuid();

    private static AmbitoUsuario Usuario(Rol rol, Guid? facultad = null, Guid? programa = null) =>
        new(Guid.NewGuid(), rol, facultad, programa);

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    public void Solo_administrador_rector_y_coordinador_academico_administran_la_estructura(short rolId, bool puede)
    {
        Assert.Equal(puede, JerarquiaInstitucional.PuedeAdministrarEstructura(Usuario(Rol.Por(rolId))));
    }

    [Fact]
    public void Los_roles_de_universidad_alcanzan_todo_el_de_facultad_solo_la_suya_y_el_de_programa_solo_el_suyo()
    {
        Assert.True(JerarquiaInstitucional.AlcanzaPrograma(Usuario(Rol.Rector), ProgramaB, FacultadB));

        var decana = Usuario(Rol.CoordinadorFacultad, FacultadA);
        Assert.True(JerarquiaInstitucional.AlcanzaPrograma(decana, ProgramaA, FacultadA));
        Assert.False(JerarquiaInstitucional.AlcanzaPrograma(decana, ProgramaB, FacultadB));

        var coordinador = Usuario(Rol.CoordinadorPrograma, FacultadA, ProgramaA);
        Assert.True(JerarquiaInstitucional.AlcanzaPrograma(coordinador, ProgramaA, FacultadA));
        Assert.False(JerarquiaInstitucional.AlcanzaPrograma(coordinador, ProgramaB, FacultadA));
    }

    [Fact]
    public void Un_coordinador_sin_ambito_no_alcanza_nada()
    {
        Assert.False(JerarquiaInstitucional.AlcanzaPrograma(Usuario(Rol.CoordinadorFacultad), ProgramaA, FacultadA));
        Assert.False(JerarquiaInstitucional.AlcanzaPrograma(Usuario(Rol.CoordinadorPrograma), ProgramaA, FacultadA));
    }

    [Fact]
    public void Un_estudiante_no_alcanza_ningun_programa_que_no_sea_el_suyo()
    {
        // El ámbito de un estudiante es de programa, pero el nivel lo deja fuera de toda gestión.
        var estudiante = Usuario(Rol.Estudiante, FacultadA, ProgramaA);

        Assert.False(JerarquiaInstitucional.PuedeGestionarVinculos(estudiante, Guid.NewGuid(), Rol.Estudiante, ProgramaA, FacultadA));
    }

    [Fact]
    public void Para_vincular_hay_que_estar_por_encima_de_la_persona_y_alcanzar_el_programa()
    {
        var coordinador = Usuario(Rol.CoordinadorPrograma, FacultadA, ProgramaA);

        Assert.True(JerarquiaInstitucional.PuedeGestionarVinculos(coordinador, Guid.NewGuid(), Rol.Estudiante, ProgramaA, FacultadA));
        Assert.True(JerarquiaInstitucional.PuedeGestionarVinculos(coordinador, Guid.NewGuid(), Rol.Docente, ProgramaA, FacultadA));

        // Otro programa: no lo alcanza.
        Assert.False(JerarquiaInstitucional.PuedeGestionarVinculos(coordinador, Guid.NewGuid(), Rol.Estudiante, ProgramaB, FacultadB));

        // Un igual o un superior: no está por encima.
        Assert.False(JerarquiaInstitucional.PuedeGestionarVinculos(coordinador, Guid.NewGuid(), Rol.CoordinadorPrograma, ProgramaA, FacultadA));
        Assert.False(JerarquiaInstitucional.PuedeGestionarVinculos(coordinador, Guid.NewGuid(), Rol.CoordinadorFacultad, ProgramaA, FacultadA));
    }

    [Fact]
    public void Nadie_se_vincula_a_si_mismo()
    {
        var rector = Usuario(Rol.Rector);

        Assert.False(JerarquiaInstitucional.PuedeGestionarVinculos(rector, rector.PersonaId, Rol.Estudiante, ProgramaA, FacultadA));
    }

    [Fact]
    public void Cada_uno_ve_sus_programas_y_otro_solo_si_esta_por_encima_y_alcanza_alguno()
    {
        var estudianteId = Guid.NewGuid();
        var programas = new[] { (ProgramaA, FacultadA) };

        // Ella misma.
        Assert.True(JerarquiaInstitucional.PuedeVerProgramasDe(
            new AmbitoUsuario(estudianteId, Rol.Estudiante, FacultadA, ProgramaA), estudianteId, Rol.Estudiante, programas));

        // Su coordinador.
        Assert.True(JerarquiaInstitucional.PuedeVerProgramasDe(
            Usuario(Rol.CoordinadorPrograma, FacultadA, ProgramaA), estudianteId, Rol.Estudiante, programas));

        // El coordinador de otro programa, no.
        Assert.False(JerarquiaInstitucional.PuedeVerProgramasDe(
            Usuario(Rol.CoordinadorPrograma, FacultadB, ProgramaB), estudianteId, Rol.Estudiante, programas));

        // Un compañero, no, aunque comparta programa.
        Assert.False(JerarquiaInstitucional.PuedeVerProgramasDe(
            Usuario(Rol.Estudiante, FacultadA, ProgramaA), estudianteId, Rol.Estudiante, programas));

        // Si alguien está en varios programas, basta con alcanzar uno.
        Assert.True(JerarquiaInstitucional.PuedeVerProgramasDe(
            Usuario(Rol.CoordinadorPrograma, FacultadB, ProgramaB), estudianteId, Rol.Estudiante,
            [(ProgramaA, FacultadA), (ProgramaB, FacultadB)]));
    }
}
