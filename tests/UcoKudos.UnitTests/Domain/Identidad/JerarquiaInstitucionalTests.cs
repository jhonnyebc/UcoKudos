using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.UnitTests.Domain.Identidad;

/// <summary>
/// La jerarquía institucional es la parte más característica del enunciado y la razón por la que
/// la autorización vive en el dominio (ADR-0004). Estas pruebas son la evidencia de que esa
/// decisión paga: recorren las reglas de RF-03, RF-04, RF-06 y RF-12 sin levantar una base de
/// datos ni un servidor.
/// </summary>
public sealed class JerarquiaInstitucionalTests
{
    private static readonly Guid Facultad = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtraFacultad = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Programa = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtroPrograma = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>
    /// RF-03, la tabla del enunciado completa. Se prueba como tabla de casos porque es exactamente
    /// lo que el enunciado especifica, incluida la fila del Administrador que se aprueba a sí mismo.
    /// </summary>
    [Theory]
    [InlineData(7, 5)] // Estudiante              → Coordinador de Programa
    [InlineData(6, 5)] // Docente                 → Coordinador de Programa
    [InlineData(5, 4)] // Coordinador de Programa → Coordinador de Facultad
    [InlineData(4, 3)] // Coordinador de Facultad → Coordinador Académico
    [InlineData(3, 2)] // Coordinador Académico   → Rector
    [InlineData(2, 1)] // Rector                  → Administrador
    [InlineData(1, 1)] // Administrador           → Administrador (se aprueba a sí mismo)
    public void El_flujo_de_aprobaciones_del_enunciado_asigna_el_aprobador_correcto(short rolSolicitado, short rolAprobadorEsperado)
    {
        var aprobador = JerarquiaInstitucional.AprobadorDe(Rol.Por(rolSolicitado));

        Assert.Equal(rolAprobadorEsperado, aprobador.Id);
    }

    [Fact]
    public void Un_coordinador_de_programa_aprueba_estudiantes_de_su_programa()
    {
        var coordinador = new AmbitoUsuario(Guid.NewGuid(), Rol.CoordinadorPrograma, Facultad, Programa);

        Assert.True(JerarquiaInstitucional.PuedeAprobar(coordinador, Rol.Estudiante, Facultad, Programa));
    }

    /// <summary>
    /// El caso que el enunciado detalla: el ámbito limita. Un coordinador administra a los
    /// estudiantes **de sus programas**, no a todos.
    /// </summary>
    [Fact]
    public void Un_coordinador_de_programa_no_aprueba_estudiantes_de_otro_programa()
    {
        var coordinador = new AmbitoUsuario(Guid.NewGuid(), Rol.CoordinadorPrograma, Facultad, Programa);

        Assert.False(JerarquiaInstitucional.PuedeAprobar(coordinador, Rol.Estudiante, Facultad, OtroPrograma));
    }

    [Fact]
    public void Un_coordinador_de_facultad_no_aprueba_estudiantes_porque_no_es_su_nivel()
    {
        var coordinador = new AmbitoUsuario(Guid.NewGuid(), Rol.CoordinadorFacultad, Facultad, null);

        // Saltarse un nivel no vale: al estudiante lo aprueba el coordinador de programa.
        Assert.False(JerarquiaInstitucional.PuedeAprobar(coordinador, Rol.Estudiante, Facultad, Programa));
    }

    [Fact]
    public void Un_rol_de_universidad_manda_sobre_cualquier_ambito()
    {
        var rector = new AmbitoUsuario(Guid.NewGuid(), Rol.Rector, null, null);
        var estudiante = new SujetoAfectado(Guid.NewGuid(), Rol.Estudiante, OtraFacultad, OtroPrograma);

        Assert.True(JerarquiaInstitucional.MandaSobre(rector, estudiante));
    }

    [Fact]
    public void Un_coordinador_de_facultad_no_manda_sobre_otra_facultad()
    {
        var coordinador = new AmbitoUsuario(Guid.NewGuid(), Rol.CoordinadorFacultad, Facultad, null);
        var sujeto = new SujetoAfectado(Guid.NewGuid(), Rol.CoordinadorPrograma, OtraFacultad, OtroPrograma);

        Assert.False(JerarquiaInstitucional.MandaSobre(coordinador, sujeto));
    }

    [Fact]
    public void Nadie_manda_sobre_si_mismo()
    {
        var id = Guid.NewGuid();
        var usuario = new AmbitoUsuario(id, Rol.Rector, null, null);
        var elMismo = new SujetoAfectado(id, Rol.Rector, null, null);

        // Impide que alguien se cambie su propio rol o se desactive.
        Assert.False(JerarquiaInstitucional.MandaSobre(usuario, elMismo));
    }

    [Fact]
    public void Nadie_manda_sobre_un_rol_de_su_mismo_nivel()
    {
        var uno = new AmbitoUsuario(Guid.NewGuid(), Rol.CoordinadorPrograma, Facultad, Programa);
        var otro = new SujetoAfectado(Guid.NewGuid(), Rol.CoordinadorPrograma, Facultad, Programa);

        Assert.False(JerarquiaInstitucional.MandaSobre(uno, otro));
    }

    /// <summary>RF-06: solo tres roles administran categorías.</summary>
    [Theory]
    [InlineData(1, true)]  // Administrador
    [InlineData(2, true)]  // Rector
    [InlineData(3, true)]  // Coordinador Académico
    [InlineData(4, false)] // Coordinador de Facultad
    [InlineData(5, false)] // Coordinador de Programa
    [InlineData(6, false)] // Docente
    [InlineData(7, false)] // Estudiante
    public void Solo_tres_roles_administran_categorias(short rolId, bool puede)
    {
        var usuario = AmbitoDe(Rol.Por(rolId));

        Assert.Equal(puede, JerarquiaInstitucional.PuedeAdministrarCategorias(usuario));
    }

    /// <summary>
    /// RF-12: solo Rector y Coordinador Académico vetan.
    ///
    /// El Administrador del Sistema **no** puede, aunque sea el rol más alto. Es contraintuitivo y
    /// está así en el enunciado: moderar contenido es una atribución académica, no técnica. Esta
    /// prueba existe sobre todo para que nadie lo "arregle" por error más adelante.
    /// </summary>
    [Theory]
    [InlineData(1, false)] // Administrador: NO veta
    [InlineData(2, true)]  // Rector
    [InlineData(3, true)]  // Coordinador Académico
    [InlineData(4, false)] // Coordinador de Facultad
    [InlineData(5, false)] // Coordinador de Programa
    [InlineData(6, false)] // Docente
    [InlineData(7, false)] // Estudiante
    public void Solo_el_rector_y_el_coordinador_academico_vetan(short rolId, bool puede)
    {
        var usuario = AmbitoDe(Rol.Por(rolId));

        Assert.Equal(puede, JerarquiaInstitucional.PuedeVetar(usuario));
    }

    [Fact]
    public void Exigir_la_administracion_de_categorias_lanza_un_error_de_autorizacion()
    {
        var docente = AmbitoDe(Rol.Docente);

        var error = Assert.Throws<ExcepcionAutorizacion>(
            () => JerarquiaInstitucional.ExigirAdministracionDeCategorias(docente));

        Assert.Contains("Docente", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Exigir_el_veto_lanza_un_error_de_autorizacion_incluso_al_administrador()
    {
        var administrador = AmbitoDe(Rol.Administrador);

        Assert.Throws<ExcepcionAutorizacion>(() => JerarquiaInstitucional.ExigirVeto(administrador));
    }

    private static AmbitoUsuario AmbitoDe(Rol rol) => rol.Ambito switch
    {
        AmbitoRol.Universidad => new AmbitoUsuario(Guid.NewGuid(), rol, null, null),
        AmbitoRol.Facultad => new AmbitoUsuario(Guid.NewGuid(), rol, Facultad, null),
        _ => new AmbitoUsuario(Guid.NewGuid(), rol, Facultad, Programa)
    };
}
