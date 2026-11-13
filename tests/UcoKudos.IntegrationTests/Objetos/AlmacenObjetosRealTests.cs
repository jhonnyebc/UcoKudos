using System.Security.Cryptography;
using UcoKudos.Infrastructure.Objetos;

namespace UcoKudos.IntegrationTests.Objetos;

/// <summary>
/// El adaptador **real** de adjuntos, contra un almacenamiento compatible con S3 de verdad, en un
/// contenedor desechable (ADR-0012).
///
/// **Qué cambió respecto a la primera versión de este archivo, y por qué importa.** Antes se probaba
/// el ciclo con URL prefirmadas: se firmaba una URL, se subía con ella y se comprobaba que una firma
/// vencida se rechazaba. Todo pasaba, y en producción subir un adjunto fallaba con un 403. La causa:
/// el servidor de objetos valida las firmas con un secreto fijo y público (<c>test</c>), y esta
/// fixture firmaba justamente con <c>test</c>, así que la prueba nunca podía ver el problema. Un
/// entorno de pruebas que comparte la suposición equivocada del código no encuentra el fallo.
///
/// La solución fue dejar de usar URL prefirmadas: los archivos pasan por la API, que habla con el
/// almacenamiento por la red interna y es la única que lo hace. Por eso estas pruebas ejercitan
/// <c>GuardarAsync</c> y <c>LeerAsync</c>, que son lo que usa de verdad la aplicación, y desaparecen
/// las de firma y caducidad: ya no hay nada que firmar ni que caducar.
///
/// Tampoco hay una prueba de «sin firma se rechaza»: el almacenamiento no tiene ninguna ruta
/// pública, así que esa garantía ya no depende de lo que el servidor de objetos haga con un anónimo,
/// que era justo lo que no se podía comprobar. Ver ADR-0012, revisión del 2026-11-13.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AlmacenObjetosRealTests(AlmacenObjetosFixture plataforma)
    : IClassFixture<AlmacenObjetosFixture>
{
    private AlmacenObjetosCompatibleS3 Almacen => plataforma.Almacen;

    /// <summary>
    /// El ciclo completo, como lo hace la aplicación: guardar lo que llegó y después inspeccionarlo
    /// para saber qué es de verdad.
    /// </summary>
    [Fact]
    public async Task Lo_que_se_guarda_se_lee_igual_y_se_inspecciona()
    {
        var contenido = PdfDePrueba();
        var clave = Clave("adjunto");

        await Almacen.GuardarAsync(clave, contenido, CancellationToken.None);

        Assert.Equal(contenido, await Almacen.LeerAsync(clave, CancellationToken.None));

        var datos = await Almacen.InspeccionarAsync(clave, CancellationToken.None);

        Assert.NotNull(datos);
        Assert.Equal("application/pdf", datos.TipoDetectado);
        Assert.Equal(contenido.Length, datos.TamanoBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(contenido)), datos.HashContenido);
    }

    /// <summary>
    /// **El tipo se detecta por el contenido real, no por la extensión.**
    ///
    /// Se guarda un PDF bajo una clave que termina en `.png`. El adaptador tiene que decir que es un
    /// PDF, porque es lo que el archivo es. La extensión y el tipo declarado los controla quien sube
    /// el archivo, así que no se puede confiar en ninguno de los dos (ADR-0012).
    /// </summary>
    [Fact]
    public async Task El_tipo_se_detecta_por_el_contenido_y_no_por_la_extension()
    {
        var clave = Clave("parece-una-imagen.png");

        await Almacen.GuardarAsync(clave, PdfDePrueba(), CancellationToken.None);

        var datos = await Almacen.InspeccionarAsync(clave, CancellationToken.None);

        Assert.NotNull(datos);
        Assert.Equal("application/pdf", datos.TipoDetectado);
    }

    /// <summary>
    /// Los bytes de un archivo binario vuelven **exactos**, incluidos los ceros y los valores
    /// altos. Un adaptador que tratara el contenido como texto lo corrompería sin que nada fallara,
    /// y un adjunto dañado se descubre cuando alguien lo abre.
    /// </summary>
    [Fact]
    public async Task Un_archivo_binario_vuelve_byte_por_byte()
    {
        var contenido = new byte[4096];
        for (var i = 0; i < contenido.Length; i++)
        {
            contenido[i] = (byte)(i * 31 % 256);
        }

        // Los de un PNG al principio: es lo que el servidor mira para saber el tipo.
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(contenido, 0);

        var clave = Clave("binario");
        await Almacen.GuardarAsync(clave, contenido, CancellationToken.None);

        Assert.Equal(contenido, await Almacen.LeerAsync(clave, CancellationToken.None));
        Assert.Equal("image/png", (await Almacen.InspeccionarAsync(clave, CancellationToken.None))!.TipoDetectado);
    }

    /// <summary>
    /// El tamaño máximo de un adjunto (5 MiB) se guarda y se lee entero. Es el caso límite del
    /// dominio, y el que fallaría si el adaptador tuviera algún tope propio.
    /// </summary>
    [Fact]
    public async Task Un_archivo_del_tamano_maximo_se_guarda_y_se_lee_entero()
    {
        var contenido = new byte[5 * 1024 * 1024];
        new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.CopyTo(contenido, 0);

        var clave = Clave("maximo");
        await Almacen.GuardarAsync(clave, contenido, CancellationToken.None);

        var leido = await Almacen.LeerAsync(clave, CancellationToken.None);

        Assert.NotNull(leido);
        Assert.Equal(contenido.Length, leido.Length);
        Assert.Equal(SHA256.HashData(contenido), SHA256.HashData(leido));
    }

    /// <summary>Guardar sobre una clave existente reemplaza el contenido.</summary>
    [Fact]
    public async Task Guardar_dos_veces_la_misma_clave_deja_el_ultimo_contenido()
    {
        var clave = Clave("reemplazo");

        await Almacen.GuardarAsync(clave, PdfDePrueba(), CancellationToken.None);
        await Almacen.GuardarAsync(clave, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], CancellationToken.None);

        Assert.Equal("image/png", (await Almacen.InspeccionarAsync(clave, CancellationToken.None))!.TipoDetectado);
    }

    /// <summary>
    /// Leer o inspeccionar un objeto que nunca se guardó devuelve null, no una excepción.
    ///
    /// Ocurre de verdad: se reserva el adjunto y la subida no llega a completarse. No es un error del
    /// sistema, y el caso de uso lo traduce a un mensaje claro.
    /// </summary>
    [Fact]
    public async Task Leer_e_inspeccionar_un_objeto_inexistente_devuelve_nulo()
    {
        var clave = Clave("nunca-subido");

        Assert.Null(await Almacen.LeerAsync(clave, CancellationToken.None));
        Assert.Null(await Almacen.InspeccionarAsync(clave, CancellationToken.None));
    }

    /// <summary>Eliminar quita el objeto, que es lo que sostiene la limpieza definitiva de adjuntos.</summary>
    [Fact]
    public async Task Eliminar_quita_el_objeto_del_almacenamiento()
    {
        var clave = Clave("temporal");
        await Almacen.GuardarAsync(clave, PdfDePrueba(), CancellationToken.None);

        Assert.NotNull(await Almacen.LeerAsync(clave, CancellationToken.None));

        await Almacen.EliminarAsync(clave, CancellationToken.None);

        Assert.Null(await Almacen.LeerAsync(clave, CancellationToken.None));
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

    /// <summary>
    /// **Guardar en un almacenamiento donde el contenedor no existe lo crea, en lugar de fallar.**
    /// Prueba de regresión del segundo defecto del primer uso real: el adaptador decía crear el
    /// contenedor al arrancar, pero nada lo hacía, y en producción cada subida acababa en un 500
    /// («The specified bucket does not exist»). Las demás pruebas no lo veían porque su fixture sí
    /// llamaba a <c>AsegurarContenedorPrivadoAsync</c>: otra vez, el entorno de pruebas hacía por el
    /// código lo que el código no hacía.
    /// </summary>
    [Fact]
    public async Task Guardar_crea_el_contenedor_si_no_existe()
    {
        var opciones = new OpcionesObjetos
        {
            Proveedor = OpcionesObjetos.ProveedorS3,
            Endpoint = plataforma.Opciones.Endpoint,
            Contenedor = "sin-crear-" + Guid.NewGuid().ToString("N")[..8],
            Usuario = plataforma.Opciones.Usuario,
            Password = plataforma.Opciones.Password
        };

        using var cliente = plataforma.CrearCliente();
        var almacen = new AlmacenObjetosCompatibleS3(opciones, cliente);
        var clave = Clave("primer-uso");

        // Sin llamar antes a AsegurarContenedorPrivadoAsync, como en producción.
        await almacen.GuardarAsync(clave, PdfDePrueba(), CancellationToken.None);

        Assert.Equal(PdfDePrueba(), await almacen.LeerAsync(clave, CancellationToken.None));
    }

    private static string Clave(string nombre) => "kudos/" + Guid.NewGuid().ToString("N") + "/" + nombre;

    /// <summary>Un PDF mínimo: lo que importa es su firma, los cuatro primeros bytes.</summary>
    private static byte[] PdfDePrueba() =>
        System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n% contenido de prueba de UCO Kudos\n%%EOF\n");
}
