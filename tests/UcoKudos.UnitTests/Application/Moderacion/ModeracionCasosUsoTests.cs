using NSubstitute;
using UcoKudos.Application.Comun;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Application.Interaccion.Entrada.Dto;
using UcoKudos.Application.Moderacion.CasosUso;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Auditoria.Repositorios;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Identidad.Repositorios;
using UcoKudos.Domain.Interaccion;
using UcoKudos.Domain.Interaccion.Repositorios;
using UcoKudos.Domain.Kudos;
using UcoKudos.Domain.Kudos.Repositorios;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.UnitTests.Application.Moderacion;

/// <summary>
/// Reportar, vetar y la bandeja de quien modera (RF-11, RF-12), sin base de datos. Las decisiones
/// del caso de uso: quién puede, qué se resuelve al vetar y que el recuento sea el correcto.
/// </summary>
public sealed class ModeracionCasosUsoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private readonly IKudoRepositorio _kudos = Substitute.For<IKudoRepositorio>();
    private readonly IReplicaRepositorio _replicas = Substitute.For<IReplicaRepositorio>();
    private readonly IModeracionRepositorio _moderacion = Substitute.For<IModeracionRepositorio>();
    private readonly IPersonaRepositorio _personas = Substitute.For<IPersonaRepositorio>();
    private readonly IUnidadTrabajo _unidad = Substitute.For<IUnidadTrabajo>();
    private readonly IBandejaSalida _bandeja = Substitute.For<IBandejaSalida>();
    private readonly ICacheConsultas _cache = Substitute.For<ICacheConsultas>();
    private readonly IEventoAuditoriaRepositorio _eventos = Substitute.For<IEventoAuditoriaRepositorio>();

    private ServicioAuditoria Auditoria => new(_eventos);

    private static ContextoPeticion Como(Rol rol) =>
        new(new AmbitoUsuario(Guid.NewGuid(), rol, null, null), "traza");

    private static Kudo UnKudo(string texto = "Gracias por todo.") =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), texto, Ahora);

    private VetarContenidoCasoUso Vetar =>
        new(_kudos, _replicas, _moderacion, _unidad, _bandeja, _cache, Auditoria, new RelojFijo());

    private GestionarReportesCasoUso Gestionar =>
        new(_kudos, _replicas, _moderacion, _personas, _unidad, Auditoria, new RelojFijo());

    private ReportarContenidoCasoUso Reportar =>
        new(_kudos, _replicas, _moderacion, _unidad, Auditoria, new RelojFijo());

    // ------------------------------------------------------------------ reportar

    [Fact]
    public async Task Cualquier_rol_puede_reportar_un_kudo_que_existe_y_queda_auditado()
    {
        var kudo = UnKudo();
        _kudos.PorIdAsync(kudo.Id, Arg.Any<CancellationToken>()).Returns(kudo);

        await Reportar.EjecutarAsync(Como(Rol.Estudiante), new ReportarDto("Kudo", kudo.Id, "Ofensivo."), CancellationToken.None);

        _moderacion.Received(1).AgregarReporte(Arg.Is<Reporte>(r => r.ContenidoId == kudo.Id && r.Motivo == "Ofensivo."));
        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.ContenidoReportar));
        await _unidad.Received(1).ConfirmarAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Kudo")]
    [InlineData("Replica")]
    public async Task No_se_reporta_un_contenido_que_no_existe(string tipo)
    {
        var error = await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Reportar.EjecutarAsync(Como(Rol.Estudiante), new ReportarDto(tipo, Guid.NewGuid(), "x"), CancellationToken.None));

        Assert.Contains("no existe", error.Message, StringComparison.Ordinal);
        _moderacion.DidNotReceive().AgregarReporte(Arg.Any<Reporte>());
    }

    [Fact]
    public async Task Se_puede_reportar_una_replica()
    {
        var replica = Replica.SobreKudo(Guid.NewGuid(), Guid.NewGuid(), "Respuesta.", Ahora);
        _replicas.PorIdAsync(replica.Id, Arg.Any<CancellationToken>()).Returns(replica);

        await Reportar.EjecutarAsync(Como(Rol.Docente), new ReportarDto("Replica", replica.Id, "Fuera de lugar."), CancellationToken.None);

        _moderacion.Received(1).AgregarReporte(Arg.Is<Reporte>(r => r.TipoContenido == TipoContenido.Replica));
    }

    // --------------------------------------------------------------------- vetar

    /// <summary>
    /// Regresión: el recuento se tomaba después de marcar las réplicas como vetadas y daba siempre
    /// cero, de modo que vetar un Kudo con tres réplicas decía «1 contenido afectado».
    /// </summary>
    [Fact]
    public async Task Vetar_un_kudo_cuenta_el_kudo_y_todas_las_replicas_de_su_hilo()
    {
        var kudo = UnKudo();
        var r1 = Replica.SobreKudo(kudo.Id, Guid.NewGuid(), "Uno.", Ahora);
        var r2 = Replica.SobreReplica(r1, Guid.NewGuid(), "Dos.", Ahora.AddMinutes(1));
        var r3 = Replica.SobreReplica(r2, Guid.NewGuid(), "Tres.", Ahora.AddMinutes(2));
        _kudos.PorIdAsync(kudo.Id, Arg.Any<CancellationToken>()).Returns(kudo);
        _replicas.HiloDeAsync(kudo.Id, Arg.Any<CancellationToken>()).Returns([r1, r2, r3]);

        var resultado = await Vetar.EjecutarAsync(
            Como(Rol.Rector), new VetarDto("Kudo", kudo.Id, "Ofensivo."), CancellationToken.None);

        Assert.Equal(4, resultado.ContenidosAfectados);
        Assert.True(resultado.EnCascada);
        Assert.True(kudo.Vetado);
        Assert.All([r1, r2, r3], r => Assert.True(r.Vetado));
    }

    [Fact]
    public async Task Vetar_un_kudo_sin_hilo_afecta_solo_al_kudo()
    {
        var kudo = UnKudo();
        _kudos.PorIdAsync(kudo.Id, Arg.Any<CancellationToken>()).Returns(kudo);
        _replicas.HiloDeAsync(kudo.Id, Arg.Any<CancellationToken>()).Returns([]);

        var resultado = await Vetar.EjecutarAsync(
            Como(Rol.CoordinadorAcademico), new VetarDto("Kudo", kudo.Id, "x"), CancellationToken.None);

        Assert.Equal(1, resultado.ContenidosAfectados);
        Assert.False(resultado.EnCascada);
    }

    [Fact]
    public async Task Vetar_una_replica_usa_la_cascada_por_ruta_y_actualiza_el_contador_del_kudo()
    {
        var kudo = UnKudo();
        kudo.FijarTotalReplicas(5);
        var replica = Replica.SobreKudo(kudo.Id, Guid.NewGuid(), "Respuesta.", Ahora);
        _replicas.PorIdAsync(replica.Id, Arg.Any<CancellationToken>()).Returns(replica);
        _replicas.VetarEnCascadaAsync(replica.RutaMaterializada, Arg.Any<CancellationToken>()).Returns(3);
        _kudos.PorIdAsync(kudo.Id, Arg.Any<CancellationToken>()).Returns(kudo);

        var resultado = await Vetar.EjecutarAsync(
            Como(Rol.Rector), new VetarDto("Replica", replica.Id, "x"), CancellationToken.None);

        Assert.Equal(3, resultado.ContenidosAfectados);
        Assert.Equal(2, kudo.TotalReplicas);
    }

    [Fact]
    public async Task Vetar_resuelve_como_aceptados_los_reportes_pendientes_avisa_al_autor_y_limpia_la_cache()
    {
        var kudo = UnKudo();
        var reporte = new Reporte(Guid.NewGuid(), TipoContenido.Kudo, kudo.Id, "Ofensivo.", Ahora);
        _kudos.PorIdAsync(kudo.Id, Arg.Any<CancellationToken>()).Returns(kudo);
        _replicas.HiloDeAsync(kudo.Id, Arg.Any<CancellationToken>()).Returns([]);
        _moderacion.ReportesPendientesDeAsync(TipoContenido.Kudo, kudo.Id, Arg.Any<CancellationToken>()).Returns([reporte]);

        await Vetar.EjecutarAsync(Como(Rol.Rector), new VetarDto("Kudo", kudo.Id, "Ofensivo."), CancellationToken.None);

        Assert.Equal(EstadoReporte.Aceptado, reporte.Estado);
        _moderacion.Received(1).AgregarVeto(Arg.Any<Veto>());
        _bandeja.Received(1).Programar(Arg.Is<EventoPendiente>(e => e.TipoEvento == TiposEvento.ContenidoVetado));
        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.ContenidoVetar));
        await _cache.Received(1).InvalidarGrupoAsync(GruposCache.Muro, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task Solo_el_rector_y_el_coordinador_academico_pueden_vetar(short rolId)
    {
        var kudo = UnKudo();
        _kudos.PorIdAsync(kudo.Id, Arg.Any<CancellationToken>()).Returns(kudo);

        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() =>
            Vetar.EjecutarAsync(Como(Rol.Por(rolId)), new VetarDto("Kudo", kudo.Id, "x"), CancellationToken.None));

        // No se veta nada, y el intento queda en la auditoría.
        Assert.False(kudo.Vetado);
        _moderacion.DidNotReceive().AgregarVeto(Arg.Any<Veto>());
        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.AccesoRechazado));
    }

    [Fact]
    public async Task No_se_veta_un_kudo_ni_una_replica_que_no_existen()
    {
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Vetar.EjecutarAsync(Como(Rol.Rector), new VetarDto("Kudo", Guid.NewGuid(), "x"), CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Vetar.EjecutarAsync(Como(Rol.Rector), new VetarDto("Replica", Guid.NewGuid(), "x"), CancellationToken.None));
    }

    // -------------------------------------------------------------------- bandeja

    [Fact]
    public async Task La_bandeja_devuelve_el_contenido_reportado_con_su_autor_y_quien_lo_reporto()
    {
        var autor = new Persona("ana@uco.edu.co", "+573001112233", "Ana Gómez", Ahora);
        var reportante = new Persona("carlos@uco.edu.co", "+573004445566", "Carlos Mejía", Ahora);
        var kudo = new Kudo(autor.Id, Guid.NewGuid(), Guid.NewGuid(), "Un mensaje reportado.", Ahora);
        var reporte = new Reporte(reportante.Id, TipoContenido.Kudo, kudo.Id, "Ofensivo.", Ahora);

        _moderacion.ReportesPendientesAsync(Arg.Any<CancellationToken>()).Returns([reporte]);
        _kudos.PorIdAsync(kudo.Id, Arg.Any<CancellationToken>()).Returns(kudo);
        _personas.PorIdAsync(autor.Id, Arg.Any<CancellationToken>()).Returns(autor);
        _personas.PorIdAsync(reportante.Id, Arg.Any<CancellationToken>()).Returns(reportante);

        var lista = await Gestionar.PendientesAsync(Como(Rol.Rector), CancellationToken.None);

        var fila = Assert.Single(lista);
        Assert.Equal("Un mensaje reportado.", fila.Contenido);
        Assert.Equal("Ana Gómez", fila.AutorDelContenido);
        Assert.Equal("Carlos Mejía", fila.ReportadoPor);
        Assert.Equal("Kudo", fila.TipoContenido);
    }

    [Fact]
    public async Task La_bandeja_incluye_los_reportes_de_replicas()
    {
        var replica = Replica.SobreKudo(Guid.NewGuid(), Guid.NewGuid(), "Una respuesta.", Ahora);
        var reporte = new Reporte(Guid.NewGuid(), TipoContenido.Replica, replica.Id, "Fuera de lugar.", Ahora);
        _moderacion.ReportesPendientesAsync(Arg.Any<CancellationToken>()).Returns([reporte]);
        _replicas.PorIdAsync(replica.Id, Arg.Any<CancellationToken>()).Returns(replica);

        var lista = await Gestionar.PendientesAsync(Como(Rol.CoordinadorAcademico), CancellationToken.None);

        var fila = Assert.Single(lista);
        Assert.Equal("Replica", fila.TipoContenido);
        Assert.Equal("Persona desconocida", fila.ReportadoPor);
    }

    [Fact]
    public async Task La_bandeja_omite_lo_que_ya_esta_vetado_o_ya_no_existe()
    {
        var vetado = UnKudo();
        vetado.Vetar();
        var reporteVetado = new Reporte(Guid.NewGuid(), TipoContenido.Kudo, vetado.Id, "x", Ahora);
        var reporteHuerfano = new Reporte(Guid.NewGuid(), TipoContenido.Kudo, Guid.NewGuid(), "x", Ahora);

        _moderacion.ReportesPendientesAsync(Arg.Any<CancellationToken>()).Returns([reporteVetado, reporteHuerfano]);
        _kudos.PorIdAsync(vetado.Id, Arg.Any<CancellationToken>()).Returns(vetado);

        Assert.Empty(await Gestionar.PendientesAsync(Como(Rol.Rector), CancellationToken.None));
    }

    [Fact]
    public async Task Los_roles_que_no_moderan_no_ven_la_bandeja_y_el_intento_queda_auditado()
    {
        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() =>
            Gestionar.PendientesAsync(Como(Rol.Administrador), CancellationToken.None));

        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.AccesoRechazado));
        await _moderacion.DidNotReceive().ReportesPendientesAsync(Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------ descartar

    [Fact]
    public async Task Descartar_resuelve_el_reporte_sin_vetar_y_queda_auditado()
    {
        var reporte = new Reporte(Guid.NewGuid(), TipoContenido.Kudo, Guid.NewGuid(), "x", Ahora);
        _moderacion.ReportePorIdAsync(reporte.Id, Arg.Any<CancellationToken>()).Returns(reporte);

        await Gestionar.DescartarAsync(Como(Rol.Rector), reporte.Id, CancellationToken.None);

        Assert.Equal(EstadoReporte.Descartado, reporte.Estado);
        _moderacion.DidNotReceive().AgregarVeto(Arg.Any<Veto>());
        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.ReporteDescartar));
        await _unidad.Received(1).ConfirmarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_se_descarta_un_reporte_que_no_existe_ni_uno_ya_resuelto()
    {
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Gestionar.DescartarAsync(Como(Rol.Rector), Guid.NewGuid(), CancellationToken.None));

        var resuelto = new Reporte(Guid.NewGuid(), TipoContenido.Kudo, Guid.NewGuid(), "x", Ahora);
        resuelto.Resolver(seVeto: true);
        _moderacion.ReportePorIdAsync(resuelto.Id, Arg.Any<CancellationToken>()).Returns(resuelto);

        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            Gestionar.DescartarAsync(Como(Rol.Rector), resuelto.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Quien_no_modera_no_descarta_reportes()
    {
        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() =>
            Gestionar.DescartarAsync(Como(Rol.Estudiante), Guid.NewGuid(), CancellationToken.None));

        await _moderacion.DidNotReceive().ReportePorIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
