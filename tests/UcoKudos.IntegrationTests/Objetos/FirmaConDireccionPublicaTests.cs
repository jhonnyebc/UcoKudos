using Amazon.Runtime;
using Amazon.S3;
using UcoKudos.Infrastructure.Objetos;

namespace UcoKudos.IntegrationTests.Objetos;

/// <summary>
/// Las URL prefirmadas se firman contra la dirección que usará el **navegador**, no la interna.
///
/// Existe por un fallo que ninguna otra prueba podía ver: la API habla con el almacenamiento por la
/// red de Docker (<c>http://objetos:4566</c>), y las URL salían firmadas contra ese nombre. El
/// navegador no lo resuelve y, además, es contenido mixto desde una página HTTPS: en producción
/// subir un adjunto no podía funcionar. Todas las pruebas pasaban porque corren dentro de la red de
/// contenedores, donde ese nombre sí existe.
///
/// No necesita contenedores: firmar es un cálculo local y no hace ninguna llamada de red.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FirmaConDireccionPublicaTests
{
    private const string Interna = "http://objetos:4566";
    private const string Publica = "https://kudos.ejemplo.test";

    private static AlmacenObjetosCompatibleS3 Crear(string? endpointPublico)
    {
        var opciones = new OpcionesObjetos
        {
            Proveedor = OpcionesObjetos.ProveedorS3,
            Endpoint = Interna,
            EndpointPublico = endpointPublico,
            Contenedor = "adjuntos",
            Usuario = "usuario-de-prueba",
            Password = "clave-de-prueba"
        };

        var credenciales = new BasicAWSCredentials(opciones.Usuario, opciones.Password);

        AmazonS3Client ClienteContra(string direccion) => new(
            credenciales,
            new AmazonS3Config
            {
                ServiceURL = direccion,
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
                UseHttp = direccion.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            });

        return new AlmacenObjetosCompatibleS3(
            opciones,
            ClienteContra(Interna),
            new ClienteDeFirmaS3(ClienteContra(opciones.EndpointDeFirma)));
    }

    [Fact]
    public async Task La_url_de_subida_se_firma_contra_la_direccion_publica()
    {
        var url = await Crear(Publica).UrlDeSubidaAsync("kudos/abc/def", TimeSpan.FromMinutes(5), CancellationToken.None);

        // Estilo de ruta, en el origen público y en https: es lo que el navegador va a llamar.
        Assert.StartsWith(Publica + "/adjuntos/kudos/abc/def?", url, StringComparison.Ordinal);
        Assert.Contains("X-Amz-Signature=", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_url_de_descarga_se_firma_contra_la_direccion_publica()
    {
        var url = await Crear(Publica).UrlDeDescargaAsync("kudos/abc/def", TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.StartsWith(Publica + "/adjuntos/kudos/abc/def?", url, StringComparison.Ordinal);
    }

    /// <summary>
    /// Lo que se comprueba es lo que NO debe aparecer. Una URL con el nombre interno del contenedor
    /// es inservible desde fuera y además revela la topología de la red al cliente.
    /// </summary>
    [Fact]
    public async Task La_url_no_revela_la_direccion_interna()
    {
        var url = await Crear(Publica).UrlDeSubidaAsync("kudos/abc/def", TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.DoesNotContain("objetos", url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("4566", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_direccion_publica_se_firma_contra_la_interna()
    {
        // Es lo que ocurre en local, donde las dos direcciones coinciden.
        var url = await Crear(null).UrlDeSubidaAsync("kudos/abc/def", TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.StartsWith(Interna + "/adjuntos/kudos/abc/def?", url, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Una_direccion_publica_vacia_se_trata_como_ausente(string vacia)
    {
        // Una variable de entorno sin valor llega como cadena vacía, no como nula. Sin esto, definir
        // la variable y dejarla en blanco firmaría contra una dirección inexistente.
        var opciones = new OpcionesObjetos { Endpoint = Interna, EndpointPublico = vacia };

        Assert.Equal(Interna, opciones.EndpointDeFirma);
    }
}
