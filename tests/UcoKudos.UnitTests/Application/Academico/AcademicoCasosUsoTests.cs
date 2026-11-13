using NSubstitute;
using UcoKudos.Application.Academico.CasosUso;
using UcoKudos.Application.Academico.Entrada.Dto;
using UcoKudos.Application.Comun;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Domain.Academico;
using UcoKudos.Domain.Academico.Repositorios;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Auditoria.Repositorios;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Identidad.Repositorios;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.UnitTests.Application.Academico;

/// <summary>
/// Gestión académica (RF-05) a nivel de caso de uso, sin base: qué se ve, quién vincula a quién y
/// cómo se administra la estructura.
/// </summary>
public sealed class AcademicoCasosUsoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private readonly IPersonaRepositorio _personas = Substitute.For<IPersonaRepositorio>();
    private readonly IEstructuraAcademicaRepositorio _academico = Substitute.For<IEstructuraAcademicaRepositorio>();
    private readonly IUnidadTrabajo _unidad = Substitute.For<IUnidadTrabajo>();
    private readonly ICacheConsultas _cache = Substitute.For<ICacheConsultas>();
    private readonly IEventoAuditoriaRepositorio _eventos = Substitute.For<IEventoAuditoriaRepositorio>();

    private ServicioAuditoria Auditoria => new(_eventos);

    private ConsultarProgramasDePersonaCasoUso Consulta => new(_personas, _academico, _unidad, Auditoria, new RelojFijo());
    private GestionarVinculosCasoUso Vinculos => new(_personas, _academico, _unidad, Auditoria, new RelojFijo());
    private AdministrarEstructuraCasoUso Estructura => new(_academico, _unidad, _cache, Auditoria, new RelojFijo());

    private readonly Facultad _ingenierias = new("Ingenierías", "ING", Ahora);
    private readonly Facultad _ciencias = new("Ciencias", "CIE", Ahora);
    private readonly Programa _sistemas;
    private readonly Programa _biologia;

    public AcademicoCasosUsoTests()
    {
        _sistemas = new Programa(_ingenierias.Id, "Ingeniería de Sistemas", "ISIS", Ahora);
        _biologia = new Programa(_ciencias.Id, "Biología", "BIO", Ahora);

        _academico.FacultadPorIdAsync(_ingenierias.Id, Arg.Any<CancellationToken>()).Returns(_ingenierias);
        _academico.FacultadPorIdAsync(_ciencias.Id, Arg.Any<CancellationToken>()).Returns(_ciencias);
        _academico.ProgramaPorIdAsync(_sistemas.Id, Arg.Any<CancellationToken>()).Returns(_sistemas);
        _academico.ProgramaPorIdAsync(_biologia.Id, Arg.Any<CancellationToken>()).Returns(_biologia);
        _academico.ProgramasPorIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => new[] { _sistemas, _biologia }.Where(p => call.Arg<IReadOnlyCollection<Guid>>().Contains(p.Id)).ToList());
    }

    private static ContextoPeticion Como(Rol rol, Guid? personaId = null, Guid? facultadId = null, Guid? programaId = null) =>
        new(new AmbitoUsuario(personaId ?? Guid.NewGuid(), rol, facultadId, programaId), "traza");

    private Persona UnaPersona(Rol rol, Programa? programaPrincipal = null)
    {
        var persona = new Persona($"{Guid.NewGuid():N}@uco.edu.co", "+573001234567", "Laura Pérez", Ahora);
        _personas.PorIdAsync(persona.Id, Arg.Any<CancellationToken>()).Returns(persona);

        var asignacion = programaPrincipal is null
            ? new AsignacionRol(persona.Id, rol, null, null, Ahora)
            : new AsignacionRol(persona.Id, rol, programaPrincipal.FacultadId, programaPrincipal.Id, Ahora);
        _personas.RolActivoAsync(persona.Id, Arg.Any<CancellationToken>()).Returns(asignacion);

        return persona;
    }

    // ------------------------------------------------------------ consultar

    [Fact]
    public async Task Una_persona_ve_su_programa_principal_y_sus_vinculos_adicionales()
    {
        var estudiante = UnaPersona(Rol.Estudiante, _sistemas);
        _academico.VinculosDeAsync(estudiante.Id, Arg.Any<CancellationToken>())
            .Returns([new VinculoAcademico(estudiante.Id, _biologia.Id, TipoVinculo.Matricula)]);

        var ficha = await Consulta.EjecutarAsync(Como(Rol.Estudiante, estudiante.Id), estudiante.Id, CancellationToken.None);

        Assert.Equal(2, ficha.Programas.Count);
        Assert.Equal("Principal", ficha.Programas[0].Relacion);
        Assert.Equal("Matrícula", ficha.Programas[1].Relacion);
        Assert.Equal("Ciencias", ficha.Programas[1].FacultadNombre);
    }

    [Fact]
    public async Task Un_vinculo_que_repite_lo_que_dice_el_rol_no_se_enseña_dos_veces()
    {
        var docente = UnaPersona(Rol.Docente, _sistemas);
        _academico.VinculosDeAsync(docente.Id, Arg.Any<CancellationToken>())
            .Returns([new VinculoAcademico(docente.Id, _sistemas.Id, TipoVinculo.Docencia)]);

        var ficha = await Consulta.EjecutarAsync(Como(Rol.Docente, docente.Id), docente.Id, CancellationToken.None);

        Assert.Single(ficha.Programas);
    }

    [Fact]
    public async Task Un_coordinador_de_facultad_ve_la_facultad_que_acompana()
    {
        var persona = new Persona("decana@uco.edu.co", "+573001234567", "Decana", Ahora);
        _personas.PorIdAsync(persona.Id, Arg.Any<CancellationToken>()).Returns(persona);
        _personas.RolActivoAsync(persona.Id, Arg.Any<CancellationToken>())
            .Returns(new AsignacionRol(persona.Id, Rol.CoordinadorFacultad, _ingenierias.Id, null, Ahora));

        var ficha = await Consulta.EjecutarAsync(
            Como(Rol.CoordinadorFacultad, persona.Id, _ingenierias.Id), persona.Id, CancellationToken.None);

        Assert.Equal("Ingenierías", ficha.FacultadAcompanada);
        Assert.Empty(ficha.Programas);
    }

    [Fact]
    public async Task Un_coordinador_ve_a_los_estudiantes_de_su_programa_y_no_a_los_de_otro()
    {
        var estudiante = UnaPersona(Rol.Estudiante, _sistemas);
        _academico.VinculosDeAsync(estudiante.Id, Arg.Any<CancellationToken>()).Returns([]);

        // Del mismo programa: ve.
        var suyo = Como(Rol.CoordinadorPrograma, facultadId: _ingenierias.Id, programaId: _sistemas.Id);
        Assert.Equal(estudiante.Id, (await Consulta.EjecutarAsync(suyo, estudiante.Id, CancellationToken.None)).PersonaId);

        // De otro programa: no, y queda auditado.
        var ajeno = Como(Rol.CoordinadorPrograma, facultadId: _ciencias.Id, programaId: _biologia.Id);
        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() => Consulta.EjecutarAsync(ajeno, estudiante.Id, CancellationToken.None));
        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.AccesoRechazado));
    }

    [Fact]
    public async Task Un_estudiante_no_ve_los_programas_de_otro_y_una_persona_inexistente_se_rechaza()
    {
        var otro = UnaPersona(Rol.Estudiante, _sistemas);
        _academico.VinculosDeAsync(otro.Id, Arg.Any<CancellationToken>()).Returns([]);

        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() =>
            Consulta.EjecutarAsync(Como(Rol.Estudiante), otro.Id, CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Consulta.EjecutarAsync(Como(Rol.Rector), Guid.NewGuid(), CancellationToken.None));
    }

    // ------------------------------------------------------------- vínculos

    [Fact]
    public async Task El_rector_matricula_a_un_estudiante_en_otro_programa()
    {
        var estudiante = UnaPersona(Rol.Estudiante, _sistemas);

        await Vinculos.AsignarAsync(Como(Rol.Rector), estudiante.Id, new AsignarVinculoDto(_biologia.Id, "Matricula"), CancellationToken.None);

        _academico.Received(1).AgregarVinculo(Arg.Is<VinculoAcademico>(v => v.ProgramaId == _biologia.Id && v.Tipo == TipoVinculo.Matricula));
        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.VinculoAsignar));
        await _unidad.Received(1).ConfirmarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_coordinador_de_programa_solo_vincula_dentro_de_su_programa()
    {
        var docente = UnaPersona(Rol.Docente, _sistemas);
        var coordinador = Como(Rol.CoordinadorPrograma, facultadId: _ingenierias.Id, programaId: _sistemas.Id);

        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() =>
            Vinculos.AsignarAsync(coordinador, docente.Id, new AsignarVinculoDto(_biologia.Id, "Docencia"), CancellationToken.None));
        _academico.DidNotReceive().AgregarVinculo(Arg.Any<VinculoAcademico>());

        await Vinculos.AsignarAsync(coordinador, docente.Id, new AsignarVinculoDto(_sistemas.Id, "Docencia"), CancellationToken.None);
        _academico.Received(1).AgregarVinculo(Arg.Any<VinculoAcademico>());
    }

    [Fact]
    public async Task El_tipo_de_vinculo_tiene_que_ser_de_la_clase_de_persona()
    {
        var docente = UnaPersona(Rol.Docente, _sistemas);
        var estudiante = UnaPersona(Rol.Estudiante, _sistemas);

        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Vinculos.AsignarAsync(Como(Rol.Rector), docente.Id, new AsignarVinculoDto(_biologia.Id, "Matricula"), CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Vinculos.AsignarAsync(Como(Rol.Rector), estudiante.Id, new AsignarVinculoDto(_biologia.Id, "Docencia"), CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Vinculos.AsignarAsync(Como(Rol.Rector), estudiante.Id, new AsignarVinculoDto(_biologia.Id, "Becario"), CancellationToken.None));
    }

    [Fact]
    public async Task No_se_vincula_con_un_programa_inexistente_ni_desactivado_ni_a_una_persona_inexistente()
    {
        var estudiante = UnaPersona(Rol.Estudiante, _sistemas);

        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Vinculos.AsignarAsync(Como(Rol.Rector), estudiante.Id, new AsignarVinculoDto(Guid.NewGuid(), "Matricula"), CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Vinculos.AsignarAsync(Como(Rol.Rector), Guid.NewGuid(), new AsignarVinculoDto(_biologia.Id, "Matricula"), CancellationToken.None));

        _biologia.Desactivar();
        var error = await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Vinculos.AsignarAsync(Como(Rol.Rector), estudiante.Id, new AsignarVinculoDto(_biologia.Id, "Matricula"), CancellationToken.None));
        Assert.Contains("desactivado", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_vinculo_activo_no_se_repite_y_uno_desactivado_se_reactiva_en_lugar_de_duplicarse()
    {
        var estudiante = UnaPersona(Rol.Estudiante, _sistemas);
        var vinculo = new VinculoAcademico(estudiante.Id, _biologia.Id, TipoVinculo.Matricula);
        _academico.VinculoAsync(estudiante.Id, _biologia.Id, TipoVinculo.Matricula, Arg.Any<CancellationToken>()).Returns(vinculo);

        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Vinculos.AsignarAsync(Como(Rol.Rector), estudiante.Id, new AsignarVinculoDto(_biologia.Id, "Matricula"), CancellationToken.None));

        vinculo.Desactivar();
        await Vinculos.AsignarAsync(Como(Rol.Rector), estudiante.Id, new AsignarVinculoDto(_biologia.Id, "Matricula"), CancellationToken.None);

        Assert.True(vinculo.Activo);
        _academico.DidNotReceive().AgregarVinculo(Arg.Any<VinculoAcademico>());
    }

    [Fact]
    public async Task Quitar_un_vinculo_lo_desactiva_y_lo_audita()
    {
        var estudiante = UnaPersona(Rol.Estudiante, _sistemas);
        var vinculo = new VinculoAcademico(estudiante.Id, _biologia.Id, TipoVinculo.Matricula);
        _academico.VinculoAsync(estudiante.Id, _biologia.Id, TipoVinculo.Matricula, Arg.Any<CancellationToken>()).Returns(vinculo);

        await Vinculos.QuitarAsync(Como(Rol.Rector), estudiante.Id, _biologia.Id, "Matricula", CancellationToken.None);

        Assert.False(vinculo.Activo);
        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.VinculoQuitar));
    }

    [Fact]
    public async Task No_se_quita_un_vinculo_que_no_existe_o_que_ya_estaba_quitado_ni_sin_permiso()
    {
        var estudiante = UnaPersona(Rol.Estudiante, _sistemas);

        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Vinculos.QuitarAsync(Como(Rol.Rector), estudiante.Id, _biologia.Id, "Matricula", CancellationToken.None));

        var quitado = new VinculoAcademico(estudiante.Id, _biologia.Id, TipoVinculo.Matricula);
        quitado.Desactivar();
        _academico.VinculoAsync(estudiante.Id, _biologia.Id, TipoVinculo.Matricula, Arg.Any<CancellationToken>()).Returns(quitado);
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Vinculos.QuitarAsync(Como(Rol.Rector), estudiante.Id, _biologia.Id, "Matricula", CancellationToken.None));

        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() =>
            Vinculos.QuitarAsync(Como(Rol.Estudiante), estudiante.Id, _biologia.Id, "Matricula", CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Vinculos.QuitarAsync(Como(Rol.Rector), estudiante.Id, Guid.NewGuid(), "Matricula", CancellationToken.None));
    }

    // ------------------------------------------------------------ estructura

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Administrador_rector_y_coordinador_academico_crean_facultades_y_la_cache_se_invalida(short rolId)
    {
        var creada = await Estructura.CrearFacultadAsync(
            Como(Rol.Por(rolId)), new DatosFacultadDto("Derecho", "der"), CancellationToken.None);

        Assert.Equal("DER", creada.Codigo);
        Assert.True(creada.Activa);
        _academico.Received(1).AgregarFacultad(Arg.Any<Facultad>());
        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.EstructuraCrear));
        await _cache.Received(1).InvalidarGrupoAsync(GruposCache.EstructuraAcademica, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task Los_demas_roles_no_administran_la_estructura(short rolId)
    {
        var contexto = Como(Rol.Por(rolId));

        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() =>
            Estructura.CrearFacultadAsync(contexto, new DatosFacultadDto("Derecho", "DER"), CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() => Estructura.CompletaAsync(contexto, CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() =>
            Estructura.CambiarEstadoProgramaAsync(contexto, _sistemas.Id, false, CancellationToken.None));

        _academico.DidNotReceive().AgregarFacultad(Arg.Any<Facultad>());
        await _cache.DidNotReceive().InvalidarGrupoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_codigo_repetido_se_rechaza_al_crear_y_al_editar()
    {
        _academico.ExisteCodigoDeFacultadAsync("DER", Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _academico.ExisteCodigoDeProgramaAsync("BIO", Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);

        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Estructura.CrearFacultadAsync(Como(Rol.Rector), new DatosFacultadDto("Derecho", "der"), CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Estructura.ActualizarFacultadAsync(Como(Rol.Rector), _ingenierias.Id, new DatosFacultadDto("Derecho", "DER"), CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Estructura.CrearProgramaAsync(Como(Rol.Rector), _ciencias.Id, new DatosProgramaDto("Biología 2", "bio"), CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Estructura.ActualizarProgramaAsync(Como(Rol.Rector), _sistemas.Id, new DatosProgramaDto("Sistemas", "BIO"), CancellationToken.None));
    }

    [Fact]
    public async Task Se_crea_un_programa_dentro_de_una_facultad_que_existe_y_no_de_una_que_no()
    {
        var creado = await Estructura.CrearProgramaAsync(
            Como(Rol.Rector), _ciencias.Id, new DatosProgramaDto("Química", "qui"), CancellationToken.None);

        Assert.Equal("QUI", creado.Codigo);
        Assert.True(creado.Activo);

        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Estructura.CrearProgramaAsync(Como(Rol.Rector), Guid.NewGuid(), new DatosProgramaDto("X", "XXX"), CancellationToken.None));
    }

    [Fact]
    public async Task Se_editan_y_se_activan_o_desactivan_facultades_y_programas()
    {
        await Estructura.ActualizarFacultadAsync(Como(Rol.Rector), _ingenierias.Id, new DatosFacultadDto("Ingenierías y Tecnología", "ingt"), CancellationToken.None);
        await Estructura.ActualizarProgramaAsync(Como(Rol.Rector), _sistemas.Id, new DatosProgramaDto("Sistemas y Computación", "sc"), CancellationToken.None);

        Assert.Equal("INGT", _ingenierias.Codigo);
        Assert.Equal("Sistemas y Computación", _sistemas.Nombre);

        await Estructura.CambiarEstadoFacultadAsync(Como(Rol.Rector), _ingenierias.Id, false, CancellationToken.None);
        await Estructura.CambiarEstadoProgramaAsync(Como(Rol.Rector), _sistemas.Id, false, CancellationToken.None);
        Assert.False(_ingenierias.Activa);
        Assert.False(_sistemas.Activo);

        await Estructura.CambiarEstadoFacultadAsync(Como(Rol.Rector), _ingenierias.Id, true, CancellationToken.None);
        await Estructura.CambiarEstadoProgramaAsync(Como(Rol.Rector), _sistemas.Id, true, CancellationToken.None);
        Assert.True(_ingenierias.Activa);
        Assert.True(_sistemas.Activo);
    }

    [Fact]
    public async Task No_se_edita_ni_se_cambia_el_estado_de_lo_que_no_existe()
    {
        var contexto = Como(Rol.Rector);

        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Estructura.ActualizarFacultadAsync(contexto, Guid.NewGuid(), new DatosFacultadDto("X", "XXX"), CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Estructura.CambiarEstadoFacultadAsync(contexto, Guid.NewGuid(), false, CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Estructura.ActualizarProgramaAsync(contexto, Guid.NewGuid(), new DatosProgramaDto("X", "XXX"), CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Estructura.CambiarEstadoProgramaAsync(contexto, Guid.NewGuid(), false, CancellationToken.None));
    }

    [Fact]
    public async Task La_estructura_completa_incluye_lo_desactivado()
    {
        _sistemas.Desactivar();
        _academico.FacultadesAsync(Arg.Any<CancellationToken>()).Returns([_ingenierias]);
        _academico.ProgramasDeFacultadAsync(_ingenierias.Id, Arg.Any<CancellationToken>()).Returns([_sistemas]);

        var completa = await Estructura.CompletaAsync(Como(Rol.Administrador), CancellationToken.None);

        var facultad = Assert.Single(completa);
        Assert.False(Assert.Single(facultad.Programas).Activo);
    }
}
