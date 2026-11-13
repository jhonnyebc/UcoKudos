using NSubstitute;
using UcoKudos.Application.Comun;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Application.Indicadores;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Auditoria.Repositorios;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.UnitTests.Application.Indicadores;

/// <summary>
/// Indicadores de gestión a nivel de caso de uso: quién los ve, sobre qué ámbito, y cómo se completan
/// y se calculan las cifras. La traducción a SQL se prueba contra la base real, en punta a punta.
/// </summary>
public sealed class ConsultarIndicadoresCasoUsoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private readonly IConsultasIndicadores _consultas = Substitute.For<IConsultasIndicadores>();
    private readonly IUnidadTrabajo _unidad = Substitute.For<IUnidadTrabajo>();
    private readonly IEventoAuditoriaRepositorio _eventos = Substitute.For<IEventoAuditoriaRepositorio>();

    private ConsultarIndicadoresCasoUso CasoDeUso =>
        new(_consultas, _unidad, new ServicioAuditoria(_eventos), new RelojFijo());

    private static ContextoPeticion Como(Rol rol, Guid? facultadId = null, Guid? programaId = null) =>
        new(new AmbitoUsuario(Guid.NewGuid(), rol, facultadId, programaId), "traza");

    private static ResumenCrudo Crudo(
        int kudos = 0,
        decimal suma = 0,
        int calificaciones = 0,
        int reportes = 0,
        IReadOnlyList<ConteoDto>? categorias = null,
        IReadOnlyList<ConteoMes>? meses = null) =>
        new(kudos, 5, suma, calificaciones, reportes, categorias ?? [], meses ?? [], [], []);

    [Theory]
    [InlineData(1, "Toda la universidad")]
    [InlineData(2, "Toda la universidad")]
    [InlineData(3, "Toda la universidad")]
    public async Task Los_roles_de_universidad_miden_toda_la_universidad(short rolId, string etiqueta)
    {
        _consultas.ResumenAsync(Arg.Any<AlcanceDeIndicadores>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Crudo());

        var indicadores = await CasoDeUso.ConsultarAsync(Como(Rol.Por(rolId)), CancellationToken.None);

        Assert.Equal(etiqueta, indicadores.Alcance);
        await _consultas.Received(1).ResumenAsync(
            Arg.Is<AlcanceDeIndicadores>(a => a.FacultadId == null && a.ProgramaId == null),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_coordinador_de_facultad_mide_su_facultad_y_uno_de_programa_su_programa()
    {
        var facultad = Guid.NewGuid();
        var programa = Guid.NewGuid();
        _consultas.ResumenAsync(Arg.Any<AlcanceDeIndicadores>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Crudo());

        var deFacultad = await CasoDeUso.ConsultarAsync(Como(Rol.CoordinadorFacultad, facultad), CancellationToken.None);
        var dePrograma = await CasoDeUso.ConsultarAsync(Como(Rol.CoordinadorPrograma, facultad, programa), CancellationToken.None);

        Assert.Equal("Tu facultad", deFacultad.Alcance);
        Assert.Equal("Tu programa", dePrograma.Alcance);

        await _consultas.Received(1).ResumenAsync(
            Arg.Is<AlcanceDeIndicadores>(a => a.FacultadId == facultad && a.ProgramaId == null),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _consultas.Received(1).ResumenAsync(
            Arg.Is<AlcanceDeIndicadores>(a => a.FacultadId == null && a.ProgramaId == programa),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public async Task Docentes_y_estudiantes_no_ven_indicadores_y_el_intento_queda_auditado(short rolId)
    {
        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() =>
            CasoDeUso.ConsultarAsync(Como(Rol.Por(rolId)), CancellationToken.None));

        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.AccesoRechazado));
        await _consultas.DidNotReceive().ResumenAsync(
            Arg.Any<AlcanceDeIndicadores>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task La_serie_mensual_trae_los_seis_meses_aunque_algunos_esten_vacios()
    {
        _consultas.ResumenAsync(Arg.Any<AlcanceDeIndicadores>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Crudo(meses: [new ConteoMes(2026, 11, 4), new ConteoMes(2026, 9, 2)]));

        var indicadores = await CasoDeUso.ConsultarAsync(Como(Rol.Rector), CancellationToken.None);

        Assert.Equal(
            ["2026-06", "2026-07", "2026-08", "2026-09", "2026-10", "2026-11"],
            indicadores.PorMes.Select(m => m.Etiqueta).ToList());
        Assert.Equal([0, 0, 0, 2, 0, 4], indicadores.PorMes.Select(m => m.Total).ToList());
    }

    [Fact]
    public async Task La_serie_empieza_en_el_primer_dia_del_mes_de_hace_cinco_meses()
    {
        _consultas.ResumenAsync(Arg.Any<AlcanceDeIndicadores>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Crudo());

        await CasoDeUso.ConsultarAsync(Como(Rol.Rector), CancellationToken.None);

        await _consultas.Received(1).ResumenAsync(
            Arg.Any<AlcanceDeIndicadores>(),
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task El_promedio_se_pondera_por_calificaciones_y_es_nulo_si_no_hay_ninguna()
    {
        _consultas.ResumenAsync(Arg.Any<AlcanceDeIndicadores>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Crudo(suma: 10m, calificaciones: 3));

        var conDatos = await CasoDeUso.ConsultarAsync(Como(Rol.Rector), CancellationToken.None);
        Assert.Equal(3.33m, conDatos.CalificacionPromedio);
        Assert.Equal(3, conDatos.TotalCalificaciones);

        _consultas.ResumenAsync(Arg.Any<AlcanceDeIndicadores>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Crudo());

        var sinDatos = await CasoDeUso.ConsultarAsync(Como(Rol.Rector), CancellationToken.None);
        Assert.Null(sinDatos.CalificacionPromedio);
    }

    [Fact]
    public async Task Las_categorias_salen_de_mayor_a_menor_y_con_desempate_estable()
    {
        _consultas.ResumenAsync(Arg.Any<AlcanceDeIndicadores>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Crudo(categorias: [new ConteoDto("Puntualidad", 2), new ConteoDto("Liderazgo", 5), new ConteoDto("Apoyo", 2)]));

        var indicadores = await CasoDeUso.ConsultarAsync(Como(Rol.Rector), CancellationToken.None);

        Assert.Equal(["Liderazgo", "Apoyo", "Puntualidad"], indicadores.PorCategoria.Select(c => c.Etiqueta).ToList());
    }

    [Fact]
    public async Task Los_reportes_pendientes_solo_se_enseñan_a_quien_puede_vetar()
    {
        _consultas.ResumenAsync(Arg.Any<AlcanceDeIndicadores>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Crudo(reportes: 4));

        Assert.Equal(4, (await CasoDeUso.ConsultarAsync(Como(Rol.Rector), CancellationToken.None)).ReportesPendientes);
        Assert.Equal(4, (await CasoDeUso.ConsultarAsync(Como(Rol.CoordinadorAcademico), CancellationToken.None)).ReportesPendientes);
        Assert.Null((await CasoDeUso.ConsultarAsync(Como(Rol.Administrador), CancellationToken.None)).ReportesPendientes);
        Assert.Null((await CasoDeUso.ConsultarAsync(Como(Rol.CoordinadorPrograma, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None)).ReportesPendientes);
    }
}
