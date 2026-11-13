using NSubstitute;
using UcoKudos.Application.Comun;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Application.Interaccion.CasosUso;
using UcoKudos.Application.Interaccion.Entrada.Dto;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Auditoria.Repositorios;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Interaccion;
using UcoKudos.Domain.Interaccion.Repositorios;
using UcoKudos.Domain.Kudos;
using UcoKudos.Domain.Kudos.Repositorios;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.UnitTests.Application.Interaccion;

/// <summary>
/// Calificar (RF-10) a nivel de caso de uso: el promedio y el total que quedan en el contenido, el
/// reemplazo de la calificación anterior y lo que se rechaza.
/// </summary>
public sealed class CalificarCasoUsoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private readonly IKudoRepositorio _kudos = Substitute.For<IKudoRepositorio>();
    private readonly IReplicaRepositorio _replicas = Substitute.For<IReplicaRepositorio>();
    private readonly ICalificacionRepositorio _calificaciones = Substitute.For<ICalificacionRepositorio>();
    private readonly IUnidadTrabajo _unidad = Substitute.For<IUnidadTrabajo>();
    private readonly ICacheConsultas _cache = Substitute.For<ICacheConsultas>();
    private readonly IEventoAuditoriaRepositorio _eventos = Substitute.For<IEventoAuditoriaRepositorio>();

    private CalificarCasoUso CasoDeUso =>
        new(_kudos, _replicas, _calificaciones, _unidad, _cache, new ServicioAuditoria(_eventos), new RelojFijo());

    private static readonly Guid Persona = Guid.NewGuid();

    private static ContextoPeticion Contexto() => new(new AmbitoUsuario(Persona, Rol.Estudiante, null, null), "traza");

    private Kudo UnKudo()
    {
        var kudo = new Kudo(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Gracias.", Ahora);
        _kudos.PorIdAsync(kudo.Id, Arg.Any<CancellationToken>()).Returns(kudo);
        return kudo;
    }

    [Fact]
    public async Task La_primera_calificacion_deja_total_uno_y_su_valor_como_promedio()
    {
        var kudo = UnKudo();
        _calificaciones.ResumenAsync(TipoContenido.Kudo, kudo.Id, Arg.Any<CancellationToken>())
            .Returns(new ResumenCalificacion(0, null));

        await CasoDeUso.EjecutarAsync(Contexto(), new CalificarDto("Kudo", kudo.Id, 4), CancellationToken.None);

        Assert.Equal(1, kudo.TotalCalificaciones);
        Assert.Equal(4m, kudo.PromedioCalificacion);
        _calificaciones.Received(1).Agregar(Arg.Is<Calificacion>(c => c.Estrellas == 4 && c.Activa));
        await _unidad.Received(1).ConfirmarAsync(Arg.Any<CancellationToken>());
        await _cache.Received(1).InvalidarGrupoAsync(GruposCache.Muro, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Una_calificacion_nueva_se_suma_al_promedio_de_las_que_ya_habia()
    {
        var kudo = UnKudo();
        // Ya hay dos calificaciones, de 5 y 3: promedio 4.
        _calificaciones.ResumenAsync(TipoContenido.Kudo, kudo.Id, Arg.Any<CancellationToken>())
            .Returns(new ResumenCalificacion(2, 4m));

        await CasoDeUso.EjecutarAsync(Contexto(), new CalificarDto("Kudo", kudo.Id, 1), CancellationToken.None);

        // (5 + 3 + 1) / 3 = 3.
        Assert.Equal(3, kudo.TotalCalificaciones);
        Assert.Equal(3m, kudo.PromedioCalificacion);
    }

    /// <summary>Cambiar de opinión reemplaza: no cuenta a la misma persona dos veces.</summary>
    [Fact]
    public async Task Cambiar_de_opinion_desactiva_la_anterior_y_no_sube_el_total()
    {
        var kudo = UnKudo();
        var anterior = new Calificacion(Persona, TipoContenido.Kudo, kudo.Id, 5, Ahora.AddDays(-1));
        _calificaciones.ActivaDeAsync(Persona, TipoContenido.Kudo, kudo.Id, Arg.Any<CancellationToken>()).Returns(anterior);

        // En la base siguen las dos activas (la nueva todavía no se guardó): 5 y 3, promedio 4.
        _calificaciones.ResumenAsync(TipoContenido.Kudo, kudo.Id, Arg.Any<CancellationToken>())
            .Returns(new ResumenCalificacion(2, 4m));

        await CasoDeUso.EjecutarAsync(Contexto(), new CalificarDto("Kudo", kudo.Id, 1), CancellationToken.None);

        Assert.False(anterior.Activa);

        // Quedan 2 personas: la de 3 y esta, que ahora puso 1. (3 + 1) / 2 = 2.
        Assert.Equal(2, kudo.TotalCalificaciones);
        Assert.Equal(2m, kudo.PromedioCalificacion);
        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(
            e => e.Accion == AccionesAuditoria.ContenidoCalificar && e.Datos.Contains("\"reemplazo\":true", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-3)]
    public async Task Una_calificacion_fuera_de_uno_a_cinco_se_rechaza_y_no_se_guarda_nada(short estrellas)
    {
        var kudo = UnKudo();

        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.EjecutarAsync(Contexto(), new CalificarDto("Kudo", kudo.Id, estrellas), CancellationToken.None));

        await _unidad.DidNotReceive().ConfirmarAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Kudo")]
    [InlineData("Replica")]
    public async Task No_se_califica_lo_que_no_existe(string tipo)
    {
        var error = await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.EjecutarAsync(Contexto(), new CalificarDto(tipo, Guid.NewGuid(), 3), CancellationToken.None));

        Assert.Contains("no existe", error.Message, StringComparison.Ordinal);
        _calificaciones.DidNotReceive().Agregar(Arg.Any<Calificacion>());
    }

    [Fact]
    public async Task No_se_califica_un_kudo_vetado()
    {
        var kudo = UnKudo();
        kudo.Vetar();

        var error = await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.EjecutarAsync(Contexto(), new CalificarDto("Kudo", kudo.Id, 3), CancellationToken.None));

        Assert.Contains("moderación", error.Message, StringComparison.Ordinal);
        _calificaciones.DidNotReceive().Agregar(Arg.Any<Calificacion>());
    }

    [Fact]
    public async Task Se_califica_una_replica_y_su_contador_se_actualiza()
    {
        var replica = Replica.SobreKudo(Guid.NewGuid(), Guid.NewGuid(), "Respuesta.", Ahora);
        _replicas.PorIdAsync(replica.Id, Arg.Any<CancellationToken>()).Returns(replica);
        _calificaciones.ResumenAsync(TipoContenido.Replica, replica.Id, Arg.Any<CancellationToken>())
            .Returns(new ResumenCalificacion(0, null));

        await CasoDeUso.EjecutarAsync(Contexto(), new CalificarDto("Replica", replica.Id, 5), CancellationToken.None);

        Assert.Equal(1, replica.TotalCalificaciones);
        Assert.Equal(5m, replica.PromedioCalificacion);
    }

    [Fact]
    public async Task No_se_califica_una_replica_vetada()
    {
        var replica = Replica.SobreKudo(Guid.NewGuid(), Guid.NewGuid(), "Respuesta.", Ahora);
        replica.Vetar();
        _replicas.PorIdAsync(replica.Id, Arg.Any<CancellationToken>()).Returns(replica);

        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.EjecutarAsync(Contexto(), new CalificarDto("Replica", replica.Id, 5), CancellationToken.None));
    }
}
