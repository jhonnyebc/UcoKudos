using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UcoKudos.Application.Interaccion.Entrada.Dto;
using UcoKudos.Application.Kudos.Entrada.Dto;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Infrastructure.Notificaciones;
using UcoKudos.IntegrationTests.Infraestructura;

namespace UcoKudos.IntegrationTests.E2E;

/// <summary>
/// Reportar y vetar (RF-11, RF-12), por HTTP y de punta a punta.
///
/// Lo que se defiende, y lo que importa de un veto:
///
///  - Solo el Rector y el Coordinador Académico vetan. Los otros cinco roles, **incluido el
///    Administrador**, reciben un 403 y el intento queda auditado.
///  - Vetar **oculta de verdad**: el muro, el hilo y el detalle dejan de servirlo. Un veto que solo
///    cambia un indicador en la base sin que las consultas lo respeten no vetaría nada.
///  - Vetar un Kudo oculta todo su hilo, y vetar una réplica oculta sus descendientes **y solo
///    ellos**: ni a sus hermanas ni al Kudo.
///  - El autor se entera, con el motivo, y el veto queda en la auditoría con quién lo hizo.
/// </summary>
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Category", "E2E")]
public sealed class ModeracionDeExtremoAExtremoTests(BaseDeDatosFixture baseDatos)
    : EscenarioDeExtremoAExtremo(baseDatos)
{
    private async Task<Guid> PublicarKudoAsync(string texto = "Gracias por el informe.")
    {
        using var autor = Como(CorreoDocente);
        var respuesta = await autor.PostAsJsonAsync(
            "/api/kudos", new PublicarKudoDto(Datos.DestinatarioId, Datos.CategoriaId, texto));
        Assert.True(respuesta.IsSuccessStatusCode, await respuesta.Content.ReadAsStringAsync());

        return (await respuesta.Content.ReadFromJsonAsync<KudoPublicadoDto>(Json))!.Id;
    }

    private async Task<Guid> ResponderAsync(string correo, Guid kudoId, string texto, Guid? padre = null)
    {
        using var cliente = Como(correo);
        var respuesta = await cliente.PostAsJsonAsync($"/api/kudos/{kudoId}/replicas", new ResponderDto(texto, padre));
        Assert.True(respuesta.IsSuccessStatusCode, await respuesta.Content.ReadAsStringAsync());

        return (await respuesta.Content.ReadFromJsonAsync<ReplicaDto>(Json))!.Id;
    }

    private static Task<HttpResponseMessage> VetarAsync(HttpClient cliente, string tipo, Guid id, string motivo = "Contenido inapropiado.") =>
        cliente.PostAsJsonAsync("/api/moderacion/vetos", new VetarDto(tipo, id, motivo));

    private async Task<List<Guid>> HiloAsync(Guid kudoId)
    {
        using var cliente = Como(CorreoEstudiante);
        return (await cliente.GetFromJsonAsync<List<ReplicaDto>>($"/api/kudos/{kudoId}/replicas", Json))!
            .Select(r => r.Id).ToList();
    }

    private async Task<List<Guid>> MuroAsync()
    {
        using var cliente = Como(CorreoEstudiante);
        return (await cliente.GetFromJsonAsync<PaginaMuroDto>("/api/kudos", Json))!
            .Elementos.Select(k => k.Id).ToList();
    }

    /// <summary>
    /// Un Kudo con un hilo de tres réplicas anidadas:
    ///
    ///   Kudo
    ///    └─ réplica 1 (estudiante)
    ///        └─ réplica 2 (docente)
    ///            └─ réplica 3 (estudiante)
    /// </summary>
    private async Task<(Guid Kudo, Guid R1, Guid R2, Guid R3)> KudoConHiloAsync()
    {
        var kudo = await PublicarKudoAsync();
        var r1 = await ResponderAsync(CorreoEstudiante, kudo, "Primera respuesta.");
        var r2 = await ResponderAsync(CorreoDocente, kudo, "Respuesta a la primera.", r1);
        var r3 = await ResponderAsync(CorreoEstudiante, kudo, "Respuesta a la respuesta.", r2);

        return (kudo, r1, r2, r3);
    }

    [Fact]
    public async Task Vetar_un_kudo_lo_oculta_junto_con_todo_su_hilo_y_deja_rastro()
    {
        var (kudo, _, _, _) = await KudoConHiloAsync();
        var otro = await PublicarKudoAsync("Otro reconocimiento que no se toca.");

        Assert.Contains(kudo, await MuroAsync());

        using var rector = Como(CorreoRector);
        var respuesta = await VetarAsync(rector, "Kudo", kudo, "Lenguaje ofensivo.");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var resultado = (await respuesta.Content.ReadFromJsonAsync<ResultadoVetoDto>(Json))!;

        // El Kudo y sus tres réplicas.
        Assert.Equal(4, resultado.ContenidosAfectados);
        Assert.True(resultado.EnCascada);

        // Se oculta de verdad: ni el muro ni el hilo lo sirven, y el otro Kudo sigue ahí.
        var muro = await MuroAsync();
        Assert.DoesNotContain(kudo, muro);
        Assert.Contains(otro, muro);
        Assert.Empty(await HiloAsync(kudo));

        // Tampoco se puede seguir respondiendo a un Kudo retirado.
        using var estudiante = Como(CorreoEstudiante);
        var tardia = await estudiante.PostAsJsonAsync($"/api/kudos/{kudo}/replicas", new ResponderDto("¿Sigue ahí?", null));
        Assert.Equal(HttpStatusCode.BadRequest, tardia.StatusCode);

        // El autor se entera, con el motivo.
        await DespacharAsync();
        var correo = Web.Services.GetRequiredService<EnviadorCorreoSimulado>();
        var aviso = correo.Enviados.Last(e => e.Destino == CorreoDocente);
        Assert.Contains("Lenguaje ofensivo.", aviso.Texto, StringComparison.Ordinal);

        // Y queda en la auditoría, con quién lo hizo.
        await using var contexto = BaseDatos.CrearContexto();
        var evento = await contexto.EventosAuditoria.SingleAsync(e => e.Accion == AccionesAuditoria.ContenidoVetar);
        Assert.Equal(kudo, evento.EntidadId);
        Assert.Equal(await contexto.Personas.Where(p => p.CorreoInstitucional == CorreoRector).Select(p => p.Id).SingleAsync(), evento.ActorId);
    }

    [Fact]
    public async Task El_detalle_de_un_kudo_vetado_deja_de_servirse()
    {
        var kudo = await PublicarKudoAsync();

        using var coordinador = Como(CorreoCoordinadorAcademico);
        Assert.Equal(HttpStatusCode.OK, (await VetarAsync(coordinador, "Kudo", kudo)).StatusCode);

        using var estudiante = Como(CorreoEstudiante);
        var detalle = await estudiante.GetAsync($"/api/kudos/{kudo}");

        Assert.NotEqual(HttpStatusCode.OK, detalle.StatusCode);
    }

    [Fact]
    public async Task Vetar_una_replica_oculta_a_sus_descendientes_y_solo_a_ellos()
    {
        var kudo = await PublicarKudoAsync();
        var r1 = await ResponderAsync(CorreoEstudiante, kudo, "Primera respuesta.");
        var hermana = await ResponderAsync(CorreoDocente, kudo, "Otra respuesta, hermana de la primera.");
        var r2 = await ResponderAsync(CorreoDocente, kudo, "Respuesta a la primera.", r1);
        var r3 = await ResponderAsync(CorreoEstudiante, kudo, "Respuesta a la respuesta.", r2);

        using var rector = Como(CorreoRector);
        var respuesta = await VetarAsync(rector, "Replica", r1);
        var resultado = (await respuesta.Content.ReadFromJsonAsync<ResultadoVetoDto>(Json))!;

        // La réplica vetada y sus dos descendientes.
        Assert.Equal(3, resultado.ContenidosAfectados);
        Assert.True(resultado.EnCascada);

        // Queda visible la hermana, y el Kudo sigue en el muro.
        Assert.Equal([hermana], await HiloAsync(kudo));
        Assert.Contains(kudo, await MuroAsync());
        Assert.DoesNotContain(r1, await HiloAsync(kudo));
        Assert.DoesNotContain(r3, await HiloAsync(kudo));
    }

    /// <summary>
    /// La regla, rol por rol. Solo el Rector y el Coordinador Académico; el Administrador **no**, por
    /// alto que sea su rol: moderar es una atribución académica, no técnica.
    /// </summary>
    [Theory]
    [InlineData(CorreoRector, true)]
    [InlineData(CorreoCoordinadorAcademico, true)]
    [InlineData(CorreoAdministrador, false)]
    [InlineData(CorreoCoordinadorFacultad, false)]
    [InlineData(CorreoCoordinadorPrograma, false)]
    [InlineData(CorreoDocente, false)]
    [InlineData(CorreoEstudiante, false)]
    public async Task Solo_el_rector_y_el_coordinador_academico_pueden_vetar(string correo, bool puede)
    {
        var kudo = await PublicarKudoAsync();

        using var cliente = Como(correo);
        var respuesta = await VetarAsync(cliente, "Kudo", kudo);

        Assert.Equal(puede ? HttpStatusCode.OK : HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Equal(puede, !(await MuroAsync()).Contains(kudo));

        if (!puede)
        {
            // El intento queda auditado: un intento fallido informa tanto como uno que funciona.
            await using var contexto = BaseDatos.CrearContexto();
            Assert.Contains(AccionesAuditoria.AccesoRechazado,
                await contexto.EventosAuditoria.Select(e => e.Accion).ToListAsync());
        }
    }

    [Fact]
    public async Task Cualquier_usuario_puede_reportar_y_no_se_oculta_nada_por_reportar()
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        var reporte = await estudiante.PostAsJsonAsync(
            "/api/moderacion/reportes", new ReportarDto("Kudo", kudo, "Me parece ofensivo."));

        Assert.Equal(HttpStatusCode.Accepted, reporte.StatusCode);

        // Reportar avisa a quien modera; vetar es otra decisión, y el contenido sigue visible.
        Assert.Contains(kudo, await MuroAsync());
    }

    [Fact]
    public async Task Vetar_algo_que_no_existe_se_rechaza_sin_romper_nada()
    {
        using var rector = Como(CorreoRector);

        Assert.Equal(HttpStatusCode.BadRequest, (await VetarAsync(rector, "Kudo", Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await VetarAsync(rector, "Replica", Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Sin_identidad_no_se_puede_reportar_ni_vetar()
    {
        var kudo = await PublicarKudoAsync();
        using var anonimo = Como();

        Assert.Equal(HttpStatusCode.Unauthorized, (await VetarAsync(anonimo, "Kudo", kudo)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonimo.PostAsJsonAsync("/api/moderacion/reportes", new ReportarDto("Kudo", kudo, "x"))).StatusCode);
    }

    [Fact]
    public async Task Vetar_dos_veces_lo_mismo_no_es_un_error_del_servidor()
    {
        var kudo = await PublicarKudoAsync();

        using var rector = Como(CorreoRector);
        Assert.Equal(HttpStatusCode.OK, (await VetarAsync(rector, "Kudo", kudo)).StatusCode);

        var segundo = await VetarAsync(rector, "Kudo", kudo);

        Assert.True((int)segundo.StatusCode < 500, "Un segundo veto no debe terminar en un error del servidor.");
    }

    private static async Task ReportarAsync(HttpClient cliente, string tipo, Guid id, string motivo) =>
        Assert.Equal(
            HttpStatusCode.Accepted,
            (await cliente.PostAsJsonAsync("/api/moderacion/reportes", new ReportarDto(tipo, id, motivo))).StatusCode);

    private static async Task<List<ReportePendienteDto>> PendientesAsync(HttpClient cliente) =>
        (await cliente.GetFromJsonAsync<List<ReportePendienteDto>>("/api/moderacion/reportes", Json))!;

    [Fact]
    public async Task Quien_modera_ve_los_reportes_pendientes_con_el_contenido_reportado()
    {
        var kudo = await PublicarKudoAsync("Un mensaje que alguien va a reportar.");

        using var estudiante = Como(CorreoEstudiante);
        await ReportarAsync(estudiante, "Kudo", kudo, "Me parece ofensivo.");

        using var rector = Como(CorreoRector);
        var reporte = Assert.Single(await PendientesAsync(rector));

        Assert.Equal(kudo, reporte.ContenidoId);
        Assert.Equal("Kudo", reporte.TipoContenido);
        Assert.Equal("Me parece ofensivo.", reporte.Motivo);
        Assert.Equal("Un mensaje que alguien va a reportar.", reporte.Contenido);
        Assert.Equal("Carlos Mejía", reporte.ReportadoPor);
        Assert.Equal("Ana Gómez", reporte.AutorDelContenido);
    }

    /// <summary>Los reportes son para quien decide: el resto de roles no los ve, y el intento queda auditado.</summary>
    [Theory]
    [InlineData(CorreoAdministrador)]
    [InlineData(CorreoCoordinadorPrograma)]
    [InlineData(CorreoDocente)]
    [InlineData(CorreoEstudiante)]
    public async Task Los_roles_que_no_moderan_no_ven_los_reportes(string correo)
    {
        using var cliente = Como(correo);

        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/moderacion/reportes")).StatusCode);

        await using var contexto = BaseDatos.CrearContexto();
        Assert.Contains(AccionesAuditoria.AccesoRechazado,
            await contexto.EventosAuditoria.Select(e => e.Accion).ToListAsync());
    }

    [Fact]
    public async Task Vetar_un_contenido_resuelve_sus_reportes_pendientes()
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        await ReportarAsync(estudiante, "Kudo", kudo, "Ofensivo.");

        using var coordinador = Como(CorreoCoordinadorAcademico);
        Assert.Single(await PendientesAsync(coordinador));

        Assert.Equal(HttpStatusCode.OK, (await VetarAsync(coordinador, "Kudo", kudo)).StatusCode);

        // Ya se decidió: no sigue pendiente, y quedó aceptado en la base.
        Assert.Empty(await PendientesAsync(coordinador));

        await using var contexto = BaseDatos.CrearContexto();
        Assert.Equal(UcoKudos.Domain.Interaccion.EstadoReporte.Aceptado,
            (await contexto.Reportes.SingleAsync()).Estado);
    }

    [Fact]
    public async Task Descartar_un_reporte_lo_resuelve_sin_vetar_el_contenido()
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        await ReportarAsync(estudiante, "Kudo", kudo, "Creo que no corresponde.");

        using var rector = Como(CorreoRector);
        var reporte = Assert.Single(await PendientesAsync(rector));

        var descarte = await rector.PostAsync($"/api/moderacion/reportes/{reporte.Id}/descartar", null);
        Assert.Equal(HttpStatusCode.NoContent, descarte.StatusCode);

        Assert.Empty(await PendientesAsync(rector));
        Assert.Contains(kudo, await MuroAsync());

        await using var contexto = BaseDatos.CrearContexto();
        Assert.Equal(UcoKudos.Domain.Interaccion.EstadoReporte.Descartado,
            (await contexto.Reportes.SingleAsync()).Estado);
        Assert.Contains(AccionesAuditoria.ReporteDescartar,
            await contexto.EventosAuditoria.Select(e => e.Accion).ToListAsync());
    }

    [Fact]
    public async Task Quien_no_modera_no_puede_descartar_un_reporte()
    {
        var kudo = await PublicarKudoAsync();

        using var estudiante = Como(CorreoEstudiante);
        await ReportarAsync(estudiante, "Kudo", kudo, "Ofensivo.");

        await using var contexto = BaseDatos.CrearContexto();
        var reporteId = (await contexto.Reportes.SingleAsync()).Id;

        var intento = await estudiante.PostAsync($"/api/moderacion/reportes/{reporteId}/descartar", null);

        Assert.Equal(HttpStatusCode.Forbidden, intento.StatusCode);
    }

    [Fact]
    public async Task Un_reporte_de_una_replica_ya_oculta_por_cascada_no_ocupa_la_bandeja()
    {
        var (kudo, _, r2, _) = await KudoConHiloAsync();

        using var docente = Como(CorreoDocente);
        await ReportarAsync(docente, "Replica", r2, "Fuera de lugar.");

        using var rector = Como(CorreoRector);
        Assert.Single(await PendientesAsync(rector));

        // Al vetar el Kudo entero, la réplica queda oculta y su reporte ya no necesita decisión.
        await VetarAsync(rector, "Kudo", kudo);

        Assert.Empty(await PendientesAsync(rector));
    }

    private static async Task<string> DetalleAsync(HttpResponseMessage respuesta)
    {
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        return cuerpo.RootElement.GetProperty("detail").GetString()!;
    }
}
