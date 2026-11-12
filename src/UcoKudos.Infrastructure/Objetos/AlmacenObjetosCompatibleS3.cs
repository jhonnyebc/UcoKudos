using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using UcoKudos.Application.Notificaciones.Salida.Puertos;

namespace UcoKudos.Infrastructure.Objetos;

/// <summary>
/// Adaptador real del almacenamiento de adjuntos, contra la API de S3 (ADR-0012).
///
/// Habla la API de S3 **por decisión explícita**, no por casualidad: el mismo adaptador funciona
/// contra el almacenamiento autoalojado de hoy y contra un servicio gestionado el día que haya
/// presupuesto, cambiando tres variables de configuración. Es la forma concreta de dejar la puerta
/// abierta sin pagar hoy por ella.
///
/// Las cuatro estrategias que el enunciado pide, aquí:
///
///  - **Almacenamiento**: el objeto va al contenedor privado, con una clave generada.
///  - **Seguridad**: solo se sirve con URL prefirmada de vida corta, que la API emite después de
///    comprobar permisos. El almacenamiento no sabe nada de la jerarquía: solo honra una firma.
///  - **Recuperación**: se guarda el hash del contenido, así que al restaurar se puede verificar que
///    el archivo es el mismo que se subió.
///  - **Trazabilidad**: cada carga y cada descarga se auditan, y eso ocurre en el caso de uso.
/// </summary>
public sealed class AlmacenObjetosCompatibleS3 : IAlmacenObjetos
{
    private readonly IAmazonS3 _cliente;
    private readonly IAmazonS3 _firmador;
    private readonly OpcionesObjetos _opciones;

    public AlmacenObjetosCompatibleS3(
        OpcionesObjetos opciones, IAmazonS3 cliente, ClienteDeFirmaS3? clienteDeFirma = null)
    {
        _opciones = opciones ?? throw new ArgumentNullException(nameof(opciones));
        _cliente = cliente ?? throw new ArgumentNullException(nameof(cliente));

        // Firmar es un cálculo local: no hace ninguna llamada de red. Por eso puede hacerse con un
        // cliente configurado con la dirección pública sin que esa dirección tenga que ser alcanzable
        // desde la API. Sin cliente de firma se usa el mismo de las operaciones, que es lo que ocurre
        // en local, donde las dos direcciones coinciden.
        _firmador = clienteDeFirma?.Cliente ?? cliente;
    }

    /// <summary>
    /// Esquema con el que se firman las URL, deducido del endpoint configurado.
    ///
    /// Hay que decírselo en cada petición y no basta con configurar el cliente: el SDK firma en https
    /// aunque el endpoint sea http, y entonces la URL apunta a un puerto donde no hay TLS y el cliente
    /// falla en el saludo. Se detectó en la prueba de integración, no razonándolo.
    ///
    /// En el servidor el endpoint será https y esto dará https solo, que es lo correcto: un adjunto no
    /// debe viajar en claro por internet.
    /// </summary>
    private Protocol Esquema =>
        _opciones.EndpointDeFirma.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? Protocol.HTTP
            : Protocol.HTTPS;

    public Task<string> UrlDeSubidaAsync(string clave, TimeSpan validez, CancellationToken ct) =>
        Task.FromResult(_firmador.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _opciones.Contenedor,
            Key = clave,
            Verb = HttpVerb.PUT,
            Protocol = Esquema,
            Expires = DateTime.UtcNow.Add(validez)
        }));

    /// <summary>
    /// URL de descarga de vida corta. La autorización ya la comprobó el caso de uso antes de llamar
    /// aquí, que es lo que hace posible QA-SEG-02: compartir el enlace fuera no da acceso permanente.
    /// </summary>
    public Task<string> UrlDeDescargaAsync(string clave, TimeSpan validez, CancellationToken ct) =>
        Task.FromResult(_firmador.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _opciones.Contenedor,
            Key = clave,
            Verb = HttpVerb.GET,
            Protocol = Esquema,
            Expires = DateTime.UtcNow.Add(validez)
        }));

    /// <summary>
    /// Inspecciona el objeto ya subido: tipo real, tamaño y hash.
    ///
    /// **El tipo se determina leyendo los primeros bytes del archivo**, no por la extensión ni por lo
    /// que declaró el cliente al pedir la URL de subida: las dos las controla quien sube el archivo.
    /// El tipo que el almacenamiento tenga registrado tampoco sirve, porque lo fija quien sube.
    /// </summary>
    public async Task<DatosObjeto?> InspeccionarAsync(string clave, CancellationToken ct)
    {
        try
        {
            using var respuesta = await _cliente.GetObjectAsync(
                new GetObjectRequest { BucketName = _opciones.Contenedor, Key = clave }, ct);

            // El contenido se lee a memoria porque el tamaño está acotado a unos pocos megabytes
            // (ADR-0012), y hace falta entero para calcular el hash de integridad.
            using var memoria = new MemoryStream();
            await respuesta.ResponseStream.CopyToAsync(memoria, ct);
            var contenido = memoria.ToArray();

            return new DatosObjeto(
                FirmaDeArchivo.Detectar(contenido),
                contenido.LongLength,
                Convert.ToHexStringLower(SHA256.HashData(contenido)));
        }
        catch (AmazonS3Exception error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // El cliente pidió la URL pero nunca subió el archivo. No es un error del sistema.
            return null;
        }
    }

    public async Task EliminarAsync(string clave, CancellationToken ct) =>
        await _cliente.DeleteObjectAsync(
            new DeleteObjectRequest { BucketName = _opciones.Contenedor, Key = clave }, ct);

    /// <summary>
    /// Asegura que el contenedor existe y que **no** tiene acceso público.
    ///
    /// Se llama al arrancar. Es la decisión central de ADR-0012: un contenedor público haría
    /// irrelevante todo lo demás, porque cualquiera con la URL entraría y no habría forma de trazar
    /// las descargas.
    /// </summary>
    public async Task AsegurarContenedorPrivadoAsync(CancellationToken ct)
    {
        await CrearContenedorSiFaltaAsync(ct);
        await BloquearAccesoPublicoAsync(ct);
    }

    /// <summary>
    /// Crea el contenedor si no está, **tolerando que ya exista**.
    ///
    /// La comprobación previa no basta y es importante entender por qué: entre consultar y crear hay
    /// una ventana, así que dos instancias arrancando a la vez intentarían crearlo las dos y una
    /// fallaría. El servidor ya responde con un error específico para ese caso, y tratarlo como
    /// éxito es lo correcto: el resultado que se buscaba, que el contenedor exista, se cumplió.
    ///
    /// Esto no es una hipótesis: al escribir la prueba de integración, el arranque falló justamente
    /// aquí contra un contenedor recién levantado.
    /// </summary>
    private async Task CrearContenedorSiFaltaAsync(CancellationToken ct)
    {
        try
        {
            await _cliente.PutBucketAsync(
                new PutBucketRequest { BucketName = _opciones.Contenedor, UseClientRegion = true }, ct);
        }
        catch (AmazonS3Exception error) when (YaExistiaElContenedor(error))
        {
            // El contenedor ya estaba. El objetivo que se buscaba —que exista— está cumplido, así que
            // esto no es un fallo y no se propaga.
        }
    }

    /// <summary>
    /// ¿El error de creación significa «ya existe y es tuyo»?
    ///
    /// La respuesta estándar de S3 para este caso es el código <c>BucketAlreadyOwnedByYou</c>, y es lo
    /// primero que se mira. Pero **no todos los servidores compatibles rellenan el código**: se
    /// comprobó contra el servidor que usan el entorno local y las pruebas, que devuelve el conflicto
    /// con el código vacío y solo el mensaje.
    ///
    /// Por eso hay un segundo reconocimiento por el texto. Es feo y conviene decir por qué se acepta
    /// en lugar de disimularlo: la alternativa que se intentó primero —crear y, si falla, releer el
    /// listado de contenedores para confirmar— resultó **peor**, porque ese mismo servidor afirma que
    /// el contenedor existe y luego no lo incluye en el listado, de modo que la comprobación daba un
    /// falso negativo y el arranque fallaba con el contenedor ya creado. Entre fiarse del mensaje y
    /// fiarse de un listado que se contradice con él, el mensaje acierta.
    ///
    /// El riesgo real de esto es que un servidor cambie el texto, y el precio de equivocarse es
    /// pequeño: se propagaría un error de arranque con el motivo escrito, no un contenedor público.
    /// </summary>
    private static bool YaExistiaElContenedor(AmazonS3Exception error) =>
        error.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists"
        || error.StatusCode == System.Net.HttpStatusCode.Conflict
        || error.Message.Contains("already own it", StringComparison.OrdinalIgnoreCase)
        || error.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Bloqueo explícito del acceso público, por si el contenedor ya existía creado a mano.
    ///
    /// Es defensa en profundidad: la garantía principal es que el contenedor se crea privado. No
    /// todos los servidores compatibles con S3 implementan esta operación, así que si responde que no
    /// la soporta no se detiene el arranque; lo que no se hace es tratarla como aplicada.
    /// </summary>
    private async Task BloquearAccesoPublicoAsync(CancellationToken ct)
    {
        try
        {
            await _cliente.PutPublicAccessBlockAsync(new PutPublicAccessBlockRequest
            {
                BucketName = _opciones.Contenedor,
                PublicAccessBlockConfiguration = new PublicAccessBlockConfiguration
                {
                    BlockPublicAcls = true,
                    BlockPublicPolicy = true,
                    IgnorePublicAcls = true,
                    RestrictPublicBuckets = true
                }
            }, ct);

            BloqueoDeAccesoPublicoAplicado = true;
        }
        catch (AmazonS3Exception)
        {
            // Se tolera **cualquier** rechazo de esta operación concreta, y conviene justificarlo en
            // lugar de dejarlo como un catch perezoso.
            //
            // El primer intento solo toleraba «no implementado». No sirvió: el servidor que usan el
            // entorno local y las pruebas no implementa esta operación y, en vez de decirlo, contesta
            // con un error genérico de otra cosa. Encadenar códigos de error aquí es perseguir las
            // rarezas de cada servidor compatible con S3, y cada uno tiene las suyas.
            //
            // Lo que no se hace es fingir que se aplicó: queda registrado abajo. Y lo importante es
            // que la privacidad **no depende de esta llamada**: un contenedor de S3 se crea privado,
            // y esto solo refuerza uno que ya existiera creado a mano. Que falle no abre nada.
            BloqueoDeAccesoPublicoAplicado = false;
        }
    }

    /// <summary>
    /// ¿El servidor aceptó el bloqueo explícito de acceso público?
    ///
    /// Se expone para que el entorno pueda comprobarlo y para que las pruebas lo verifiquen: un
    /// «false» aquí significa que la privacidad del contenedor depende solo de cómo se creó, y eso
    /// hay que saberlo antes de confiar adjuntos a ese servidor.
    /// </summary>
    public bool BloqueoDeAccesoPublicoAplicado { get; private set; }
}

/// <summary>
/// Detecta el tipo de un archivo por su firma, los primeros bytes del contenido.
///
/// Está aparte porque lo usan el adaptador real y el simulado, y las pruebas lo comprueban por su
/// cuenta: es la pieza que impide que alguien suba un ejecutable con extensión .pdf.
/// </summary>
public static class FirmaDeArchivo
{
    public const string Desconocido = "application/octet-stream";

    public static string Detectar(byte[] contenido)
    {
        ArgumentNullException.ThrowIfNull(contenido);

        // %PDF
        if (Empieza(contenido, [0x25, 0x50, 0x44, 0x46]))
            return "application/pdf";

        // JPEG: FF D8 FF
        if (Empieza(contenido, [0xFF, 0xD8, 0xFF]))
            return "image/jpeg";

        // PNG: 89 'P' 'N' 'G'
        if (Empieza(contenido, [0x89, 0x50, 0x4E, 0x47]))
            return "image/png";

        // WebP: 'RIFF' .... 'WEBP'
        if (Empieza(contenido, [0x52, 0x49, 0x46, 0x46])
            && contenido.Length >= 12
            && contenido[8] == 0x57 && contenido[9] == 0x45 && contenido[10] == 0x42 && contenido[11] == 0x50)
        {
            return "image/webp";
        }

        return Desconocido;
    }

    private static bool Empieza(byte[] contenido, byte[] firma)
    {
        if (contenido.Length < firma.Length)
            return false;

        for (var i = 0; i < firma.Length; i++)
        {
            if (contenido[i] != firma[i])
                return false;
        }

        return true;
    }
}

/// <summary>
/// El cliente con el que se firman las URL que usa el navegador.
///
/// Es un tipo propio y no un segundo <c>IAmazonS3</c> registrado en el contenedor porque dos
/// registros de la misma interfaz se pisan: quien pidiera <c>IAmazonS3</c> recibiría el último sin
/// que nada avisara, y las operaciones internas acabarían yendo a la dirección pública.
/// </summary>
public sealed class ClienteDeFirmaS3(IAmazonS3 cliente)
{
    public IAmazonS3 Cliente { get; } = cliente ?? throw new ArgumentNullException(nameof(cliente));
}
