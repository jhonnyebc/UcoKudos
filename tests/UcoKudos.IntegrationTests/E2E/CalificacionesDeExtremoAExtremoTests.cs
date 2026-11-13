using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using UcoKudos.Application.Interaccion.Entrada.Dto;
using UcoKudos.Application.Kudos.Entrada.Dto;
using UcoKudos.Domain.Auditoria;
using UcoKudos.IntegrationTests.Infraestructura;

namespace UcoKudos.IntegrationTests.E2E;

/// <summary>
/// Calificar (RF-10), por HTTP y de punta a punta: de 1 a 5 estrellas, una calificación activa por
/// persona y contenido, y el promedio y el total del muro al día.
///
/// Cambiar de opinión **no edita** la calificación: desactiva la anterior y crea otra, así que queda
/// el histórico y el promedio no cuenta dos veces a la misma persona.
/// </summary>
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Category", "E2E")]
public sealed class CalificacionesDeExtremoAExtremoTests(BaseDeDatosFixture baseDatos)
    : EscenarioDeExtremoAExtremo(baseDatos)
{
    private async Task<Guid> PublicarKudoAsync()
    {
        using var autor = Como(CorreoDocente);
        var respuesta = await autor.PostAsJsonAsync(
            "/api/kudos", new PublicarKudoDto(Datos.DestinatarioId, Datos.CategoriaId, "Gracias por el informe."));
        Assert.True(respuesta.IsSuccessStatusCode, await respuesta.Content.ReadAsStringAsync());

        return (await respuesta.Content.ReadFromJsonAsync<KudoPublicadoDto>(Json))!.Id;
    }

    private static Task<HttpResponseMessage> CalificarAsync(HttpClient cliente, string tipo, Guid id, short estrellas) =>
        cliente.PostAsJsonAsync("/api/interaccion/calificaciones", new CalificarDto(tipo, id, estrellas));

    private async Task<KudoDelMuroDto> EnElMuroAsync(Guid kudoId)
    {
        using var cliente = Como(CorreoEstudiante);
        var muro = (await cliente.GetFromJsonAsync<PaginaMuroDto>("/api/kudos", Json))!;

        return muro.Elementos.Single(k => k.Id == kudoId);
    }

    [Fact]
    public async Task Una_calificacion_aparece_en_el_muro_con_su_promedio_y_su_total()
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        Assert.Equal(HttpStatusCode.NoContent, (await CalificarAsync(estudiante, "Kudo", kudo, 5)).StatusCode);

        var fila = await EnElMuroAsync(kudo);
        Assert.Equal(1, fila.TotalCalificaciones);
        Assert.Equal(5m, fila.PromedioCalificacion);
    }

    [Fact]
    public async Task El_promedio_reune_las_calificaciones_de_varias_personas()
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        using var coordinador = Como(CorreoCoordinadorPrograma);
        using var rector = Como(CorreoRector);

        await CalificarAsync(estudiante, "Kudo", kudo, 5);
        await CalificarAsync(coordinador, "Kudo", kudo, 4);
        await CalificarAsync(rector, "Kudo", kudo, 3);

        var fila = await EnElMuroAsync(kudo);
        Assert.Equal(3, fila.TotalCalificaciones);
        Assert.Equal(4m, fila.PromedioCalificacion);
    }

    /// <summary>
    /// Cambiar de opinión reemplaza: el total no sube y el promedio refleja el valor nuevo, no la
    /// suma de los dos. La anterior queda desactivada en la base como histórico.
    /// </summary>
    [Fact]
    public async Task Cambiar_la_calificacion_reemplaza_la_anterior_y_deja_el_historico()
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        using var rector = Como(CorreoRector);
        await CalificarAsync(estudiante, "Kudo", kudo, 5);
        await CalificarAsync(rector, "Kudo", kudo, 3);

        await CalificarAsync(estudiante, "Kudo", kudo, 1);

        var fila = await EnElMuroAsync(kudo);
        Assert.Equal(2, fila.TotalCalificaciones);
        Assert.Equal(2m, fila.PromedioCalificacion);

        await using var contexto = BaseDatos.CrearContexto();
        var deLaPersona = await contexto.Calificaciones
            .Where(c => c.PersonaId == Datos.DestinatarioId && c.ContenidoId == kudo)
            .ToListAsync();

        Assert.Equal(2, deLaPersona.Count);
        Assert.Single(deLaPersona, c => c.Activa);
        Assert.Equal(1, deLaPersona.Single(c => c.Activa).Estrellas);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task Una_calificacion_fuera_de_uno_a_cinco_se_rechaza(short estrellas)
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        var respuesta = await CalificarAsync(estudiante, "Kudo", kudo, estrellas);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(0, (await EnElMuroAsync(kudo)).TotalCalificaciones);
    }

    [Fact]
    public async Task Se_puede_calificar_una_replica()
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        var replica = await estudiante.PostAsJsonAsync($"/api/kudos/{kudo}/replicas", new ResponderDto("Gracias.", null));
        var replicaId = (await replica.Content.ReadFromJsonAsync<ReplicaDto>(Json))!.Id;

        using var docente = Como(CorreoDocente);
        Assert.Equal(HttpStatusCode.NoContent, (await CalificarAsync(docente, "Replica", replicaId, 4)).StatusCode);

        var hilo = (await docente.GetFromJsonAsync<List<ReplicaDto>>($"/api/kudos/{kudo}/replicas", Json))!;
        var fila = Assert.Single(hilo);
        Assert.Equal(1, fila.TotalCalificaciones);
        Assert.Equal(4m, fila.PromedioCalificacion);
    }

    [Fact]
    public async Task No_se_califica_lo_que_no_existe_ni_sin_identidad()
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        Assert.Equal(HttpStatusCode.BadRequest, (await CalificarAsync(estudiante, "Kudo", Guid.NewGuid(), 5)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CalificarAsync(estudiante, "Replica", Guid.NewGuid(), 5)).StatusCode);

        using var anonimo = Como();
        Assert.Equal(HttpStatusCode.Unauthorized, (await CalificarAsync(anonimo, "Kudo", kudo, 5)).StatusCode);
    }

    [Fact]
    public async Task No_se_califica_un_kudo_vetado()
    {
        var kudo = await PublicarKudoAsync();

        using var rector = Como(CorreoRector);
        await rector.PostAsJsonAsync("/api/moderacion/vetos", new VetarDto("Kudo", kudo, "Ofensivo."));

        using var estudiante = Como(CorreoEstudiante);
        var respuesta = await CalificarAsync(estudiante, "Kudo", kudo, 5);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task Calificar_deja_rastro_en_la_auditoria_con_quien_califico_y_si_reemplazo()
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        await CalificarAsync(estudiante, "Kudo", kudo, 4);
        await CalificarAsync(estudiante, "Kudo", kudo, 2);

        await using var contexto = BaseDatos.CrearContexto();
        var eventos = await contexto.EventosAuditoria
            .Where(e => e.Accion == AccionesAuditoria.ContenidoCalificar)
            .OrderBy(e => e.Id)
            .ToListAsync();

        Assert.Equal(2, eventos.Count);
        Assert.All(eventos, e => Assert.Equal(Datos.DestinatarioId, e.ActorId));
        Assert.Contains("\"reemplazo\":false", eventos[0].Datos, StringComparison.Ordinal);
        Assert.Contains("\"reemplazo\":true", eventos[1].Datos, StringComparison.Ordinal);
    }
}
