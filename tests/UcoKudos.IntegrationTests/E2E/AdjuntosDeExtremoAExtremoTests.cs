using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UcoKudos.Application.Kudos.Entrada.Dto;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Infrastructure.Objetos;
using UcoKudos.IntegrationTests.Infraestructura;

namespace UcoKudos.IntegrationTests.E2E;

/// <summary>
/// Adjuntos (RF-08) por HTTP, recorriendo el flujo entero como lo haría el navegador: pedir permiso,
/// subir el archivo y confirmar.
///
/// **Existe porque el primer uso real falló, y ninguna prueba lo había visto.** Al adjuntar un
/// archivo desde un celular, la API respondía «otra operación modificó este contenido al mismo
/// tiempo», una excepción de concurrencia, sin que nadie estuviera tocando nada. La causa era de
/// mapeo: el identificador del adjunto lo asigna el dominio, y EF Core, al ver una entidad nueva
/// con la clave ya rellena dentro de la colección de un agregado rastreado, la da por existente y
/// emite un UPDATE en lugar de un INSERT. Cero filas afectadas, y lo traduce como conflicto.
///
/// Las pruebas de integración anteriores cubrían el almacenamiento y los casos de uso por separado,
/// pero ninguna publicaba un Kudo y luego le añadía un adjunto **cargado de la base**, que es el
/// camino de verdad.
///
/// El navegador se simula con el almacenamiento en memoria: colocar el objeto equivale al PUT que
/// haría el cliente contra la URL prefirmada.
/// </summary>
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Category", "E2E")]
public sealed class AdjuntosDeExtremoAExtremoTests(BaseDeDatosFixture baseDatos)
    : EscenarioDeExtremoAExtremo(baseDatos)
{
    /// <summary>Los primeros bytes de un PNG: es lo que el servidor lee para saber qué tipo es.</summary>
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private async Task<Guid> PublicarUnKudoAsync()
    {
        using var autor = Como(CorreoDocente);

        var respuesta = await autor.PostAsJsonAsync(
            "/api/kudos",
            new PublicarKudoDto(Datos.DestinatarioId, Datos.CategoriaId, "Gracias por el informe."));

        Assert.True(respuesta.IsSuccessStatusCode, await respuesta.Content.ReadAsStringAsync());

        return (await respuesta.Content.ReadFromJsonAsync<KudoPublicadoDto>(Json))!.Id;
    }

    private static IntencionAdjuntoDto Intencion(string nombre = "foto.png", string tipo = "image/png", long tamano = 12) =>
        new(nombre, tipo, tamano);

    /// <summary>Pide permiso y devuelve la clave del objeto, como la ve el almacenamiento.</summary>
    private static async Task<(SubidaAdjuntoDto Permiso, string Clave)> PedirPermisoAsync(
        HttpClient cliente, Guid kudoId, IntencionAdjuntoDto datos)
    {
        var respuesta = await cliente.PostAsJsonAsync($"/api/kudos/{kudoId}/adjuntos/intencion", datos);
        Assert.True(respuesta.IsSuccessStatusCode, await respuesta.Content.ReadAsStringAsync());

        var permiso = (await respuesta.Content.ReadFromJsonAsync<SubidaAdjuntoDto>(Json))!;

        // «memoria://subida/<clave>?firma=...» es la URL que el almacenamiento simulado entrega.
        var clave = permiso.UrlDeSubida["memoria://subida/".Length..].Split('?')[0];

        return (permiso, clave);
    }

    /// <summary>El PUT que haría el navegador contra la URL prefirmada.</summary>
    private void SubirComoElNavegador(string clave, byte[] contenido) =>
        Web.Services.GetRequiredService<AlmacenObjetosSimulado>().Colocar(clave, contenido);

    [Fact]
    public async Task Un_adjunto_recorre_el_flujo_completo_y_queda_visible_y_auditado()
    {
        var kudoId = await PublicarUnKudoAsync();
        using var autor = Como(CorreoDocente);
        using var destinatario = Como(CorreoEstudiante);

        // Paso 1: pedir permiso. Aquí fallaba en producción con un conflicto de concurrencia.
        var (permiso, clave) = await PedirPermisoAsync(autor, kudoId, Intencion());

        // Paso 2: subir el archivo directo al almacenamiento (simulado).
        SubirComoElNavegador(clave, Png);

        // Paso 3: confirmar. El servidor inspecciona el contenido real.
        var confirmada = await autor.PostAsync($"/api/kudos/{kudoId}/adjuntos/{permiso.AdjuntoId}/confirmar", null);
        Assert.Equal(HttpStatusCode.NoContent, confirmada.StatusCode);

        // El destinatario ve el adjunto en el detalle, con el tipo REAL detectado.
        var detalle = await destinatario.GetFromJsonAsync<DetalleKudoDto>($"/api/kudos/{kudoId}", Json);
        var adjunto = Assert.Single(detalle!.Adjuntos);
        Assert.Equal("foto.png", adjunto.NombreOriginal);
        Assert.Equal("image/png", adjunto.TipoDetectado);
        Assert.Equal(Png.Length, adjunto.TamanoBytes);

        // El muro cuenta el adjunto.
        var muro = await destinatario.GetFromJsonAsync<PaginaMuroDto>("/api/kudos", Json);
        Assert.Equal(1, Assert.Single(muro!.Elementos).TotalAdjuntos);

        // Descargar devuelve un enlace, y la descarga queda auditada.
        var descarga = await destinatario.GetFromJsonAsync<JsonElement>(
            $"/api/kudos/{kudoId}/adjuntos/{adjunto.Id}/descarga", Json);
        Assert.StartsWith("memoria://descarga/", descarga.GetProperty("url").GetString(), StringComparison.Ordinal);

        await using var contexto = BaseDatos.CrearContexto();
        var acciones = await contexto.EventosAuditoria.Select(e => e.Accion).ToListAsync();
        Assert.Contains(AccionesAuditoria.AdjuntoCargar, acciones);
        Assert.Contains(AccionesAuditoria.AdjuntoDescargar, acciones);
    }

    /// <summary>
    /// Que el adjunto se pueda guardar no basta: tiene que **seguir ahí** al leer el Kudo de la base
    /// otra vez. Un UPDATE que no afecta filas fallaba, pero un INSERT mal hecho podría pasar en
    /// silencio y perderse.
    /// </summary>
    [Fact]
    public async Task Los_adjuntos_de_un_kudo_se_acumulan_hasta_el_maximo()
    {
        var kudoId = await PublicarUnKudoAsync();
        using var autor = Como(CorreoDocente);

        // Cinco, uno a continuación del otro, cada uno sobre el Kudo recargado de la base.
        for (var i = 1; i <= 5; i++)
        {
            var (permiso, clave) = await PedirPermisoAsync(autor, kudoId, Intencion($"foto{i}.png"));
            SubirComoElNavegador(clave, Png);

            var confirmada = await autor.PostAsync($"/api/kudos/{kudoId}/adjuntos/{permiso.AdjuntoId}/confirmar", null);
            Assert.Equal(HttpStatusCode.NoContent, confirmada.StatusCode);
        }

        var detalle = await autor.GetFromJsonAsync<DetalleKudoDto>($"/api/kudos/{kudoId}", Json);
        Assert.Equal(5, detalle!.Adjuntos.Count);

        // El sexto se rechaza con el motivo del límite.
        var sexto = await autor.PostAsJsonAsync($"/api/kudos/{kudoId}/adjuntos/intencion", Intencion("sexta.png"));

        Assert.Equal(HttpStatusCode.BadRequest, sexto.StatusCode);
        using var cuerpo = JsonDocument.Parse(await sexto.Content.ReadAsStringAsync());
        Assert.Contains("5", cuerpo.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Solo_el_autor_del_kudo_puede_adjuntarle_archivos()
    {
        var kudoId = await PublicarUnKudoAsync();
        using var destinatario = Como(CorreoEstudiante);

        var respuesta = await destinatario.PostAsJsonAsync($"/api/kudos/{kudoId}/adjuntos/intencion", Intencion());

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    /// <summary>
    /// **El tipo se decide por el contenido y no por lo que declara quien sube.** Un ejecutable
    /// renombrado a .pdf declara «application/pdf» y se rechaza al confirmar.
    /// </summary>
    [Fact]
    public async Task Un_archivo_cuyo_contenido_no_es_del_tipo_declarado_se_rechaza_al_confirmar()
    {
        var kudoId = await PublicarUnKudoAsync();
        using var autor = Como(CorreoDocente);

        var (permiso, clave) = await PedirPermisoAsync(autor, kudoId, Intencion("falso.pdf", "application/pdf"));

        // Bytes de un ejecutable de Windows («MZ»), no de un PDF.
        SubirComoElNavegador(clave, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);

        var respuesta = await autor.PostAsync($"/api/kudos/{kudoId}/adjuntos/{permiso.AdjuntoId}/confirmar", null);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Contains("no está permitido", cuerpo.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("programa.exe", "application/x-msdownload", 100)]
    [InlineData("documento.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 100)]
    [InlineData("enorme.pdf", "application/pdf", 5 * 1024 * 1024 + 1)]
    public async Task Se_rechaza_pronto_un_tipo_no_permitido_o_un_tamano_excesivo(string nombre, string tipo, long tamano)
    {
        var kudoId = await PublicarUnKudoAsync();
        using var autor = Como(CorreoDocente);

        var respuesta = await autor.PostAsJsonAsync(
            $"/api/kudos/{kudoId}/adjuntos/intencion", Intencion(nombre, tipo, tamano));

        // Antes de hacer subir nada: es lo que evita que alguien espere a subir cinco megas para
        // enterarse de que el archivo no servía.
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task Un_adjunto_sin_confirmar_no_se_puede_descargar()
    {
        var kudoId = await PublicarUnKudoAsync();
        using var autor = Como(CorreoDocente);
        using var destinatario = Como(CorreoEstudiante);

        // Se pide permiso pero nunca se confirma: el archivo no pasó la inspección.
        var (permiso, _) = await PedirPermisoAsync(autor, kudoId, Intencion());

        var respuesta = await destinatario.GetAsync($"/api/kudos/{kudoId}/adjuntos/{permiso.AdjuntoId}/descarga");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task Sin_identidad_no_se_puede_pedir_permiso_para_adjuntar()
    {
        var kudoId = await PublicarUnKudoAsync();
        using var anonimo = Como();

        var respuesta = await anonimo.PostAsJsonAsync($"/api/kudos/{kudoId}/adjuntos/intencion", Intencion());

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }
}
