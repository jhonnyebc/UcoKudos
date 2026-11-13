using System.Globalization;
using UcoKudos.Application.Comun;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.Application.Indicadores;

/// <summary>Qué parte de la universidad se mide: todo, una facultad o un programa.</summary>
public sealed record AlcanceDeIndicadores(Guid? FacultadId, Guid? ProgramaId);

/// <summary>Un conteo con su etiqueta: una categoría, una persona.</summary>
public sealed record ConteoDto(string Etiqueta, int Total);

/// <summary>Un conteo por mes calendario.</summary>
public sealed record ConteoMes(int Anio, int Mes, int Total);

/// <summary>Lo que devuelve la consulta de lectura, sin interpretar: el caso de uso lo ordena y lo completa.</summary>
public sealed record ResumenCrudo(
    int Kudos,
    int Personas,
    decimal SumaPonderadaCalificaciones,
    int TotalCalificaciones,
    int ReportesPendientes,
    IReadOnlyList<ConteoDto> PorCategoria,
    IReadOnlyList<ConteoMes> PorMes,
    IReadOnlyList<ConteoDto> MasReconocidos,
    IReadOnlyList<ConteoDto> MasReconocen);

/// <summary>Lado de lectura de los indicadores. Es una consulta de agregación, no entidades de dominio.</summary>
public interface IConsultasIndicadores
{
    Task<ResumenCrudo> ResumenAsync(AlcanceDeIndicadores alcance, DateTimeOffset desde, CancellationToken ct);
}

/// <summary>Los indicadores tal como los consume la pantalla.</summary>
/// <param name="Alcance">Qué se está midiendo: «Toda la universidad», «Tu facultad» o «Tu programa».</param>
/// <param name="ReportesPendientes">Solo para quien modera; para el resto es nulo, no cero.</param>
public sealed record IndicadoresDto(
    string Alcance,
    int KudosPublicados,
    int PersonasActivas,
    decimal? CalificacionPromedio,
    int TotalCalificaciones,
    int? ReportesPendientes,
    IReadOnlyList<ConteoDto> PorCategoria,
    IReadOnlyList<ConteoDto> PorMes,
    IReadOnlyList<ConteoDto> MasReconocidos,
    IReadOnlyList<ConteoDto> MasReconocen);

public interface IIndicadoresPuertoEntrada
{
    Task<IndicadoresDto> ConsultarAsync(ContextoPeticion contexto, CancellationToken ct);
}

/// <summary>
/// Reportes e indicadores: cuántos reconocimientos hay, de qué categorías, cuándo, y quiénes
/// reconocen y son reconocidos.
///
/// **Cada quien mide solo su ámbito**: los roles de universidad, toda; un Coordinador de Facultad, su
/// facultad; uno de Programa, su programa. Un Kudo cuenta en el ámbito donde está **su destinatario**.
/// Los docentes y los estudiantes no ven indicadores: son una herramienta de gestión, no de consulta.
///
/// Lo vetado no cuenta: un indicador que sumara contenido oculto mediría algo que nadie ve.
/// </summary>
public sealed class ConsultarIndicadoresCasoUso(
    IConsultasIndicadores consultas,
    IUnidadTrabajo unidadTrabajo,
    ServicioAuditoria auditoria,
    TimeProvider reloj) : IIndicadoresPuertoEntrada
{
    /// <summary>Cuántos meses de historia se enseñan, contando el actual.</summary>
    public const int MesesEnLaSerie = 6;

    public async Task<IndicadoresDto> ConsultarAsync(ContextoPeticion contexto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var ahora = reloj.GetUtcNow();

        if (!JerarquiaInstitucional.PuedeVerIndicadores(contexto.Ambito))
        {
            await auditoria.RegistrarAccesoRechazadoAsync(contexto, "ver los indicadores", Guid.Empty, ahora, ct);
            await unidadTrabajo.ConfirmarAsync(ct);

            throw new ExcepcionAutorizacion("El rol " + contexto.Rol.Nombre + " no ve los indicadores.");
        }

        var (alcance, etiqueta) = contexto.Rol.Ambito switch
        {
            AmbitoRol.Facultad => (new AlcanceDeIndicadores(contexto.Ambito.FacultadId, null), "Tu facultad"),
            AmbitoRol.Programa => (new AlcanceDeIndicadores(null, contexto.Ambito.ProgramaId), "Tu programa"),
            _ => (new AlcanceDeIndicadores(null, null), "Toda la universidad")
        };

        var primerMes = new DateTimeOffset(ahora.Year, ahora.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-(MesesEnLaSerie - 1));
        var crudo = await consultas.ResumenAsync(alcance, primerMes, ct);

        decimal? promedio = crudo.TotalCalificaciones == 0
            ? null
            : Math.Round(crudo.SumaPonderadaCalificaciones / crudo.TotalCalificaciones, 2);

        return new IndicadoresDto(
            etiqueta,
            crudo.Kudos,
            crudo.Personas,
            promedio,
            crudo.TotalCalificaciones,
            JerarquiaInstitucional.PuedeVetar(contexto.Ambito) ? crudo.ReportesPendientes : null,
            [.. crudo.PorCategoria.OrderByDescending(c => c.Total).ThenBy(c => c.Etiqueta, StringComparer.Ordinal)],
            SerieMensual(crudo.PorMes, primerMes),
            crudo.MasReconocidos,
            crudo.MasReconocen);
    }

    /// <summary>
    /// Los últimos meses **todos**, también los que no tuvieron ningún Kudo: una serie con huecos
    /// distorsiona el gráfico, y un mes en cero también es un dato.
    /// </summary>
    private static List<ConteoDto> SerieMensual(IReadOnlyList<ConteoMes> meses, DateTimeOffset primerMes)
    {
        var serie = new List<ConteoDto>();

        for (var i = 0; i < MesesEnLaSerie; i++)
        {
            var mes = primerMes.AddMonths(i);
            var total = meses.Where(m => m.Anio == mes.Year && m.Mes == mes.Month).Sum(m => m.Total);

            serie.Add(new ConteoDto(mes.ToString("yyyy-MM", CultureInfo.InvariantCulture), total));
        }

        return serie;
    }
}
