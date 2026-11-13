using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UcoKudos.Application.Interaccion.Entrada.Dto;
using UcoKudos.Application.Kudos.Entrada.Dto;
using UcoKudos.Domain.Auditoria;
using UcoKudos.IntegrationTests.Infraestructura;

namespace UcoKudos.IntegrationTests.E2E;

/// <summary>
/// Réplicas anidadas (RF-09), por HTTP y de punta a punta: se responde a un Kudo o a otra réplica,
/// **sin límite de profundidad, de cantidad ni de longitud**, y el hilo llega ya en el orden en que
/// se pinta, con la profundidad para la sangría.
/// </summary>
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Category", "E2E")]
public sealed class ReplicasDeExtremoAExtremoTests(BaseDeDatosFixture baseDatos)
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

    private static Task<HttpResponseMessage> ResponderAsync(HttpClient cliente, Guid kudo, string texto, Guid? padre = null) =>
        cliente.PostAsJsonAsync($"/api/kudos/{kudo}/replicas", new ResponderDto(texto, padre));

    private static async Task<ReplicaDto> CrearAsync(HttpClient cliente, Guid kudo, string texto, Guid? padre = null)
    {
        var respuesta = await ResponderAsync(cliente, kudo, texto, padre);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        return (await respuesta.Content.ReadFromJsonAsync<ReplicaDto>(Json))!;
    }

    private async Task<List<ReplicaDto>> HiloAsync(Guid kudo)
    {
        using var cliente = Como(CorreoEstudiante);
        return (await cliente.GetFromJsonAsync<List<ReplicaDto>>($"/api/kudos/{kudo}/replicas", Json))!;
    }

    [Fact]
    public async Task Se_responde_a_un_kudo_y_a_otra_replica_y_el_hilo_llega_en_orden_con_su_profundidad()
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        using var docente = Como(CorreoDocente);

        var primera = await CrearAsync(estudiante, kudo, "Primera respuesta.");
        var segundaRaiz = await CrearAsync(docente, kudo, "Otra respuesta al Kudo.");
        var hija = await CrearAsync(docente, kudo, "Respuesta a la primera.", primera.Id);
        var nieta = await CrearAsync(estudiante, kudo, "Respuesta a la respuesta.", hija.Id);

        var hilo = await HiloAsync(kudo);

        // Orden de presentación: cada rama junto a sus descendientes.
        Assert.Equal([primera.Id, hija.Id, nieta.Id, segundaRaiz.Id], hilo.Select(r => r.Id).ToList());
        Assert.Equal([1, 2, 3, 1], hilo.Select(r => (int)r.Profundidad).ToList());
        Assert.Equal(primera.Id, hilo[1].ReplicaPadreId);
        Assert.Null(hilo[0].ReplicaPadreId);
    }

    [Fact]
    public async Task Cada_replica_lleva_el_nombre_de_su_autor()
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        var creada = await CrearAsync(estudiante, kudo, "Gracias.");

        Assert.Equal("Carlos Mejía", creada.AutorNombre);
        Assert.Equal("Carlos Mejía", Assert.Single(await HiloAsync(kudo)).AutorNombre);
    }

    /// <summary>El contador de respuestas del muro cuenta todas, también las anidadas.</summary>
    [Fact]
    public async Task El_muro_cuenta_las_respuestas_de_todo_el_hilo()
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        var raiz = await CrearAsync(estudiante, kudo, "Uno.");
        var hija = await CrearAsync(estudiante, kudo, "Dos.", raiz.Id);
        await CrearAsync(estudiante, kudo, "Tres.", hija.Id);

        var muro = (await estudiante.GetFromJsonAsync<PaginaMuroDto>("/api/kudos", Json))!;

        Assert.Equal(3, muro.Elementos.Single(k => k.Id == kudo).TotalReplicas);
    }

    /// <summary>
    /// «Sin límite de profundidad»: el enunciado lo exige y el diseño lo respeta, así que una cadena
    /// de veinte respuestas se acepta entera y llega con la profundidad correcta.
    /// </summary>
    [Fact]
    public async Task No_hay_limite_de_profundidad()
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        Guid? padre = null;
        for (var i = 1; i <= 20; i++)
            padre = (await CrearAsync(estudiante, kudo, "Nivel " + i, padre)).Id;

        var hilo = await HiloAsync(kudo);

        Assert.Equal(20, hilo.Count);
        Assert.Equal(20, hilo[^1].Profundidad);
    }

    [Fact]
    public async Task No_hay_limite_de_longitud()
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        var largo = new string('a', 20_000);
        await CrearAsync(estudiante, kudo, largo);

        Assert.Equal(20_000, Assert.Single(await HiloAsync(kudo)).Contenido.Length);
    }

    [Fact]
    public async Task Responder_con_texto_vacio_se_rechaza()
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);

        Assert.Equal(HttpStatusCode.BadRequest, (await ResponderAsync(estudiante, kudo, "   ")).StatusCode);
        Assert.Empty(await HiloAsync(kudo));
    }

    [Fact]
    public async Task No_se_responde_a_un_kudo_ni_a_una_replica_que_no_existen_ni_a_la_de_otro_kudo()
    {
        var kudo = await PublicarKudoAsync();
        var otro = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        var deOtro = await CrearAsync(estudiante, otro, "En el otro Kudo.");

        Assert.Equal(HttpStatusCode.BadRequest, (await ResponderAsync(estudiante, Guid.NewGuid(), "x")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ResponderAsync(estudiante, kudo, "x", Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ResponderAsync(estudiante, kudo, "x", deOtro.Id)).StatusCode);
    }

    [Fact]
    public async Task No_se_responde_a_una_replica_vetada_ni_a_un_kudo_vetado()
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        var replica = await CrearAsync(estudiante, kudo, "Algo.");

        using var rector = Como(CorreoRector);
        await rector.PostAsJsonAsync("/api/moderacion/vetos", new VetarDto("Replica", replica.Id, "Ofensivo."));

        Assert.Equal(HttpStatusCode.BadRequest, (await ResponderAsync(estudiante, kudo, "x", replica.Id)).StatusCode);

        await rector.PostAsJsonAsync("/api/moderacion/vetos", new VetarDto("Kudo", kudo, "Ofensivo."));

        Assert.Equal(HttpStatusCode.BadRequest, (await ResponderAsync(estudiante, kudo, "x")).StatusCode);
    }

    [Fact]
    public async Task Sin_identidad_no_se_responde_ni_se_lee_el_hilo()
    {
        var kudo = await PublicarKudoAsync();
        using var anonimo = Como();

        Assert.Equal(HttpStatusCode.Unauthorized, (await ResponderAsync(anonimo, kudo, "x")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"/api/kudos/{kudo}/replicas")).StatusCode);
    }

    [Fact]
    public async Task Responder_avisa_al_destinatario_del_kudo_pero_no_si_es_el_quien_responde()
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        using var coordinador = Como(CorreoCoordinadorPrograma);

        // El destinatario del Kudo es el estudiante: sus propias respuestas no lo avisan a sí mismo.
        await CrearAsync(estudiante, kudo, "Gracias de nuevo.");
        await DespacharAsync();
        await using (var contexto = BaseDatos.CrearContexto())
        {
            Assert.Empty(await contexto.Avisos.Where(a => a.PersonaId == Datos.DestinatarioId && a.ClaveMensaje == "replica.en_tu_kudo").ToListAsync());
        }

        // Una persona distinta sí lo avisa.
        await CrearAsync(coordinador, kudo, "Felicitaciones.");
        await DespacharAsync();
        await using (var contexto = BaseDatos.CrearContexto())
        {
            var aviso = Assert.Single(await contexto.Avisos.Where(a => a.PersonaId == Datos.DestinatarioId && a.ClaveMensaje == "replica.en_tu_kudo").ToListAsync());
            Assert.Contains("Coordinador de Sistemas", aviso.Variables, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Responder_deja_rastro_en_la_auditoria_con_la_profundidad()
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        var raiz = await CrearAsync(estudiante, kudo, "Uno.");
        await CrearAsync(estudiante, kudo, "Dos.", raiz.Id);

        await using var contexto = BaseDatos.CrearContexto();
        var eventos = await contexto.EventosAuditoria
            .Where(e => e.Accion == AccionesAuditoria.ReplicaPublicar)
            .OrderBy(e => e.Id)
            .ToListAsync();

        Assert.Equal(2, eventos.Count);
        Assert.Contains("\"profundidad\":1", eventos[0].Datos, StringComparison.Ordinal);
        Assert.Contains("\"profundidad\":2", eventos[1].Datos, StringComparison.Ordinal);
    }
}
