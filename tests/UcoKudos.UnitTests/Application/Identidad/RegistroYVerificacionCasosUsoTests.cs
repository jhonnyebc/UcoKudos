using NSubstitute;
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
/// Registro de cuentas, verificación de códigos y reenvío (RF-01, RF-02), sin base de datos.
///
/// Lo que se comprueba aquí son las **decisiones del caso de uso**: qué se emite, qué se guarda aunque
/// la verificación falle, y que las rutas públicas no distingan una cuenta que no existe de una sin
/// código. El recorrido por HTTP contra PostgreSQL está en las pruebas de punta a punta.
/// </summary>
public sealed class RegistroYVerificacionCasosUsoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public DateTimeOffset Instante { get; set; } = ahora;

        public override DateTimeOffset GetUtcNow() => Instante;
    }

    private readonly IPersonaRepositorio _personas = Substitute.For<IPersonaRepositorio>();
    private readonly IEstructuraAcademicaRepositorio _academico = Substitute.For<IEstructuraAcademicaRepositorio>();
    private readonly IUnidadTrabajo _unidad = Substitute.For<IUnidadTrabajo>();
    private readonly IBandejaSalida _bandeja = Substitute.For<IBandejaSalida>();
    private readonly IEventoAuditoriaRepositorio _eventos = Substitute.For<IEventoAuditoriaRepositorio>();
    private readonly RelojFijo _reloj = new(Ahora);

    private ServicioAuditoria Auditoria => new(_eventos);

    private static Persona NuevaPersona(string correo = "laura@uco.edu.co") =>
        new(correo, "+573001234567", "Laura Pérez", Ahora);

    // ---------------------------------------------------------------- registro

    private RegistrarCuentaCasoUso Registro => new(_personas, _academico, _unidad, _bandeja, Auditoria, _reloj);

    private static RegistrarCuentaDto Solicitud(Guid? facultad, Guid? programa) =>
        new("laura@uco.edu.co", "+573001234567", "Laura Pérez", Rol.Estudiante.Id, facultad, programa);

    [Fact]
    public async Task El_registro_deja_la_cuenta_pendiente_y_emite_un_codigo_de_correo_y_otro_de_celular()
    {
        var facultad = Guid.NewGuid();
        var programa = new Programa(facultad, "Ingeniería de Sistemas", "ISIS", Ahora);
        _academico.ProgramaPorIdAsync(programa.Id, Arg.Any<CancellationToken>()).Returns(programa);

        var resultado = await Registro.EjecutarAsync(Solicitud(facultad, programa.Id), "traza", CancellationToken.None);

        Assert.Equal("Pendiente", resultado.EstadoCuenta);
        Assert.False(resultado.CorreoVerificado);
        Assert.False(resultado.CelularVerificado);

        _personas.Received(1).Agregar(Arg.Any<Persona>());
        _personas.Received(1).AgregarRol(Arg.Is<AsignacionRol>(a => !a.Activa));

        // Un código por cada dato de contacto, con su huella y sin el código.
        _personas.Received(1).AgregarVerificacion(Arg.Is<VerificacionContacto>(v => v.Tipo == TipoContacto.Correo));
        _personas.Received(1).AgregarVerificacion(Arg.Is<VerificacionContacto>(v => v.Tipo == TipoContacto.Celular));

        _bandeja.Received(1).Programar(Arg.Is<EventoPendiente>(e => e.TipoEvento == TiposEvento.VerificacionCorreoSolicitada));
        _bandeja.Received(1).Programar(Arg.Is<EventoPendiente>(e => e.TipoEvento == TiposEvento.VerificacionCelularSolicitada));

        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.SolicitudPresentar));
        await _unidad.Received(1).ConfirmarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_se_registra_un_correo_que_ya_existe()
    {
        _personas.ExisteCorreoAsync("laura@uco.edu.co", Arg.Any<CancellationToken>()).Returns(true);

        var error = await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Registro.EjecutarAsync(Solicitud(Guid.NewGuid(), Guid.NewGuid()), "traza", CancellationToken.None));

        Assert.Contains("Ya existe una cuenta", error.Message, StringComparison.Ordinal);
        await _unidad.DidNotReceive().ConfirmarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_se_registra_con_un_programa_que_no_existe()
    {
        var error = await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Registro.EjecutarAsync(Solicitud(Guid.NewGuid(), Guid.NewGuid()), "traza", CancellationToken.None));

        Assert.Contains("programa", error.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ verificación

    private VerificarContactoCasoUso Verificacion => new(_personas, _unidad, Auditoria, _reloj);

    private const string MensajeGenerico = "El código no es correcto o ya no está vigente. Si hace falta, pide uno nuevo.";

    /// <summary>Una persona con un código vigente de ese tipo, que es «123456».</summary>
    private (Persona Persona, VerificacionContacto Codigo) ConCodigo(TipoContacto tipo)
    {
        var persona = NuevaPersona();
        var codigo = new VerificacionContacto(persona.Id, tipo, "123456", Ahora);

        _personas.PorCorreoAsync("laura@uco.edu.co", Arg.Any<CancellationToken>()).Returns(persona);
        _personas.VerificacionVigenteAsync(persona.Id, tipo, Arg.Any<CancellationToken>()).Returns(codigo);

        return (persona, codigo);
    }

    [Fact]
    public async Task Un_codigo_correcto_de_correo_verifica_el_correo_y_queda_auditado()
    {
        var (persona, _) = ConCodigo(TipoContacto.Correo);

        var estado = await Verificacion.EjecutarAsync(
            new VerificarContactoDto("laura@uco.edu.co", "correo", "123456"), "traza", CancellationToken.None);

        Assert.True(persona.CorreoVerificado);
        Assert.True(estado.CorreoVerificado);
        Assert.False(estado.CelularVerificado);
        Assert.Equal("Pendiente", estado.EstadoCuenta);

        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.ContactoVerificar));
        await _unidad.Received(1).ConfirmarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_codigo_correcto_de_celular_verifica_el_celular()
    {
        var (persona, _) = ConCodigo(TipoContacto.Celular);

        var estado = await Verificacion.EjecutarAsync(
            new VerificarContactoDto("laura@uco.edu.co", "CELULAR", "123456"), "traza", CancellationToken.None);

        Assert.True(persona.CelularVerificado);
        Assert.True(estado.CelularVerificado);
    }

    /// <summary>
    /// El intento fallido **se confirma**: si se revirtiera con el error, el contador de intentos no
    /// avanzaría y el límite de cinco no limitaría nada.
    /// </summary>
    [Fact]
    public async Task Un_codigo_incorrecto_se_rechaza_pero_el_intento_se_guarda()
    {
        var (persona, codigo) = ConCodigo(TipoContacto.Correo);

        var error = await Assert.ThrowsAsync<ExcepcionDominio>(() => Verificacion.EjecutarAsync(
            new VerificarContactoDto("laura@uco.edu.co", "correo", "000000"), "traza", CancellationToken.None));

        Assert.Equal(MensajeGenerico, error.Message);
        Assert.False(persona.CorreoVerificado);
        Assert.Equal(1, codigo.Intentos);
        await _unidad.Received(1).ConfirmarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_codigo_vencido_se_rechaza_con_el_mismo_mensaje()
    {
        ConCodigo(TipoContacto.Correo);
        _reloj.Instante = Ahora + VerificacionContacto.Vigencia + TimeSpan.FromSeconds(1);

        var error = await Assert.ThrowsAsync<ExcepcionDominio>(() => Verificacion.EjecutarAsync(
            new VerificarContactoDto("laura@uco.edu.co", "correo", "123456"), "traza", CancellationToken.None));

        Assert.Equal(MensajeGenerico, error.Message);
    }

    [Fact]
    public async Task Agotados_los_intentos_ni_el_codigo_correcto_sirve()
    {
        var (persona, _) = ConCodigo(TipoContacto.Correo);

        for (var i = 0; i < VerificacionContacto.IntentosMaximos; i++)
        {
            await Assert.ThrowsAsync<ExcepcionDominio>(() => Verificacion.EjecutarAsync(
                new VerificarContactoDto("laura@uco.edu.co", "correo", "000000"), "traza", CancellationToken.None));
        }

        await Assert.ThrowsAsync<ExcepcionDominio>(() => Verificacion.EjecutarAsync(
            new VerificarContactoDto("laura@uco.edu.co", "correo", "123456"), "traza", CancellationToken.None));

        Assert.False(persona.CorreoVerificado);
    }

    /// <summary>
    /// La ruta es pública, así que «la cuenta no existe», «no hay código vigente» y «ya estaba
    /// verificado» dan el mismo mensaje: de lo contrario serviría de buscador de correos registrados.
    /// </summary>
    [Fact]
    public async Task Una_cuenta_inexistente_sin_codigo_o_ya_verificada_dan_el_mismo_mensaje()
    {
        // Inexistente.
        var inexistente = await Assert.ThrowsAsync<ExcepcionDominio>(() => Verificacion.EjecutarAsync(
            new VerificarContactoDto("nadie@uco.edu.co", "correo", "123456"), "traza", CancellationToken.None));

        // Existe pero sin código vigente.
        var sinCodigo = NuevaPersona("otra@uco.edu.co");
        _personas.PorCorreoAsync("otra@uco.edu.co", Arg.Any<CancellationToken>()).Returns(sinCodigo);
        var sinVigente = await Assert.ThrowsAsync<ExcepcionDominio>(() => Verificacion.EjecutarAsync(
            new VerificarContactoDto("otra@uco.edu.co", "correo", "123456"), "traza", CancellationToken.None));

        // Ya verificada.
        var verificada = NuevaPersona("ya@uco.edu.co");
        verificada.VerificarCorreo();
        _personas.PorCorreoAsync("ya@uco.edu.co", Arg.Any<CancellationToken>()).Returns(verificada);
        var yaVerificada = await Assert.ThrowsAsync<ExcepcionDominio>(() => Verificacion.EjecutarAsync(
            new VerificarContactoDto("ya@uco.edu.co", "correo", "123456"), "traza", CancellationToken.None));

        Assert.Equal(MensajeGenerico, inexistente.Message);
        Assert.Equal(MensajeGenerico, sinVigente.Message);
        Assert.Equal(MensajeGenerico, yaVerificada.Message);
    }

    [Fact]
    public async Task Un_tipo_de_verificacion_desconocido_se_rechaza()
    {
        var error = await Assert.ThrowsAsync<ExcepcionDominio>(() => Verificacion.EjecutarAsync(
            new VerificarContactoDto("laura@uco.edu.co", "huella", "123456"), "traza", CancellationToken.None));

        Assert.Contains("correo", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verificar_el_ultimo_dato_pendiente_con_la_solicitud_aprobada_activa_la_cuenta()
    {
        var (persona, _) = ConCodigo(TipoContacto.Celular);
        persona.AprobarSolicitud();
        persona.VerificarCorreo();

        var estado = await Verificacion.EjecutarAsync(
            new VerificarContactoDto("laura@uco.edu.co", "celular", "123456"), "traza", CancellationToken.None);

        Assert.Equal("Activa", estado.EstadoCuenta);
        Assert.True(estado.SolicitudAprobada);
    }

    // ----------------------------------------------------------------- reenvío

    private ReenviarCodigosCasoUso Reenvio => new(_personas, _unidad, _bandeja, _reloj);

    [Fact]
    public async Task Reenviar_a_una_cuenta_inexistente_no_hace_nada_ni_lo_dice()
    {
        await Reenvio.EjecutarAsync("nadie@uco.edu.co", "traza", CancellationToken.None);

        _bandeja.DidNotReceive().Programar(Arg.Any<EventoPendiente>());
        await _unidad.DidNotReceive().ConfirmarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reenviar_a_una_cuenta_que_ya_no_esta_pendiente_no_hace_nada()
    {
        var activa = NuevaPersona();
        activa.AprobarSolicitud();
        activa.VerificarCorreo();
        activa.VerificarCelular();
        _personas.PorCorreoAsync("laura@uco.edu.co", Arg.Any<CancellationToken>()).Returns(activa);

        await Reenvio.EjecutarAsync("laura@uco.edu.co", "traza", CancellationToken.None);

        _bandeja.DidNotReceive().Programar(Arg.Any<EventoPendiente>());
    }

    [Fact]
    public async Task Reenviar_emite_codigos_nuevos_e_invalida_los_anteriores()
    {
        var persona = NuevaPersona();
        var viejo = new VerificacionContacto(persona.Id, TipoContacto.Correo, "111111", Ahora.AddMinutes(-5));
        _personas.PorCorreoAsync("laura@uco.edu.co", Arg.Any<CancellationToken>()).Returns(persona);
        _personas.VerificacionVigenteAsync(persona.Id, TipoContacto.Correo, Arg.Any<CancellationToken>()).Returns(viejo);

        await Reenvio.EjecutarAsync("laura@uco.edu.co", "traza", CancellationToken.None);

        // El viejo ya no sirve: solo vale el último.
        Assert.Equal(ResultadoCodigo.SinCodigo, viejo.Verificar("111111", Ahora));

        _personas.Received(2).AgregarVerificacion(Arg.Any<VerificacionContacto>());
        _bandeja.Received(2).Programar(Arg.Any<EventoPendiente>());
        await _unidad.Received(1).ConfirmarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_se_manda_mas_de_un_codigo_por_minuto()
    {
        var persona = NuevaPersona();
        var reciente = new VerificacionContacto(persona.Id, TipoContacto.Correo, "111111", Ahora.AddSeconds(-20));
        _personas.PorCorreoAsync("laura@uco.edu.co", Arg.Any<CancellationToken>()).Returns(persona);
        _personas.VerificacionVigenteAsync(persona.Id, TipoContacto.Correo, Arg.Any<CancellationToken>()).Returns(reciente);

        await Reenvio.EjecutarAsync("laura@uco.edu.co", "traza", CancellationToken.None);

        // El del correo es de hace 20 segundos y se salta; el del celular no tenía ninguno y sale.
        _personas.Received(1).AgregarVerificacion(Arg.Is<VerificacionContacto>(v => v.Tipo == TipoContacto.Celular));
        _personas.DidNotReceive().AgregarVerificacion(Arg.Is<VerificacionContacto>(v => v.Tipo == TipoContacto.Correo));
    }

    [Fact]
    public async Task No_se_reenvia_lo_que_ya_esta_verificado()
    {
        var persona = NuevaPersona();
        persona.VerificarCorreo();
        _personas.PorCorreoAsync("laura@uco.edu.co", Arg.Any<CancellationToken>()).Returns(persona);

        await Reenvio.EjecutarAsync("laura@uco.edu.co", "traza", CancellationToken.None);

        _personas.Received(1).AgregarVerificacion(Arg.Is<VerificacionContacto>(v => v.Tipo == TipoContacto.Celular));
        _personas.DidNotReceive().AgregarVerificacion(Arg.Is<VerificacionContacto>(v => v.Tipo == TipoContacto.Correo));
    }
}
