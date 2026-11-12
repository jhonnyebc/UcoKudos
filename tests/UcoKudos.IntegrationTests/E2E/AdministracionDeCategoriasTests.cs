using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UcoKudos.Application.Kudos.Entrada.Dto;
using UcoKudos.Domain.Auditoria;
using UcoKudos.IntegrationTests.Infraestructura;

namespace UcoKudos.IntegrationTests.E2E;

/// <summary>
/// Administración de categorías (RF-06), por HTTP y con cada uno de los siete roles.
///
/// El enunciado lo dice en dos frases: *«Únicamente podrán administrar categorías: Administrador,
/// Rector, Coordinador Académico»* y *«los demás roles no podrán»*. Una regla escrita así se prueba
/// con **los siete roles**, no con uno que sí y uno que no: son los casos de en medio —un
/// Coordinador de Facultad, que parece tener autoridad y no la tiene— los que se rompen cuando
/// alguien cambia la jerarquía.
///
/// Hasta ahora la restricción estaba en el dominio y ninguna prueba la ejercitaba por su nombre.
/// </summary>
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Category", "E2E")]
public sealed class AdministracionDeCategoriasTests(BaseDeDatosFixture baseDatos)
    : EscenarioDeExtremoAExtremo(baseDatos)
{
    private static int _contador;

    /// <summary>Un nombre distinto en cada llamada: el nombre de una categoría es único.</summary>
    private static GuardarCategoriaDto Nueva(string prefijo = "Categoría de prueba") =>
        new($"{prefijo} {Interlocked.Increment(ref _contador)}", "Creada por una prueba.");

    [Theory]
    [InlineData(CorreoAdministrador)]
    [InlineData(CorreoRector)]
    [InlineData(CorreoCoordinadorAcademico)]
    public async Task Los_tres_roles_con_permiso_pueden_crear_categorias(string correo)
    {
        using var cliente = Como(correo);

        var respuesta = await cliente.PostAsJsonAsync("/api/categorias", Nueva());

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
    }

    [Theory]
    [InlineData(CorreoCoordinadorFacultad, "Coordinador de Facultad")]
    [InlineData(CorreoCoordinadorPrograma, "Coordinador de Programa")]
    [InlineData(CorreoDocente, "Docente")]
    [InlineData(CorreoEstudiante, "Estudiante")]
    public async Task Los_otros_cuatro_roles_no_pueden_y_el_motivo_nombra_su_rol(string correo, string rol)
    {
        using var cliente = Como(correo);

        var respuesta = await cliente.PostAsJsonAsync("/api/categorias", Nueva());

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);

        // El motivo llega íntegro, con el nombre del rol: es lo que la pantalla le enseña a la persona.
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Contains(rol, cuerpo.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// **Un acceso rechazado también queda auditado.** Es la mitad que se suele olvidar de la
    /// autorización: sin ella no habría forma de detectar a alguien que prueba permisos uno por uno.
    /// </summary>
    [Fact]
    public async Task Un_intento_rechazado_queda_registrado_en_la_auditoria()
    {
        using var docente = Como(CorreoDocente);

        var respuesta = await docente.PostAsJsonAsync("/api/categorias", Nueva());
        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);

        await using var contexto = BaseDatos.CrearContexto();
        var rechazo = await contexto.EventosAuditoria
            .Where(e => e.Accion == AccionesAuditoria.AccesoRechazado)
            .SingleAsync();

        Assert.Equal(Datos.AutorId, rechazo.ActorId);
        Assert.Contains("crear una categoría", rechazo.Datos, StringComparison.Ordinal);
        Assert.Contains("Docente", rechazo.Datos, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ver_todas_las_categorias_tambien_esta_restringido()
    {
        const string ruta = "/api/categorias/todas";

        using var docente = Como(CorreoDocente);
        using var rector = Como(CorreoRector);

        Assert.Equal(HttpStatusCode.Forbidden, (await docente.GetAsync(ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await rector.GetAsync(ruta)).StatusCode);
    }

    [Fact]
    public async Task Una_categoria_nueva_aparece_en_la_lista_de_publicar_aunque_haya_cache()
    {
        using var rector = Como(CorreoRector);
        using var docente = Como(CorreoDocente);

        // Se consulta primero para que la lista quede en caché: lo que se comprueba es que crear
        // la invalida, y con la caché vacía esa comprobación no probaría nada.
        var antes = await docente.GetFromJsonAsync<List<CategoriaDto>>("/api/categorias", Json);

        var datos = Nueva("Innovación Abierta");
        var creada = await (await rector.PostAsJsonAsync("/api/categorias", datos))
            .Content.ReadFromJsonAsync<CategoriaDto>(Json);

        var despues = await docente.GetFromJsonAsync<List<CategoriaDto>>("/api/categorias", Json);

        Assert.DoesNotContain(antes!, c => c.Id == creada!.Id);
        Assert.Contains(despues!, c => c.Id == creada!.Id && c.Nombre == datos.Nombre);
    }

    [Fact]
    public async Task El_nombre_de_una_categoria_no_se_repite()
    {
        using var rector = Como(CorreoRector);
        var datos = Nueva();

        Assert.Equal(HttpStatusCode.Created, (await rector.PostAsJsonAsync("/api/categorias", datos)).StatusCode);
        var repetida = await rector.PostAsJsonAsync("/api/categorias", datos);

        Assert.Equal(HttpStatusCode.BadRequest, repetida.StatusCode);
        using var cuerpo = JsonDocument.Parse(await repetida.Content.ReadAsStringAsync());
        Assert.Contains("Ya existe", cuerpo.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Renombrar_cambia_el_nombre_y_la_descripcion()
    {
        using var rector = Como(CorreoRector);
        var creada = await (await rector.PostAsJsonAsync("/api/categorias", Nueva()))
            .Content.ReadFromJsonAsync<CategoriaDto>(Json);

        var cambios = new GuardarCategoriaDto("Nombre nuevo", "Descripción nueva");
        var respuesta = await rector.PutAsJsonAsync($"/api/categorias/{creada!.Id}", cambios);

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);

        var todas = await rector.GetFromJsonAsync<List<CategoriaDto>>("/api/categorias/todas", Json);
        var actualizada = Assert.Single(todas!, c => c.Id == creada.Id);
        Assert.Equal("Nombre nuevo", actualizada.Nombre);
        Assert.Equal("Descripción nueva", actualizada.Descripcion);
    }

    /// <summary>
    /// **Se desactiva, no se borra, y esta es la razón.** Los Kudos ya publicados conservan su
    /// categoría: borrarla dejaría reconocimientos sin clasificar. Desactivada solo deja de ofrecerse
    /// para publicar.
    /// </summary>
    [Fact]
    public async Task Desactivar_una_categoria_la_retira_de_publicar_pero_no_de_los_kudos_ya_hechos()
    {
        using var rector = Como(CorreoRector);
        using var docente = Como(CorreoDocente);
        using var estudiante = Como(CorreoEstudiante);

        var creada = await (await rector.PostAsJsonAsync("/api/categorias", Nueva("Mentoría")))
            .Content.ReadFromJsonAsync<CategoriaDto>(Json);

        // Un Kudo publicado con esa categoría, antes de desactivarla.
        var publicado = await docente.PostAsJsonAsync(
            "/api/kudos",
            new PublicarKudoDto(Datos.DestinatarioId, creada!.Id, "Gracias por la mentoría."));
        Assert.True(publicado.IsSuccessStatusCode, await publicado.Content.ReadAsStringAsync());

        var desactivada = await rector.PostAsync($"/api/categorias/{creada.Id}/estado?activa=false", null);
        Assert.Equal(HttpStatusCode.NoContent, desactivada.StatusCode);

        // Ya no se ofrece para publicar...
        var activas = await docente.GetFromJsonAsync<List<CategoriaDto>>("/api/categorias", Json);
        Assert.DoesNotContain(activas!, c => c.Id == creada.Id);

        // ...pero sigue en la vista de administración, marcada como inactiva...
        var todas = await rector.GetFromJsonAsync<List<CategoriaDto>>("/api/categorias/todas", Json);
        Assert.False(Assert.Single(todas!, c => c.Id == creada.Id).Activa);

        // ...y el Kudo que ya existía conserva su categoría.
        var muro = await estudiante.GetFromJsonAsync<PaginaMuroDto>("/api/kudos", Json);
        Assert.Equal(creada.Nombre, Assert.Single(muro!.Elementos).CategoriaNombre);
    }

    [Fact]
    public async Task Una_categoria_desactivada_se_puede_volver_a_activar()
    {
        using var rector = Como(CorreoRector);
        var creada = await (await rector.PostAsJsonAsync("/api/categorias", Nueva()))
            .Content.ReadFromJsonAsync<CategoriaDto>(Json);

        await rector.PostAsync($"/api/categorias/{creada!.Id}/estado?activa=false", null);
        await rector.PostAsync($"/api/categorias/{creada.Id}/estado?activa=true", null);

        var activas = await rector.GetFromJsonAsync<List<CategoriaDto>>("/api/categorias", Json);
        Assert.Contains(activas!, c => c.Id == creada.Id);
    }

    [Fact]
    public async Task Las_quince_categorias_del_enunciado_estan_sembradas()
    {
        using var estudiante = Como(CorreoEstudiante);

        var activas = await estudiante.GetFromJsonAsync<List<CategoriaDto>>("/api/categorias", Json);

        // El enunciado pide «como mínimo» estas quince. Se comprueban por nombre y no por cantidad:
        // una categoría añadida no debe romper la prueba, y una que falte, sí.
        string[] exigidas =
        [
            "Rendimiento Académico", "Mejor Investigador", "Empatía", "Comunicación Asertiva",
            "Orientación al Logro", "Trabajo en Equipo", "Trabajo Bajo Presión", "Visión Estratégica",
            "Innovación", "Liderazgo", "Transformación Digital", "Mentor Inspirador", "Espíritu UCO",
            "Servicio a la Comunidad", "Compromiso Institucional"
        ];

        var nombres = activas!.Select(c => c.Nombre).ToHashSet();
        Assert.All(exigidas, nombre => Assert.Contains(nombre, nombres));
    }
}
