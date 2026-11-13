using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using UcoKudos.Application.Indicadores;
using UcoKudos.Application.Interaccion.Entrada.Dto;
using UcoKudos.Application.Kudos.Entrada.Dto;
using UcoKudos.Domain.Academico;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Identidad;
using UcoKudos.IntegrationTests.Infraestructura;

namespace UcoKudos.IntegrationTests.E2E;

/// <summary>
/// Reportes e indicadores de gestión, por HTTP y contra PostgreSQL real.
///
/// Son agregaciones que EF traduce a SQL, y esa traducción es justo lo que un doble en memoria no
/// comprobaría: por eso estas pruebas corren contra la base de verdad.
/// </summary>
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Category", "E2E")]
public sealed class IndicadoresDeExtremoAExtremoTests(BaseDeDatosFixture baseDatos)
    : EscenarioDeExtremoAExtremo(baseDatos)
{
    private async Task<Guid> OtraCategoriaAsync()
    {
        await using var contexto = BaseDatos.CrearContexto();
        return await contexto.Categorias.Where(c => c.Id != Semillas.CategoriaRendimiento).Select(c => c.Id).FirstAsync();
    }

    private async Task<Guid> PublicarAsync(Guid destinatarioId, Guid categoriaId, string autor = CorreoDocente)
    {
        using var cliente = Como(autor);
        var respuesta = await cliente.PostAsJsonAsync("/api/kudos", new PublicarKudoDto(destinatarioId, categoriaId, "Gracias por todo."));
        Assert.True(respuesta.IsSuccessStatusCode, await respuesta.Content.ReadAsStringAsync());

        return (await respuesta.Content.ReadFromJsonAsync<KudoPublicadoDto>(Json))!.Id;
    }

    private async Task<IndicadoresDto> IndicadoresAsync(string correo)
    {
        using var cliente = Como(correo);
        return (await cliente.GetFromJsonAsync<IndicadoresDto>("/api/indicadores", Json))!;
    }

    /// <summary>Otra facultad con un programa, un estudiante activo en él y un Kudo para esa persona.</summary>
    private async Task<(Guid EstudianteId, Guid ProgramaId)> OtroProgramaConEstudianteAsync()
    {
        await using var contexto = BaseDatos.CrearContexto();
        var ahora = DateTimeOffset.UtcNow;

        var facultad = new Facultad("Ciencias", "CIE", ahora);
        var programa = new Programa(facultad.Id, "Biología", "BIO", ahora);
        var estudiante = Semillas.Activa(new Persona("estudiante.bio@uco.edu.co", "+573005550001", "Sofía Ríos", ahora));

        contexto.Facultades.Add(facultad);
        contexto.Programas.Add(programa);
        contexto.Personas.Add(estudiante);
        contexto.AsignacionesRol.Add(new AsignacionRol(estudiante.Id, Rol.Estudiante, facultad.Id, programa.Id, ahora));
        await contexto.SaveChangesAsync();

        return (estudiante.Id, programa.Id);
    }

    [Fact]
    public async Task Sin_ningun_kudo_los_indicadores_estan_en_cero_y_la_serie_mensual_completa()
    {
        var indicadores = await IndicadoresAsync(CorreoRector);

        Assert.Equal("Toda la universidad", indicadores.Alcance);
        Assert.Equal(0, indicadores.KudosPublicados);
        Assert.Null(indicadores.CalificacionPromedio);
        Assert.Empty(indicadores.PorCategoria);
        Assert.Empty(indicadores.MasReconocidos);

        // Seis meses, todos, aunque estén en cero: un mes sin Kudos también es un dato.
        Assert.Equal(ConsultarIndicadoresCasoUso.MesesEnLaSerie, indicadores.PorMes.Count);
        Assert.All(indicadores.PorMes, m => Assert.Equal(0, m.Total));
    }

    [Fact]
    public async Task El_rector_ve_los_totales_por_categoria_por_mes_y_las_personas_mas_activas()
    {
        var otraCategoria = await OtraCategoriaAsync();
        await PublicarAsync(Datos.DestinatarioId, Semillas.CategoriaRendimiento);
        await PublicarAsync(Datos.DestinatarioId, Semillas.CategoriaRendimiento);
        await PublicarAsync(Datos.DestinatarioId, otraCategoria);

        var indicadores = await IndicadoresAsync(CorreoRector);

        Assert.Equal(3, indicadores.KudosPublicados);

        // 7 personas activas con rol: las dos de la semilla y las cinco de los demás roles.
        Assert.Equal(7, indicadores.PersonasActivas);

        // Por categoría, de mayor a menor.
        Assert.Equal(2, indicadores.PorCategoria[0].Total);
        Assert.Equal(1, indicadores.PorCategoria[1].Total);

        // Por mes: el actual es el último de la serie y tiene los tres.
        var mesActual = DateTimeOffset.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        Assert.Equal(mesActual, indicadores.PorMes[^1].Etiqueta);
        Assert.Equal(3, indicadores.PorMes[^1].Total);

        // Quién recibe y quién escribe.
        Assert.Equal(new ConteoDto("Carlos Mejía", 3), indicadores.MasReconocidos[0]);
        Assert.Equal(new ConteoDto("Ana Gómez", 3), indicadores.MasReconocen[0]);
    }

    [Fact]
    public async Task Lo_vetado_no_cuenta()
    {
        var kudo = await PublicarAsync(Datos.DestinatarioId, Semillas.CategoriaRendimiento);
        await PublicarAsync(Datos.DestinatarioId, Semillas.CategoriaRendimiento);

        using var rector = Como(CorreoRector);
        await rector.PostAsJsonAsync("/api/moderacion/vetos", new VetarDto("Kudo", kudo, "Ofensivo."));

        var indicadores = await IndicadoresAsync(CorreoRector);

        Assert.Equal(1, indicadores.KudosPublicados);
        Assert.Equal(1, indicadores.MasReconocidos[0].Total);
    }

    [Fact]
    public async Task La_calificacion_promedio_pondera_todas_las_calificaciones()
    {
        var uno = await PublicarAsync(Datos.DestinatarioId, Semillas.CategoriaRendimiento);
        var dos = await PublicarAsync(Datos.DestinatarioId, Semillas.CategoriaRendimiento);

        using var rector = Como(CorreoRector);
        using var coordinador = Como(CorreoCoordinadorPrograma);

        // Un Kudo con 5 y 3 (promedio 4, dos calificaciones) y otro con 2 (una calificación):
        // (5 + 3 + 2) / 3 = 3,33, y no el promedio de los promedios.
        await rector.PostAsJsonAsync("/api/interaccion/calificaciones", new CalificarDto("Kudo", uno, 5));
        await coordinador.PostAsJsonAsync("/api/interaccion/calificaciones", new CalificarDto("Kudo", uno, 3));
        await rector.PostAsJsonAsync("/api/interaccion/calificaciones", new CalificarDto("Kudo", dos, 2));

        var indicadores = await IndicadoresAsync(CorreoRector);

        Assert.Equal(3, indicadores.TotalCalificaciones);
        Assert.Equal(3.33m, indicadores.CalificacionPromedio);
    }

    [Fact]
    public async Task Cada_coordinador_mide_su_ambito_y_el_rector_toda_la_universidad()
    {
        var (otroEstudiante, _) = await OtroProgramaConEstudianteAsync();

        // Dos Kudos para Carlos (programa de Sistemas) y uno para Sofía (programa de Biología).
        await PublicarAsync(Datos.DestinatarioId, Semillas.CategoriaRendimiento);
        await PublicarAsync(Datos.DestinatarioId, Semillas.CategoriaRendimiento);
        await PublicarAsync(otroEstudiante, Semillas.CategoriaRendimiento);

        var delRector = await IndicadoresAsync(CorreoRector);
        var delPrograma = await IndicadoresAsync(CorreoCoordinadorPrograma);
        var deLaFacultad = await IndicadoresAsync(CorreoCoordinadorFacultad);

        Assert.Equal(3, delRector.KudosPublicados);

        Assert.Equal("Tu programa", delPrograma.Alcance);
        Assert.Equal(2, delPrograma.KudosPublicados);
        Assert.Equal("Carlos Mejía", Assert.Single(delPrograma.MasReconocidos).Etiqueta);

        // La facultad de Sistemas tampoco incluye a la de Biología.
        Assert.Equal("Tu facultad", deLaFacultad.Alcance);
        Assert.Equal(2, deLaFacultad.KudosPublicados);
    }

    [Fact]
    public async Task Los_reportes_pendientes_solo_se_enseñan_a_quien_modera()
    {
        var kudo = await PublicarAsync(Datos.DestinatarioId, Semillas.CategoriaRendimiento);

        using var estudiante = Como(CorreoEstudiante);
        await estudiante.PostAsJsonAsync("/api/moderacion/reportes", new ReportarDto("Kudo", kudo, "Ofensivo."));

        Assert.Equal(1, (await IndicadoresAsync(CorreoRector)).ReportesPendientes);
        Assert.Equal(1, (await IndicadoresAsync(CorreoCoordinadorAcademico)).ReportesPendientes);

        // Para el resto es nulo y no cero: no se les dice «no hay», se les calla.
        Assert.Null((await IndicadoresAsync(CorreoCoordinadorPrograma)).ReportesPendientes);
        Assert.Null((await IndicadoresAsync(CorreoAdministrador)).ReportesPendientes);
    }

    [Theory]
    [InlineData(CorreoDocente)]
    [InlineData(CorreoEstudiante)]
    public async Task Docentes_y_estudiantes_no_ven_indicadores_y_el_intento_queda_auditado(string correo)
    {
        using var cliente = Como(correo);

        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/indicadores")).StatusCode);

        await using var contexto = BaseDatos.CrearContexto();
        Assert.Contains(AccionesAuditoria.AccesoRechazado, await contexto.EventosAuditoria.Select(e => e.Accion).ToListAsync());
    }

    [Fact]
    public async Task Sin_identidad_no_hay_indicadores()
    {
        using var anonimo = Como();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/indicadores")).StatusCode);
    }
}
