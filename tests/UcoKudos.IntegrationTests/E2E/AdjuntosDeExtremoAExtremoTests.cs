using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UcoKudos.Application.Kudos.Entrada.Dto;
using UcoKudos.Domain.Auditoria;
using UcoKudos.IntegrationTests.Infraestructura;

namespace UcoKudos.IntegrationTests.E2E;

/// <summary>
/// Adjuntos (RF-08) por HTTP, recorriendo el flujo entero como lo hace el navegador: reservar el
/// adjunto, subir su contenido a la API y confirmar; y después descargarlo.
///
/// **Esta clase existe porque los adjuntos fallaron dos veces en el primer uso real, y ninguna prueba
/// lo había visto.**
///
/// La primera, un 409 al reservar: EF Core daba por existente un adjunto con clave asignada por el
/// dominio y emitía un UPDATE en lugar de un INSERT. Ninguna prueba añadía un adjunto a un Kudo
/// cargado de la base.
///
/// La segunda, un 403 al subir: el almacenamiento rechazaba todas las URL prefirmadas porque las
/// validaba con un secreto fijo y público, y las pruebas firmaban justo con ese secreto. De ahí que
/// los archivos pasen ahora por la API en las dos direcciones, con la sesión de quien sube o descarga,
/// y que el almacenamiento no tenga ninguna ruta pública.
///
/// Que el almacenamiento sea el simulado es deliberado: lo que se comprueba aquí es la API —quién
/// puede subir, qué se acepta, qué se sirve y qué se audita—. El adaptador real tiene sus propias
/// pruebas contra un servidor de objetos de verdad.
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

    /// <summary>Reserva el adjunto y devuelve su identificador.</summary>
    private static async Task<Guid> ReservarAsync(HttpClient cliente, Guid kudoId, IntencionAdjuntoDto datos)
    {
        var respuesta = await cliente.PostAsJsonAsync($"/api/kudos/{kudoId}/adjuntos/intencion", datos);
        Assert.True(respuesta.IsSuccessStatusCode, await respuesta.Content.ReadAsStringAsync());

        return (await respuesta.Content.ReadFromJsonAsync<SubidaAdjuntoDto>(Json))!.AdjuntoId;
    }

    /// <summary>El PUT del contenido, tal cual lo hace el cliente: el archivo es el cuerpo.</summary>
    private static Task<HttpResponseMessage> SubirContenidoAsync(
        HttpClient cliente, Guid kudoId, Guid adjuntoId, byte[] contenido) =>
        cliente.PutAsync($"/api/kudos/{kudoId}/adjuntos/{adjuntoId}/contenido", new ByteArrayContent(contenido));

    private static Task<HttpResponseMessage> ConfirmarAsync(HttpClient cliente, Guid kudoId, Guid adjuntoId) =>
        cliente.PostAsync($"/api/kudos/{kudoId}/adjuntos/{adjuntoId}/confirmar", null);

    /// <summary>Los tres pasos seguidos. Devuelve el identificador del adjunto confirmado.</summary>
    private static async Task<Guid> AdjuntarAsync(HttpClient autor, Guid kudoId, byte[] contenido, string nombre = "foto.png")
    {
        var adjuntoId = await ReservarAsync(autor, kudoId, Intencion(nombre));

        var subida = await SubirContenidoAsync(autor, kudoId, adjuntoId, contenido);
        Assert.Equal(HttpStatusCode.NoContent, subida.StatusCode);

        var confirmada = await ConfirmarAsync(autor, kudoId, adjuntoId);
        Assert.Equal(HttpStatusCode.NoContent, confirmada.StatusCode);

        return adjuntoId;
    }

    private static async Task<string> DetalleDelMensajeAsync(HttpResponseMessage respuesta)
    {
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        return cuerpo.RootElement.GetProperty("detail").GetString()!;
    }

    [Fact]
    public async Task Un_adjunto_recorre_el_flujo_completo_y_se_descarga_igual_que_se_subio()
    {
        var kudoId = await PublicarUnKudoAsync();
        using var autor = Como(CorreoDocente);
        using var destinatario = Como(CorreoEstudiante);

        // Los tres pasos. El primero fallaba en producción con un conflicto de concurrencia, y el
        // segundo con un 403 del almacenamiento cuando las URL eran prefirmadas.
        var adjuntoId = await AdjuntarAsync(autor, kudoId, Png);

        // El destinatario ve el adjunto en el detalle, con el tipo REAL detectado.
        var detalle = await destinatario.GetFromJsonAsync<DetalleKudoDto>($"/api/kudos/{kudoId}", Json);
        var adjunto = Assert.Single(detalle!.Adjuntos);
        Assert.Equal("foto.png", adjunto.NombreOriginal);
        Assert.Equal("image/png", adjunto.TipoDetectado);
        Assert.Equal(Png.Length, adjunto.TamanoBytes);

        // El muro cuenta el adjunto.
        var muro = await destinatario.GetFromJsonAsync<PaginaMuroDto>("/api/kudos", Json);
        Assert.Equal(1, Assert.Single(muro!.Elementos).TotalAdjuntos);

        // Descargar entrega EXACTAMENTE los bytes que se subieron, con el tipo detectado.
        var descarga = await destinatario.GetAsync($"/api/kudos/{kudoId}/adjuntos/{adjuntoId}/archivo");
        Assert.Equal(HttpStatusCode.OK, descarga.StatusCode);
        Assert.Equal(Png, await descarga.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/png", descarga.Content.Headers.ContentType?.MediaType);

        // Quién ve qué: la subida y la descarga quedan auditadas, cada una con su actor.
        await using var contexto = BaseDatos.CrearContexto();
        var eventos = await contexto.EventosAuditoria
            .Where(e => e.Accion == AccionesAuditoria.AdjuntoCargar || e.Accion == AccionesAuditoria.AdjuntoDescargar)
            .ToListAsync();

        Assert.Equal(Datos.AutorId, Assert.Single(eventos, e => e.Accion == AccionesAuditoria.AdjuntoCargar).ActorId);
        Assert.Equal(Datos.DestinatarioId, Assert.Single(eventos, e => e.Accion == AccionesAuditoria.AdjuntoDescargar).ActorId);
    }

    /// <summary>
    /// Servir un archivo a un navegador tiene su riesgo propio, y las cabeceras lo acotan: el
    /// contenido de un reconocimiento interno no se cachea en proxies compartidos, y `nosniff` impide
    /// que el navegador reinterprete lo que se sirvió como imagen.
    /// </summary>
    [Fact]
    public async Task El_archivo_se_sirve_con_cabeceras_que_impiden_cachearlo_y_reinterpretarlo()
    {
        var kudoId = await PublicarUnKudoAsync();
        using var autor = Como(CorreoDocente);
        var adjuntoId = await AdjuntarAsync(autor, kudoId, Png);

        var descarga = await autor.GetAsync($"/api/kudos/{kudoId}/adjuntos/{adjuntoId}/archivo");

        Assert.Contains("no-store", descarga.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Contains("nosniff", descarga.Headers.GetValues("X-Content-Type-Options"));
    }

    /// <summary>
    /// El contrato del detalle no lleva ningún enlace. Antes cada adjunto traía una URL prefirmada
    /// generada al listar, sin rastro de quién la pidió; ahora listar solo da metadatos y el acceso
    /// al archivo es descargar, que sí se audita.
    /// </summary>
    [Fact]
    public async Task El_detalle_de_un_kudo_no_trae_ningun_enlace_a_los_archivos()
    {
        var kudoId = await PublicarUnKudoAsync();
        using var autor = Como(CorreoDocente);
        await AdjuntarAsync(autor, kudoId, Png);

        var texto = await autor.GetStringAsync($"/api/kudos/{kudoId}");

        Assert.DoesNotContain("http", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url", texto, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Que el adjunto se pueda guardar no basta: tiene que **seguir ahí** al leer el Kudo de la base
    /// otra vez, cinco veces seguidas, cada una sobre el Kudo recargado.
    /// </summary>
    [Fact]
    public async Task Los_adjuntos_de_un_kudo_se_acumulan_hasta_el_maximo()
    {
        var kudoId = await PublicarUnKudoAsync();
        using var autor = Como(CorreoDocente);

        for (var i = 1; i <= 5; i++)
        {
            await AdjuntarAsync(autor, kudoId, Png, $"foto{i}.png");
        }

        var detalle = await autor.GetFromJsonAsync<DetalleKudoDto>($"/api/kudos/{kudoId}", Json);
        Assert.Equal(5, detalle!.Adjuntos.Count);

        // El sexto se rechaza con el motivo del límite.
        var sexto = await autor.PostAsJsonAsync($"/api/kudos/{kudoId}/adjuntos/intencion", Intencion("sexta.png"));

        Assert.Equal(HttpStatusCode.BadRequest, sexto.StatusCode);
        Assert.Contains("5", await DetalleDelMensajeAsync(sexto), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Solo_el_autor_del_kudo_puede_reservar_ni_subir_archivos()
    {
        var kudoId = await PublicarUnKudoAsync();
        using var autor = Como(CorreoDocente);
        using var destinatario = Como(CorreoEstudiante);

        var reserva = await destinatario.PostAsJsonAsync($"/api/kudos/{kudoId}/adjuntos/intencion", Intencion());
        Assert.Equal(HttpStatusCode.Forbidden, reserva.StatusCode);

        // El destinatario tampoco puede subir contenido a un adjunto que el autor sí reservó.
        var adjuntoId = await ReservarAsync(autor, kudoId, Intencion());
        var subida = await SubirContenidoAsync(destinatario, kudoId, adjuntoId, Png);

        Assert.Equal(HttpStatusCode.Forbidden, subida.StatusCode);
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

        var adjuntoId = await ReservarAsync(autor, kudoId, Intencion("falso.pdf", "application/pdf"));

        // Bytes de un ejecutable de Windows («MZ»), no de un PDF.
        var subida = await SubirContenidoAsync(autor, kudoId, adjuntoId, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);
        Assert.Equal(HttpStatusCode.NoContent, subida.StatusCode);

        var confirmada = await ConfirmarAsync(autor, kudoId, adjuntoId);

        Assert.Equal(HttpStatusCode.BadRequest, confirmada.StatusCode);
        Assert.Contains("no está permitido", await DetalleDelMensajeAsync(confirmada), StringComparison.Ordinal);

        // Y no queda como adjunto servible.
        var detalle = await autor.GetFromJsonAsync<DetalleKudoDto>($"/api/kudos/{kudoId}", Json);
        Assert.Empty(detalle!.Adjuntos);
    }

    [Theory]
    [InlineData("programa.exe", "application/x-msdownload", 100)]
    [InlineData("documento.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 100)]
    [InlineData("enorme.pdf", "application/pdf", 5 * 1024 * 1024 + 1)]
    public async Task Se_rechaza_pronto_un_tipo_no_permitido_o_un_tamano_declarado_excesivo(string nombre, string tipo, long tamano)
    {
        var kudoId = await PublicarUnKudoAsync();
        using var autor = Como(CorreoDocente);

        var respuesta = await autor.PostAsJsonAsync(
            $"/api/kudos/{kudoId}/adjuntos/intencion", Intencion(nombre, tipo, tamano));

        // Antes de hacer subir nada: es lo que evita que alguien espere a subir cinco megas para
        // enterarse de que el archivo no servía.
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    /// <summary>
    /// **El tamaño se acota mientras se lee el cuerpo, no solo con lo que declaró el cliente.** Quien
    /// declara 1 KB puede mandar 1 GB. Es lo que impide agotar la memoria de la aplicación con un
    /// cuerpo desmesurado.
    /// </summary>
    [Fact]
    public async Task Un_contenido_que_pasa_del_maximo_se_rechaza_aunque_se_haya_declarado_pequeno()
    {
        var kudoId = await PublicarUnKudoAsync();
        using var autor = Como(CorreoDocente);

        var adjuntoId = await ReservarAsync(autor, kudoId, Intencion(tamano: 100));

        var demasiado = new byte[5 * 1024 * 1024 + 1];
        Png.CopyTo(demasiado, 0);

        var subida = await SubirContenidoAsync(autor, kudoId, adjuntoId, demasiado);

        Assert.Equal(HttpStatusCode.BadRequest, subida.StatusCode);
        Assert.Contains("máximo", await DetalleDelMensajeAsync(subida), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_contenido_vacio_se_rechaza()
    {
        var kudoId = await PublicarUnKudoAsync();
        using var autor = Como(CorreoDocente);
        var adjuntoId = await ReservarAsync(autor, kudoId, Intencion());

        var subida = await SubirContenidoAsync(autor, kudoId, adjuntoId, []);

        Assert.Equal(HttpStatusCode.BadRequest, subida.StatusCode);
        Assert.Contains("vacío", await DetalleDelMensajeAsync(subida), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Subir_contenido_a_un_adjunto_que_no_se_reservo_se_rechaza()
    {
        var kudoId = await PublicarUnKudoAsync();
        using var autor = Como(CorreoDocente);

        var subida = await SubirContenidoAsync(autor, kudoId, Guid.NewGuid(), Png);

        Assert.Equal(HttpStatusCode.BadRequest, subida.StatusCode);
    }

    /// <summary>
    /// Un adjunto confirmado tiene su hash registrado: reemplazar el contenido lo dejaría desmentido,
    /// y la trazabilidad de ADR-0012 dejaría de significar nada.
    /// </summary>
    [Fact]
    public async Task El_contenido_de_un_adjunto_confirmado_no_se_puede_reemplazar()
    {
        var kudoId = await PublicarUnKudoAsync();
        using var autor = Como(CorreoDocente);
        var adjuntoId = await AdjuntarAsync(autor, kudoId, Png);

        var reemplazo = await SubirContenidoAsync(autor, kudoId, adjuntoId, [0x89, 0x50, 0x4E, 0x47, 0x01, 0x02, 0x03, 0x04]);

        Assert.Equal(HttpStatusCode.BadRequest, reemplazo.StatusCode);
        Assert.Contains("confirmado", await DetalleDelMensajeAsync(reemplazo), StringComparison.Ordinal);

        // Y el archivo sigue siendo el original.
        var descarga = await autor.GetAsync($"/api/kudos/{kudoId}/adjuntos/{adjuntoId}/archivo");
        Assert.Equal(Png, await descarga.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Un_adjunto_sin_confirmar_no_se_puede_descargar()
    {
        var kudoId = await PublicarUnKudoAsync();
        using var autor = Como(CorreoDocente);
        using var destinatario = Como(CorreoEstudiante);

        // Se reserva y se sube, pero nunca se confirma: el archivo no pasó la inspección.
        var adjuntoId = await ReservarAsync(autor, kudoId, Intencion());
        await SubirContenidoAsync(autor, kudoId, adjuntoId, Png);

        var respuesta = await destinatario.GetAsync($"/api/kudos/{kudoId}/adjuntos/{adjuntoId}/archivo");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task Sin_identidad_no_se_puede_reservar_subir_ni_descargar()
    {
        var kudoId = await PublicarUnKudoAsync();
        using var autor = Como(CorreoDocente);
        var adjuntoId = await AdjuntarAsync(autor, kudoId, Png);

        using var anonimo = Como();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonimo.PostAsJsonAsync($"/api/kudos/{kudoId}/adjuntos/intencion", Intencion())).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await SubirContenidoAsync(anonimo, kudoId, adjuntoId, Png)).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonimo.GetAsync($"/api/kudos/{kudoId}/adjuntos/{adjuntoId}/archivo")).StatusCode);
    }

    /// <summary>
    /// Una cuenta que no existe en la plataforma no descarga nada: autenticarse no es tener cuenta.
    /// Con URL prefirmadas esto no se podía garantizar, porque quien tuviera la URL no necesitaba
    /// identificarse.
    /// </summary>
    [Fact]
    public async Task Una_identidad_sin_cuenta_no_descarga_archivos()
    {
        var kudoId = await PublicarUnKudoAsync();
        using var autor = Como(CorreoDocente);
        var adjuntoId = await AdjuntarAsync(autor, kudoId, Png);

        using var desconocido = Como("nadie@uco.edu.co");

        var respuesta = await desconocido.GetAsync($"/api/kudos/{kudoId}/adjuntos/{adjuntoId}/archivo");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }
}
