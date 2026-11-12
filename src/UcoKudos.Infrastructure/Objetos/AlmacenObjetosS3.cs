using System.Collections.Concurrent;
using System.Security.Cryptography;
using UcoKudos.Application.Notificaciones.Salida.Puertos;

namespace UcoKudos.Infrastructure.Objetos;

/// <summary>Configuración del almacenamiento de adjuntos (ADR-0012).</summary>
public sealed class OpcionesObjetos
{
    public const string Seccion = "UcoKudos:Objetos";
    public const string ProveedorSimulado = "Simulado";
    public const string ProveedorS3 = "S3";

    public string Proveedor { get; set; } = ProveedorSimulado;

    public string Endpoint { get; set; } = "http://localhost:9000";

    /// <summary>
    /// Dirección con la que se **firman** las URL que recibe el navegador, si difiere de
    /// <see cref="Endpoint"/>.
    ///
    /// **Existen dos direcciones porque hay dos lectores distintos.** La API habla con el
    /// almacenamiento por la red interna (<c>http://objetos:4566</c>), pero la URL prefirmada la usa
    /// el *navegador*, que no ve esa red: un nombre interno de Docker no se resuelve desde fuera y,
    /// además, sería contenido mixto desde una página HTTPS. Sin esta opción las URL salían firmadas
    /// contra la dirección interna y subir un adjunto no podía funcionar en producción, aunque todas
    /// las pruebas pasaran, porque corren dentro de la red de contenedores.
    ///
    /// Una firma SigV4 incluye el <c>Host</c> al que va dirigida, así que la URL tiene que firmarse
    /// contra la dirección por la que de verdad se va a llamar. Si queda vacía se usa
    /// <see cref="Endpoint"/>.
    /// </summary>
    public string? EndpointPublico { get; set; }

    /// <summary>La dirección contra la que se firma: la pública si existe, si no la interna.</summary>
    public string EndpointDeFirma => string.IsNullOrWhiteSpace(EndpointPublico) ? Endpoint : EndpointPublico;

    /// <summary>
    /// El contenedor es **privado**: sin lectura anónima. Es la decisión central de ADR-0012, porque
    /// un contenedor público haría irrelevante todo lo demás.
    /// </summary>
    public string Contenedor { get; set; } = "adjuntos";

    public string? Usuario { get; set; }

    /// <summary>SECRETO.</summary>
    public string? Password { get; set; }

    public bool EsSimulado => string.Equals(Proveedor, ProveedorSimulado, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Almacenamiento de objetos simulado, en memoria.
///
/// Está aquí y no solo en las pruebas porque el entorno de desarrollo arranca sin credenciales: la
/// plataforma tiene que poder funcionar antes de que alguien configure el almacenamiento.
///
/// Reproduce las dos cosas que importan del comportamiento real: la URL prefirmada **vence**, y la
/// inspección devuelve el tipo detectado del contenido y no el declarado. La prueba de QA-SEG-02
/// comprueba justamente que una URL vencida sea rechazada.
/// </summary>
public sealed class AlmacenObjetosSimulado(TimeProvider reloj) : IAlmacenObjetos
{
    private readonly ConcurrentDictionary<string, byte[]> _objetos = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _firmas = new(StringComparer.Ordinal);

    public Task<string> UrlDeSubidaAsync(string clave, TimeSpan validez, CancellationToken ct)
    {
        var firma = Firmar(clave, validez);
        return Task.FromResult("memoria://subida/" + clave + "?firma=" + firma);
    }

    public Task<string> UrlDeDescargaAsync(string clave, TimeSpan validez, CancellationToken ct)
    {
        var firma = Firmar(clave, validez);
        return Task.FromResult("memoria://descarga/" + clave + "?firma=" + firma);
    }

    public Task<DatosObjeto?> InspeccionarAsync(string clave, CancellationToken ct)
    {
        if (!_objetos.TryGetValue(clave, out var contenido))
            return Task.FromResult<DatosObjeto?>(null);

        var tipo = DetectarTipo(contenido);
        var hash = Convert.ToHexStringLower(SHA256.HashData(contenido));

        return Task.FromResult<DatosObjeto?>(new DatosObjeto(tipo, contenido.Length, hash));
    }

    public Task EliminarAsync(string clave, CancellationToken ct)
    {
        _objetos.TryRemove(clave, out _);
        return Task.CompletedTask;
    }

    /// <summary>Simula que el cliente subió el archivo. Lo usan las pruebas.</summary>
    public void Colocar(string clave, byte[] contenido) => _objetos[clave] = contenido;

    /// <summary>
    /// ¿Esta firma sigue siendo válida? Es lo que permite probar que un enlace vencido se rechaza
    /// (QA-SEG-02) sin esperar minutos reales.
    /// </summary>
    public bool FirmaVigente(string firma) =>
        _firmas.TryGetValue(firma, out var vence) && vence > reloj.GetUtcNow();

    private string Firmar(string clave, TimeSpan validez)
    {
        var firma = Convert.ToHexStringLower(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(clave + reloj.GetUtcNow().ToUnixTimeMilliseconds())))[..32];

        _firmas[firma] = reloj.GetUtcNow().Add(validez);
        return firma;
    }

    /// <summary>
    /// Detecta el tipo por los **primeros bytes** del archivo, que es lo que hace el almacenamiento
    /// real y lo que exige ADR-0012: la extensión y lo que declara el cliente las controla quien
    /// sube el archivo.
    /// </summary>
    internal static string DetectarTipo(byte[] contenido)
    {
        if (contenido.Length >= 4 && contenido[0] == 0x25 && contenido[1] == 0x50 && contenido[2] == 0x44 && contenido[3] == 0x46)
            return "application/pdf";

        if (contenido.Length >= 3 && contenido[0] == 0xFF && contenido[1] == 0xD8 && contenido[2] == 0xFF)
            return "image/jpeg";

        if (contenido.Length >= 8 && contenido[0] == 0x89 && contenido[1] == 0x50 && contenido[2] == 0x4E && contenido[3] == 0x47)
            return "image/png";

        if (contenido.Length >= 12
            && contenido[0] == 0x52 && contenido[1] == 0x49 && contenido[2] == 0x46 && contenido[3] == 0x46
            && contenido[8] == 0x57 && contenido[9] == 0x45 && contenido[10] == 0x42 && contenido[11] == 0x50)
        {
            return "image/webp";
        }

        return "application/octet-stream";
    }
}
