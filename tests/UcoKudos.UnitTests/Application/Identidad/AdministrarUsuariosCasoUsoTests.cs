using NSubstitute;
using UcoKudos.Application.Comun;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Application.Identidad.CasosUso;
using UcoKudos.Application.Identidad.Entrada.Dto;
using UcoKudos.Domain.Academico;
using UcoKudos.Domain.Academico.Repositorios;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Auditoria.Repositorios;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Identidad.Repositorios;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.UnitTests.Application.Identidad;

/// <summary>
/// Administración de usuarios a nivel de caso de uso: la cadena de quién administra a quién, el
/// efecto de desactivar y reactivar, y las reglas del cambio de rol.
/// </summary>
public sealed class AdministrarUsuariosCasoUsoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private readonly IPersonaRepositorio _personas = Substitute.For<IPersonaRepositorio>();
    private readonly IEstructuraAcademicaRepositorio _academico = Substitute.For<IEstructuraAcademicaRepositorio>();
    private readonly IUnidadTrabajo _unidad = Substitute.For<IUnidadTrabajo>();
    private readonly IEventoAuditoriaRepositorio _eventos = Substitute.For<IEventoAuditoriaRepositorio>();

    private readonly Facultad _facultad = new("Ingenierías", "ING", Ahora);
    private readonly Programa _sistemas;
    private readonly Programa _civil;

    public AdministrarUsuariosCasoUsoTests()
    {
        _sistemas = new Programa(_facultad.Id, "Sistemas", "ISIS", Ahora);
        _civil = new Programa(_facultad.Id, "Civil", "CIV", Ahora);
        _academico.FacultadPorIdAsync(_facultad.Id, Arg.Any<CancellationToken>()).Returns(_facultad);
        _academico.ProgramaPorIdAsync(_sistemas.Id, Arg.Any<CancellationToken>()).Returns(_sistemas);
        _academico.ProgramaPorIdAsync(_civil.Id, Arg.Any<CancellationToken>()).Returns(_civil);
    }

    private AdministrarUsuariosCasoUso CasoDeUso =>
        new(_personas, _academico, _unidad, new ServicioAuditoria(_eventos), new RelojFijo());

    private static ContextoPeticion Como(Rol rol, Guid? facultadId = null, Guid? programaId = null, Guid? personaId = null) =>
        new(new AmbitoUsuario(personaId ?? Guid.NewGuid(), rol, facultadId, programaId), "traza");

    /// <summary>Una persona activa con su rol, ya cargable por el repositorio.</summary>
    private (Persona Persona, AsignacionRol Asignacion) UnaPersona(Rol rol, Programa? programa = null, bool activa = true)
    {
        var persona = new Persona($"{Guid.NewGuid():N}@uco.edu.co", "+573001234567", "Laura Pérez", Ahora);
        if (activa)
        {
            persona.AprobarSolicitud();
            persona.VerificarCorreo();
            persona.VerificarCelular();
        }

        var asignacion = programa is null
            ? new AsignacionRol(persona.Id, rol, rol.Ambito == AmbitoRol.Facultad ? _facultad.Id : null, null, Ahora)
            : new AsignacionRol(persona.Id, rol, programa.FacultadId, programa.Id, Ahora);

        _personas.PorIdAsync(persona.Id, Arg.Any<CancellationToken>()).Returns(persona);
        _personas.RolActivoAsync(persona.Id, Arg.Any<CancellationToken>()).Returns(asignacion);

        return (persona, asignacion);
    }

    private ContextoPeticion CoordinadorDeSistemas() => Como(Rol.CoordinadorPrograma, _facultad.Id, _sistemas.Id);

    // --------------------------------------------------------------- listado

    [Fact]
    public async Task El_listado_pide_los_roles_que_el_rol_administra_con_su_ambito_y_excluye_a_quien_consulta()
    {
        var yo = Guid.NewGuid();
        var otra = new Persona("otra@uco.edu.co", "+573001234567", "Otra Persona", Ahora);
        var propia = new Persona("yo@uco.edu.co", "+573001234568", "Quien Consulta", Ahora);
        _personas.AdministradasAsync(Arg.Any<IReadOnlyCollection<short>>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                (otra, new AsignacionRol(otra.Id, Rol.Docente, _facultad.Id, _sistemas.Id, Ahora)),
                (propia, new AsignacionRol(yo, Rol.Docente, _facultad.Id, _sistemas.Id, Ahora))
            ]);

        var lista = await CasoDeUso.AdministradosAsync(
            Como(Rol.CoordinadorPrograma, _facultad.Id, _sistemas.Id, propia.Id), "  per ", CancellationToken.None);

        Assert.Equal("Otra Persona", Assert.Single(lista).NombreCompleto);

        await _personas.Received(1).AdministradasAsync(
            Arg.Is<IReadOnlyCollection<short>>(r => r.Contains(Rol.Estudiante.Id) && r.Contains(Rol.Docente.Id) && r.Count == 2),
            null, _sistemas.Id, "per", AdministrarUsuariosCasoUso.TamanoMaximoDeLista, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_coordinador_de_facultad_lista_con_el_ambito_de_su_facultad_y_el_rector_sin_ambito()
    {
        _personas.AdministradasAsync(Arg.Any<IReadOnlyCollection<short>>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await CasoDeUso.AdministradosAsync(Como(Rol.CoordinadorFacultad, _facultad.Id), null, CancellationToken.None);
        await CasoDeUso.AdministradosAsync(Como(Rol.Rector), "", CancellationToken.None);

        await _personas.Received(1).AdministradasAsync(
            Arg.Is<IReadOnlyCollection<short>>(r => r.Single() == Rol.CoordinadorPrograma.Id), _facultad.Id, null, null,
            Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _personas.Received(1).AdministradasAsync(
            Arg.Is<IReadOnlyCollection<short>>(r => r.Single() == Rol.CoordinadorAcademico.Id), null, null, null,
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public async Task Docentes_y_estudiantes_no_tienen_a_quien_administrar_y_ni_siquiera_se_consulta(short rolId)
    {
        var lista = await CasoDeUso.AdministradosAsync(Como(Rol.Por(rolId)), null, CancellationToken.None);

        Assert.Empty(lista);
        await _personas.DidNotReceive().AdministradasAsync(
            Arg.Any<IReadOnlyCollection<short>>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ----------------------------------------------------- desactivar y reactivar

    [Fact]
    public async Task Desactivar_deja_la_cuenta_inactiva_con_su_rol_y_audita_el_motivo()
    {
        var (persona, asignacion) = UnaPersona(Rol.Estudiante, _sistemas);

        await CasoDeUso.DesactivarAsync(CoordinadorDeSistemas(), persona.Id, "  Ya no estudia.  ", CancellationToken.None);

        Assert.Equal(EstadoCuenta.Inactiva, persona.Estado);
        Assert.True(asignacion.Activa);
        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(
            e => e.Accion == AccionesAuditoria.UsuarioDesactivar && e.Datos.Contains("Ya no estudia.", StringComparison.Ordinal)));
        await _unidad.Received(1).ConfirmarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reactivar_devuelve_la_cuenta_a_activa_y_se_audita()
    {
        var (persona, _) = UnaPersona(Rol.Estudiante, _sistemas);
        persona.Desactivar();

        await CasoDeUso.ReactivarAsync(CoordinadorDeSistemas(), persona.Id, CancellationToken.None);

        Assert.Equal(EstadoCuenta.Activa, persona.Estado);
        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.UsuarioReactivar));
    }

    [Fact]
    public async Task Solo_se_desactiva_lo_activo_y_solo_se_reactiva_lo_inactivo()
    {
        var (activa, _) = UnaPersona(Rol.Estudiante, _sistemas);
        var (pendiente, _) = UnaPersona(Rol.Estudiante, _sistemas, activa: false);

        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.ReactivarAsync(CoordinadorDeSistemas(), activa.Id, CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.DesactivarAsync(CoordinadorDeSistemas(), pendiente.Id, "x", CancellationToken.None));
    }

    [Fact]
    public async Task El_motivo_es_obligatorio_y_la_persona_y_su_rol_tienen_que_existir()
    {
        var (persona, _) = UnaPersona(Rol.Estudiante, _sistemas);

        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.DesactivarAsync(CoordinadorDeSistemas(), persona.Id, "   ", CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.DesactivarAsync(CoordinadorDeSistemas(), Guid.NewGuid(), "x", CancellationToken.None));

        // Una persona sin rol activo todavía está en «Cuentas».
        var sinRol = new Persona("sinrol@uco.edu.co", "+573001234567", "Sin Rol", Ahora);
        _personas.PorIdAsync(sinRol.Id, Arg.Any<CancellationToken>()).Returns(sinRol);
        var error = await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.DesactivarAsync(CoordinadorDeSistemas(), sinRol.Id, "x", CancellationToken.None));
        Assert.Contains("Cuentas", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_cadena_del_enunciado_decide_quien_administra_y_el_intento_ajeno_se_audita()
    {
        var (estudiante, _) = UnaPersona(Rol.Estudiante, _sistemas);

        // El de otro programa, el de un escalón distinto y un estudiante no administran.
        var deOtroPrograma = Como(Rol.CoordinadorPrograma, _facultad.Id, _civil.Id);
        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() =>
            CasoDeUso.DesactivarAsync(deOtroPrograma, estudiante.Id, "x", CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() =>
            CasoDeUso.DesactivarAsync(Como(Rol.CoordinadorFacultad, _facultad.Id), estudiante.Id, "x", CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() =>
            CasoDeUso.ReactivarAsync(Como(Rol.Estudiante), estudiante.Id, CancellationToken.None));

        Assert.Equal(EstadoCuenta.Activa, estudiante.Estado);
        _eventos.Received(3).Agregar(Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.AccesoRechazado));
    }

    [Fact]
    public async Task Nadie_se_administra_a_si_mismo()
    {
        var (admin, _) = UnaPersona(Rol.Administrador);

        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() =>
            CasoDeUso.DesactivarAsync(Como(Rol.Administrador, personaId: admin.Id), admin.Id, "x", CancellationToken.None));
    }

    // ------------------------------------------------------------ cambiar rol

    [Fact]
    public async Task El_coordinador_cambia_a_un_docente_a_estudiante_revoca_el_rol_anterior_y_lo_audita()
    {
        var (docente, anterior) = UnaPersona(Rol.Docente, _sistemas);

        await CasoDeUso.CambiarRolAsync(
            CoordinadorDeSistemas(), docente.Id, new CambiarRolDto(Rol.Estudiante.Id, null, _sistemas.Id, "Cambio de vinculación."), CancellationToken.None);

        Assert.False(anterior.Activa);
        _personas.Received(1).AgregarRol(Arg.Is<AsignacionRol>(a => a.Activa && a.RolId == Rol.Estudiante.Id && a.ProgramaId == _sistemas.Id));
        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.UsuarioCambiarRol));
        await _unidad.Received(1).ConfirmarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task La_facultad_del_rol_nuevo_sale_del_programa_y_no_de_lo_que_diga_quien_llama()
    {
        var (docente, _) = UnaPersona(Rol.Docente, _sistemas);

        // Se manda una facultad cualquiera: se ignora y se usa la del programa.
        await CasoDeUso.CambiarRolAsync(
            CoordinadorDeSistemas(), docente.Id, new CambiarRolDto(Rol.Estudiante.Id, Guid.NewGuid(), _sistemas.Id, "x"), CancellationToken.None);

        _personas.Received(1).AgregarRol(Arg.Is<AsignacionRol>(a => a.FacultadId == _facultad.Id));
    }

    [Fact]
    public async Task No_se_asigna_un_rol_que_uno_no_aprueba_ni_fuera_de_su_ambito()
    {
        var (docente, anterior) = UnaPersona(Rol.Docente, _sistemas);

        // El Coordinador de Programa no aprueba a otro Coordinador de Programa.
        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() => CasoDeUso.CambiarRolAsync(
            CoordinadorDeSistemas(), docente.Id, new CambiarRolDto(Rol.CoordinadorPrograma.Id, null, _sistemas.Id, "x"), CancellationToken.None));

        // Ni pasa a alguien suyo a otro programa.
        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() => CasoDeUso.CambiarRolAsync(
            CoordinadorDeSistemas(), docente.Id, new CambiarRolDto(Rol.Docente.Id, null, _civil.Id, "x"), CancellationToken.None));

        Assert.True(anterior.Activa);
        _personas.DidNotReceive().AgregarRol(Arg.Any<AsignacionRol>());
    }

    [Fact]
    public async Task El_rol_nuevo_no_puede_ser_el_mismo_ni_estar_ocupado_ni_en_un_ambito_desactivado()
    {
        var (docente, _) = UnaPersona(Rol.Docente, _sistemas);

        await Assert.ThrowsAsync<ExcepcionDominio>(() => CasoDeUso.CambiarRolAsync(
            CoordinadorDeSistemas(), docente.Id, new CambiarRolDto(Rol.Docente.Id, null, _sistemas.Id, "x"), CancellationToken.None));

        _personas.ExisteRolActivoAsync(Rol.Estudiante.Id, _facultad.Id, _sistemas.Id, Arg.Any<CancellationToken>()).Returns(true);
        var ocupado = await Assert.ThrowsAsync<ExcepcionDominio>(() => CasoDeUso.CambiarRolAsync(
            CoordinadorDeSistemas(), docente.Id, new CambiarRolDto(Rol.Estudiante.Id, null, _sistemas.Id, "x"), CancellationToken.None));
        Assert.Contains("Ya hay alguien", ocupado.Message, StringComparison.Ordinal);

        _sistemas.Desactivar();
        var desactivado = await Assert.ThrowsAsync<ExcepcionDominio>(() => CasoDeUso.CambiarRolAsync(
            CoordinadorDeSistemas(), docente.Id, new CambiarRolDto(Rol.Estudiante.Id, null, _sistemas.Id, "x"), CancellationToken.None));
        Assert.Contains("desactivado", desactivado.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_rol_de_facultad_exige_una_facultad_que_exista_y_este_activa()
    {
        var (coordinador, _) = UnaPersona(Rol.CoordinadorPrograma, _sistemas);
        var decana = Como(Rol.CoordinadorFacultad, _facultad.Id);

        // Un rol de facultad lo aprueba el Coordinador Académico, no la Decana: se rechaza antes de nada
        // por ámbito, y aquí se comprueba la validación de la facultad con quien sí lo aprueba.
        var academico = Como(Rol.CoordinadorAcademico);
        var (otro, _) = UnaPersona(Rol.CoordinadorFacultad);

        await Assert.ThrowsAsync<ExcepcionDominio>(() => CasoDeUso.CambiarRolAsync(
            academico, otro.Id, new CambiarRolDto(Rol.CoordinadorFacultad.Id, Guid.NewGuid(), null, "x"), CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionDominio>(() => CasoDeUso.CambiarRolAsync(
            academico, otro.Id, new CambiarRolDto(Rol.CoordinadorFacultad.Id, null, null, "x"), CancellationToken.None));

        _facultad.Desactivar();
        await Assert.ThrowsAsync<ExcepcionDominio>(() => CasoDeUso.CambiarRolAsync(
            academico, otro.Id, new CambiarRolDto(Rol.CoordinadorFacultad.Id, _facultad.Id, null, "x"), CancellationToken.None));

        // El programa que no existe también se rechaza.
        await Assert.ThrowsAsync<ExcepcionDominio>(() => CasoDeUso.CambiarRolAsync(
            decana, coordinador.Id, new CambiarRolDto(Rol.CoordinadorPrograma.Id, null, Guid.NewGuid(), "x"), CancellationToken.None));
    }

    [Fact]
    public async Task Un_rol_de_universidad_no_lleva_ambito_y_lo_asigna_su_aprobador()
    {
        var (academico, _) = UnaPersona(Rol.CoordinadorAcademico);

        // El Administrador aprueba al Rector: de Coordinador Académico a Rector lo asigna... el Rector no
        // aprueba rectores, así que esto solo lo puede hacer el Administrador sobre un Rector.
        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() => CasoDeUso.CambiarRolAsync(
            Como(Rol.Rector), academico.Id, new CambiarRolDto(Rol.Rector.Id, null, null, "x"), CancellationToken.None));
    }
}
