using System.Net;
using System.Security.Cryptography;
using UcoKudos.Infrastructure.Objetos;

namespace UcoKudos.IntegrationTests.Objetos;

/// <summary>
/// El adaptador **real** de adjuntos, contra un almacenamiento compatible con S3 de verdad, en un
/// contenedor desechable (ADR-0012).
///
/// Es el nivel donde estas cosas se pueden probar y en ningún otro: la firma de una URL y su
/// caducidad las implementa el almacenamiento, así que un doble en memoria no probaría nada.
///
/// **Las dos pruebas que importan son las negativas.** La prueba de seguridad de los adjuntos no es
/// que la descarga funcione, sino que **falle**: sin firma y con una firma vencida. Sin ellas, la
/// estrategia de seguridad de ADR-0012 sería una afirmación del documento.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AlmacenObjetosRealTests(AlmacenObjetosFixture plataforma)
    : IClassFixture<AlmacenObjetosFixture>
{
    private AlmacenObjetosCompatibleS3 Almacen => plataforma.Almacen;

    private HttpClient Http => plataforma.Http;

    /// <summary>
    /// El ciclo completo: la API firma una URL de subida, el cliente sube **directo** al
    /// almacenamiento sin pasar por la aplicación, y después la API inspecciona el objeto.
    ///
    /// Que los megabytes no atraviesen la API es lo que importa en la red móvil del 90 % de los
    /// usuarios (DRV-001).
    /// </summary>
    [Fact]
    public async Task El_cliente_sube_con_url_prefirmada_y_la_api_inspecciona_el_objeto()
    {
        var contenido = PdfDePrueba();
        var clave = Clave("adjunto");

        var urlSubida = await Almacen.UrlDeSubidaAsync(clave, TimeSpan.FromMinutes(5), CancellationToken.None);

        using var subida = await Http.PutAsync(new Uri(urlSubida), new ByteArrayContent(contenido));
        Assert.True(subida.IsSuccessStatusCode, "La subida con URL prefirmada debería funcionar.");

        var datos = await Almacen.InspeccionarAsync(clave, CancellationToken.None);

        Assert.NotNull(datos);
        Assert.Equal("application/pdf", datos.TipoDetectado);
        Assert.Equal(contenido.Length, datos.TamanoBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(contenido)), datos.HashContenido);
    }

    /// <summary>
    /// **El tipo se detecta por el contenido real, no por la extensión.**
    ///
    /// Se sube un PDF bajo una clave que termina en `.png`. El adaptador tiene que decir que es un
    /// PDF, porque es lo que el archivo es. La extensión y el tipo declarado los controla quien sube
    /// el archivo, así que no se puede confiar en ninguno de los dos (ADR-0012).
    /// </summary>
    [Fact]
    public async Task El_tipo_se_detecta_por_el_contenido_y_no_por_la_extension()
    {
        var clave = Clave("parece-una-imagen.png");

        await SubirAsync(clave, PdfDePrueba());

        var datos = await Almacen.InspeccionarAsync(clave, CancellationToken.None);

        Assert.NotNull(datos);
        Assert.Equal("application/pdf", datos.TipoDetectado);
    }

    /// <summary>
    /// **QA-SEG-02, primera mitad: sin firma no se entra.** Está marcada como omitida, y eso es un
    /// hueco reconocido, no un descuido: conviene explicarlo entero.
    ///
    /// La prueba está escrita y es correcta. Lo que falta es un servidor donde signifique algo. En S3
    /// un contenedor recién creado no admite lectura anónima, pero los servidores compatibles que se
    /// pueden ejecutar en local no reproducen esa negativa: el primero que se probó servía el objeto
    /// sin firma y además ignoraba la caducidad; el actual sí hace cumplir la caducidad —por eso la
    /// otra mitad de QA-SEG-02 sí está verificada— pero sigue atendiendo las peticiones anónimas.
    ///
    /// Dejarla activa contra un servidor permisivo sería peor que omitirla: obligaría a relajar la
    /// comprobación hasta que pasara, y entonces quedaría una prueba verde que no comprueba nada.
    ///
    /// Queda como tarea de la Fase 4: ejecutarla contra el almacenamiento real del despliegue y
    /// quitar esta marca. Mientras tanto, la afirmación honesta es que el contenedor **se crea**
    /// privado, no que se haya comprobado que rechaza a un anónimo.
    /// </summary>
    [Fact(Skip = "El servidor compatible con S3 que se usa en pruebas atiende las peticiones anónimas; " +
                 "verificar contra el almacenamiento de producción (ADR-0012, revisión del 2026-11-09).")]
    public async Task Pedir_el_objeto_sin_firma_es_rechazado()
    {
        var clave = Clave("privado");
        await SubirAsync(clave, PdfDePrueba());

        using var respuesta = await Http.GetAsync(new Uri(plataforma.Endpoint + "/adjuntos/" + clave));

        Assert.False(respuesta.IsSuccessStatusCode, "Un objeto del contenedor privado no debe servirse sin firma.");
        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    /// <summary>
    /// **QA-SEG-02, segunda mitad: una firma vencida tampoco entra.**
    ///
    /// Es lo que hace que compartir el enlace fuera no dé acceso permanente. Se firma con una validez
    /// ya pasada, que es la forma de probar la caducidad sin esperar minutos reales.
    /// </summary>
    [Fact]
    public async Task Una_url_prefirmada_vencida_es_rechazada()
    {
        var clave = Clave("vencido");
        await SubirAsync(clave, PdfDePrueba());

        var urlVencida = await Almacen.UrlDeDescargaAsync(clave, TimeSpan.FromSeconds(-30), CancellationToken.None);

        using var respuesta = await Http.GetAsync(new Uri(urlVencida));

        Assert.False(respuesta.IsSuccessStatusCode, "Una URL prefirmada vencida no debe servir el objeto.");
    }

    /// <summary>Una firma vigente sí sirve el objeto, y devuelve el contenido exacto.</summary>
    [Fact]
    public async Task Una_url_prefirmada_vigente_sirve_el_objeto()
    {
        var contenido = PdfDePrueba();
        var clave = Clave("vigente");
        await SubirAsync(clave, contenido);

        var url = await Almacen.UrlDeDescargaAsync(clave, TimeSpan.FromMinutes(5), CancellationToken.None);

        using var respuesta = await Http.GetAsync(new Uri(url));

        Assert.True(respuesta.IsSuccessStatusCode);
        Assert.Equal(contenido, await respuesta.Content.ReadAsByteArrayAsync());
    }

    /// <summary>
    /// Inspeccionar un objeto que nunca se subió devuelve null, no una excepción.
    ///
    /// Ocurre de verdad: el cliente pide la URL de subida y abandona. No es un error del sistema, y
    /// el caso de uso lo traduce a un mensaje claro.
    /// </summary>
    [Fact]
    public async Task Inspeccionar_un_objeto_inexistente_devuelve_nulo() =>
        Assert.Null(await Almacen.InspeccionarAsync(Clave("nunca-subido"), CancellationToken.None));

    /// <summary>Eliminar quita el objeto, que es lo que sostiene la limpieza definitiva de adjuntos.</summary>
    [Fact]
    public async Task Eliminar_quita_el_objeto_del_almacenamiento()
    {
        var clave = Clave("temporal");
        await SubirAsync(clave, PdfDePrueba());

        Assert.NotNull(await Almacen.InspeccionarAsync(clave, CancellationToken.None));

        await Almacen.EliminarAsync(clave, CancellationToken.None);

        Assert.Null(await Almacen.InspeccionarAsync(clave, CancellationToken.None));
    }

    /// <summary>
    /// **Asegurar el contenedor dos veces no falla.** Es una prueba de regresión, no una hipótesis.
    ///
    /// La primera versión del adaptador reventaba aquí: el servidor contesta que el contenedor ya
    /// existe y es tuyo, y eso se estaba tratando como error, así que la aplicación no arrancaba
    /// contra un almacenamiento que ya tenía su contenedor creado —es decir, en el segundo arranque
    /// de siempre, y en cualquier despliegue con más de una instancia.
    /// </summary>
    [Fact]
    public async Task Asegurar_el_contenedor_dos_veces_es_idempotente()
    {
        using var otroCliente = plataforma.CrearCliente();
        var otroArranque = new AlmacenObjetosCompatibleS3(plataforma.Opciones, otroCliente);

        await otroArranque.AsegurarContenedorPrivadoAsync(CancellationToken.None);
        await otroArranque.AsegurarContenedorPrivadoAsync(CancellationToken.None);
    }

    private static string Clave(string nombre) => "kudos/" + Guid.NewGuid().ToString("N") + "/" + nombre;

    private async Task SubirAsync(string clave, byte[] contenido)
    {
        var url = await Almacen.UrlDeSubidaAsync(clave, TimeSpan.FromMinutes(5), CancellationToken.None);

        try
        {
            using var respuesta = await Http.PutAsync(new Uri(url), new ByteArrayContent(contenido));
            respuesta.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException error)
        {
            // La URL firmada se incluye en el mensaje a propósito: sin ella, un fallo de red aquí no
            // dice si el problema es el servidor o la URL que se generó.
            throw new InvalidOperationException(
                "Falló la subida a la URL prefirmada. Endpoint: " + plataforma.Endpoint + " URL: " + url, error);
        }
    }

    /// <summary>Un PDF mínimo: lo que importa es su firma, los cuatro primeros bytes.</summary>
    private static byte[] PdfDePrueba() =>
        System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n% contenido de prueba de UCO Kudos\n%%EOF\n");
}
