using NSubstitute;
using UcoKudos.Application.Comun;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Application.Interaccion.CasosUso;
using UcoKudos.Application.Interaccion.Entrada.Dto;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Auditoria.Repositorios;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Identidad.Repositorios;
using UcoKudos.Domain.Interaccion;
using UcoKudos.Domain.Interaccion.Repositorios;
using UcoKudos.Domain.Kudos;
using UcoKudos.Domain.Kudos.Repositorios;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.UnitTests.Application.Interaccion;

/// <summary>
/// Responder (RF-09) a nivel de caso de uso: el árbol que se construye, el contador del Kudo, a quién
/// se avisa y qué se rechaza.
/// </summary>
public sealed class ResponderCasoUsoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private readonly IKudoRepositorio _kudos = Substitute.For<IKudoRepositorio>();
    private readonly IReplicaRepositorio _replicas = Substitute.For<IReplicaRepositorio>();
    private readonly IPersonaRepositorio _personas = Substitute.For<IPersonaRepositorio>();
    private readonly IUnidadTrabajo _unidad = Substitute.For<IUnidadTrabajo>();
    private readonly IBandejaSalida _bandeja = Substitute.For<IBandejaSalida>();
    private readonly ICacheConsultas _cache = Substitute.For<ICacheConsultas>();
    private readonly IEventoAuditoriaRepositorio _eventos = Substitute.For<IEventoAuditoriaRepositorio>();

    private ResponderCasoUso CasoDeUso =>
        new(_kudos, _replicas, _personas, _unidad, _bandeja, _cache, new ServicioAuditoria(_eventos), new RelojFijo());

    private static readonly Persona Ana = new("ana@uco.edu.co", "+573001112233", "Ana Gómez", Ahora);
    private static readonly Persona Carlos = new("carlos@uco.edu.co", "+573004445566", "Carlos Mejía", Ahora);

    private static ContextoPeticion Como(Persona persona) =>
        new(new AmbitoUsuario(persona.Id, Rol.Docente, null, null), "traza");

    /// <summary>Un Kudo de Ana para Carlos.</summary>
    private Kudo UnKudo()
    {
        var kudo = new Kudo(Ana.Id, Carlos.Id, Guid.NewGuid(), "Gracias.", Ahora);
        _kudos.PorIdAsync(kudo.Id, Arg.Any<CancellationToken>()).Returns(kudo);
        _personas.PorIdAsync(Ana.Id, Arg.Any<CancellationToken>()).Returns(Ana);
        _personas.PorIdAsync(Carlos.Id, Arg.Any<CancellationToken>()).Returns(Carlos);
        return kudo;
    }

    [Fact]
    public async Task Responder_a_un_kudo_crea_una_replica_de_primer_nivel_con_el_nombre_del_autor()
    {
        var kudo = UnKudo();
        _kudos.ContarReplicasAsync(kudo.Id, Arg.Any<CancellationToken>()).Returns(0);

        var creada = await CasoDeUso.EjecutarAsync(Como(Carlos), kudo.Id, new ResponderDto("Gracias.", null), CancellationToken.None);

        Assert.Equal(1, creada.Profundidad);
        Assert.Null(creada.ReplicaPadreId);
        Assert.Equal("Carlos Mejía", creada.AutorNombre);
        _replicas.Received(1).Agregar(Arg.Any<Replica>());
        await _unidad.Received(1).ConfirmarAsync(Arg.Any<CancellationToken>());
        await _cache.Received(1).InvalidarGrupoAsync(GruposCache.Muro, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Responder_a_una_replica_la_cuelga_de_su_padre_con_un_nivel_mas()
    {
        var kudo = UnKudo();
        var padre = Replica.SobreKudo(kudo.Id, Ana.Id, "Primera.", Ahora);
        _replicas.PorIdAsync(padre.Id, Arg.Any<CancellationToken>()).Returns(padre);

        var creada = await CasoDeUso.EjecutarAsync(Como(Carlos), kudo.Id, new ResponderDto("Segunda.", padre.Id), CancellationToken.None);

        Assert.Equal(padre.Id, creada.ReplicaPadreId);
        Assert.Equal(2, creada.Profundidad);
    }

    [Fact]
    public async Task El_contador_del_kudo_queda_al_dia_en_la_misma_transaccion()
    {
        var kudo = UnKudo();
        _kudos.ContarReplicasAsync(kudo.Id, Arg.Any<CancellationToken>()).Returns(4);

        await CasoDeUso.EjecutarAsync(Como(Ana), kudo.Id, new ResponderDto("Otra.", null), CancellationToken.None);

        Assert.Equal(5, kudo.TotalReplicas);
    }

    [Fact]
    public async Task Se_avisa_al_destinatario_del_kudo_pero_no_cuando_es_quien_responde()
    {
        var kudo = UnKudo();

        // Responde Ana (autora): se avisa a Carlos, el destinatario.
        await CasoDeUso.EjecutarAsync(Como(Ana), kudo.Id, new ResponderDto("Una nota.", null), CancellationToken.None);
        _bandeja.Received(1).Programar(Arg.Is<EventoPendiente>(e => e.TipoEvento == TiposEvento.ReplicaPublicada));

        _bandeja.ClearReceivedCalls();

        // Responde Carlos, que es el destinatario: no se avisa a sí mismo.
        await CasoDeUso.EjecutarAsync(Como(Carlos), kudo.Id, new ResponderDto("Gracias.", null), CancellationToken.None);
        _bandeja.DidNotReceive().Programar(Arg.Any<EventoPendiente>());
    }

    [Fact]
    public async Task Responder_queda_auditado_con_la_profundidad()
    {
        var kudo = UnKudo();

        await CasoDeUso.EjecutarAsync(Como(Carlos), kudo.Id, new ResponderDto("Gracias.", null), CancellationToken.None);

        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(
            e => e.Accion == AccionesAuditoria.ReplicaPublicar && e.Datos.Contains("\"profundidad\":1", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task No_se_responde_a_un_kudo_que_no_existe_ni_a_uno_vetado()
    {
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.EjecutarAsync(Como(Carlos), Guid.NewGuid(), new ResponderDto("x", null), CancellationToken.None));

        var kudo = UnKudo();
        kudo.Vetar();

        var error = await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.EjecutarAsync(Como(Carlos), kudo.Id, new ResponderDto("x", null), CancellationToken.None));

        Assert.Contains("moderación", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_se_responde_a_una_replica_que_no_existe_vetada_o_de_otro_kudo()
    {
        var kudo = UnKudo();

        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.EjecutarAsync(Como(Carlos), kudo.Id, new ResponderDto("x", Guid.NewGuid()), CancellationToken.None));

        var vetada = Replica.SobreKudo(kudo.Id, Ana.Id, "Vetada.", Ahora);
        vetada.Vetar();
        _replicas.PorIdAsync(vetada.Id, Arg.Any<CancellationToken>()).Returns(vetada);

        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.EjecutarAsync(Como(Carlos), kudo.Id, new ResponderDto("x", vetada.Id), CancellationToken.None));

        var deOtro = Replica.SobreKudo(Guid.NewGuid(), Ana.Id, "En otro Kudo.", Ahora);
        _replicas.PorIdAsync(deOtro.Id, Arg.Any<CancellationToken>()).Returns(deOtro);

        var error = await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.EjecutarAsync(Como(Carlos), kudo.Id, new ResponderDto("x", deOtro.Id), CancellationToken.None));

        Assert.Contains("otro Kudo", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_hilo_omite_lo_vetado_y_pone_el_nombre_de_cada_autor()
    {
        var kudo = UnKudo();
        var visible = Replica.SobreKudo(kudo.Id, Ana.Id, "Visible.", Ahora);
        var vetada = Replica.SobreKudo(kudo.Id, Carlos.Id, "Vetada.", Ahora.AddMinutes(1));
        vetada.Vetar();
        var desconocida = Replica.SobreKudo(kudo.Id, Guid.NewGuid(), "De alguien sin ficha.", Ahora.AddMinutes(2));
        _replicas.HiloDeAsync(kudo.Id, Arg.Any<CancellationToken>()).Returns([visible, vetada, desconocida]);

        var hilo = await CasoDeUso.HiloAsync(kudo.Id, CancellationToken.None);

        Assert.Equal(2, hilo.Count);
        Assert.Equal("Ana Gómez", hilo[0].AutorNombre);
        Assert.Equal("Alguien", hilo[1].AutorNombre);
        Assert.DoesNotContain(hilo, r => r.Contenido == "Vetada.");
    }
}
