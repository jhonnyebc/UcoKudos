using UcoKudos.Domain.Identidad;

namespace UcoKudos.UnitTests.Domain.Identidad;

/// <summary>
/// La cadena de administración del enunciado, en una tabla: cada rol administra a quienes aprueba,
/// dentro de su ámbito, y nadie se administra a sí mismo.
/// </summary>
public sealed class CadenaDeAdministracionTests
{
    private static readonly Guid Facultad = Guid.NewGuid();
    private static readonly Guid OtraFacultad = Guid.NewGuid();
    private static readonly Guid Programa = Guid.NewGuid();
    private static readonly Guid OtroPrograma = Guid.NewGuid();

    [Theory]
    [InlineData(5, new short[] { 6, 7 })] // Coordinador de Programa → Docente y Estudiante
    [InlineData(4, new short[] { 5 })]    // Coordinador de Facultad → Coordinador de Programa
    [InlineData(3, new short[] { 4 })]    // Coordinador Académico → Coordinador de Facultad
    [InlineData(2, new short[] { 3 })]    // Rector → Coordinador Académico
    [InlineData(1, new short[] { 1, 2 })] // Administrador → Administrador y Rector
    [InlineData(6, new short[] { })]
    [InlineData(7, new short[] { })]
    public void Cada_rol_administra_a_los_roles_que_aprueba(short rolId, short[] esperados)
    {
        var roles = JerarquiaInstitucional.RolesQueAdministra(Rol.Por(rolId)).Select(r => r.Id).Order().ToArray();

        Assert.Equal(esperados.Order().ToArray(), roles);
    }

    [Fact]
    public void El_coordinador_de_programa_administra_a_los_de_su_programa_y_a_nadie_mas()
    {
        var coordinador = new AmbitoUsuario(Guid.NewGuid(), Rol.CoordinadorPrograma, Facultad, Programa);

        Assert.True(JerarquiaInstitucional.PuedeAdministrar(coordinador, Guid.NewGuid(), Rol.Estudiante, Facultad, Programa));
        Assert.True(JerarquiaInstitucional.PuedeAdministrar(coordinador, Guid.NewGuid(), Rol.Docente, Facultad, Programa));
        Assert.False(JerarquiaInstitucional.PuedeAdministrar(coordinador, Guid.NewGuid(), Rol.Estudiante, Facultad, OtroPrograma));
        Assert.False(JerarquiaInstitucional.PuedeAdministrar(coordinador, Guid.NewGuid(), Rol.CoordinadorPrograma, Facultad, Programa));
    }

    [Fact]
    public void El_coordinador_de_facultad_administra_a_los_coordinadores_de_programa_de_su_facultad()
    {
        var decana = new AmbitoUsuario(Guid.NewGuid(), Rol.CoordinadorFacultad, Facultad, null);

        Assert.True(JerarquiaInstitucional.PuedeAdministrar(decana, Guid.NewGuid(), Rol.CoordinadorPrograma, Facultad, Programa));
        Assert.False(JerarquiaInstitucional.PuedeAdministrar(decana, Guid.NewGuid(), Rol.CoordinadorPrograma, OtraFacultad, OtroPrograma));

        // Un estudiante es del escalón de abajo del coordinador de programa, no de la decana.
        Assert.False(JerarquiaInstitucional.PuedeAdministrar(decana, Guid.NewGuid(), Rol.Estudiante, Facultad, Programa));
    }

    [Fact]
    public void Los_roles_de_universidad_administran_a_su_escalon_en_toda_la_universidad()
    {
        var academico = new AmbitoUsuario(Guid.NewGuid(), Rol.CoordinadorAcademico, null, null);
        var administrador = new AmbitoUsuario(Guid.NewGuid(), Rol.Administrador, null, null);

        Assert.True(JerarquiaInstitucional.PuedeAdministrar(academico, Guid.NewGuid(), Rol.CoordinadorFacultad, OtraFacultad, null));
        Assert.True(JerarquiaInstitucional.PuedeAdministrar(administrador, Guid.NewGuid(), Rol.Rector, null, null));
        Assert.True(JerarquiaInstitucional.PuedeAdministrar(administrador, Guid.NewGuid(), Rol.Administrador, null, null));
    }

    [Fact]
    public void Nadie_se_administra_a_si_mismo_ni_siquiera_el_administrador()
    {
        var administrador = new AmbitoUsuario(Guid.NewGuid(), Rol.Administrador, null, null);

        Assert.False(JerarquiaInstitucional.PuedeAdministrar(administrador, administrador.PersonaId, Rol.Administrador, null, null));
    }
}
