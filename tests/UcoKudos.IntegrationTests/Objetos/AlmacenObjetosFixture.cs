using Amazon.Runtime;
using Amazon.S3;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using UcoKudos.Infrastructure.Objetos;

namespace UcoKudos.IntegrationTests.Objetos;

/// <summary>
/// Un único almacenamiento compatible con S3 para toda la clase de pruebas.
///
/// Es **un** contenedor, no uno por prueba, y la razón no es solo la velocidad: levantar siete
/// contenedores a la vez, encima del de PostgreSQL y dentro del contenedor donde corren las pruebas,
/// saturaba Docker y las pruebas fallaban por tiempo de espera sin llegar a ejecutar nada. Un fallo
/// así es peor que uno real, porque parece del código y no lo es.
///
/// Compartirlo es seguro porque cada prueba usa una clave propia generada: no se pisan entre ellas.
///
/// No es MinIO, y conviene saber por qué: MinIO cerró sus imágenes públicas, tanto las propias como
/// las de Bitnami, así que ya no se puede descargar sin credenciales. Está anotado como revisión en
/// ADR-0012, porque cambia el SBB elegido.
///
/// **Una advertencia sobre lo que este servidor NO prueba.** Valida las firmas de las URL prefirmadas
/// con un secreto fijo y público (<c>test</c>), y por eso las credenciales de abajo son exactamente
/// esas. Mientras la aplicación usaba URL prefirmadas, las pruebas pasaban porque firmaban con
/// <c>test</c>, y en producción —con un secreto de verdad— todas las subidas daban 403. Ahora los
/// archivos pasan por la API y el adaptador solo guarda y lee por la red interna, así que esa
/// diferencia ya no puede ocultar nada; pero queda escrito porque un entorno de pruebas que comparte
/// la suposición del código no encuentra el fallo.
/// </summary>
public sealed class AlmacenObjetosFixture : IAsyncLifetime
{
    private const int PuertoS3 = 4566;

    private readonly IContainer _contenedor = new ContainerBuilder("localstack/localstack:4.9")
        .WithPortBinding(PuertoS3, assignRandomHostPort: true)
        .WithEnvironment("SERVICES", "s3")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(peticion =>
            peticion.ForPort(PuertoS3).ForPath("/_localstack/health")))
        .Build();

    public AlmacenObjetosCompatibleS3 Almacen { get; private set; } = null!;

    public OpcionesObjetos Opciones { get; private set; } = null!;

    public IAmazonS3 Cliente { get; private set; } = null!;

    public string Endpoint { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();

        Endpoint = "http://" + _contenedor.Hostname + ":" + _contenedor.GetMappedPublicPort(PuertoS3);

        Opciones = new OpcionesObjetos
        {
            Proveedor = OpcionesObjetos.ProveedorS3,
            Endpoint = Endpoint,
            Contenedor = "adjuntos",

            // Las credenciales que este servidor de pruebas reconoce. No son un secreto ni se parecen
            // a uno: solo existen dentro del contenedor desechable de esta clase.
            Usuario = "test",
            Password = "test"
        };

        Cliente = CrearCliente();

        Almacen = new AlmacenObjetosCompatibleS3(Opciones, Cliente);
        await Almacen.AsegurarContenedorPrivadoAsync(CancellationToken.None);
    }

    /// <summary>
    /// Cliente sin reintentos y con un tiempo de espera corto, a propósito.
    ///
    /// Con los valores por omisión, un error que el servidor contesta al instante tardaba minutos en
    /// llegar a la prueba porque el SDK lo reintentaba: el fallo seguía siendo el mismo, pero costaba
    /// cinco minutos verlo.
    /// </summary>
    public AmazonS3Client CrearCliente() =>
        new(
            new BasicAWSCredentials(Opciones.Usuario, Opciones.Password),
            new AmazonS3Config
            {
                ServiceURL = Opciones.Endpoint,
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",

                // Igual que en el arranque real: el esquema lo manda el endpoint.
                UseHttp = Opciones.Endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
                MaxErrorRetry = 0,
                Timeout = TimeSpan.FromSeconds(10)
            });

    public async Task DisposeAsync()
    {
        Cliente.Dispose();
        await _contenedor.DisposeAsync();
    }
}
