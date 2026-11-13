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
/// Reproduce lo que importa del comportamiento real: guardar y leer devuelven lo mismo, un objeto
/// inexistente se lee como nulo, y la inspección devuelve el tipo detectado del contenido y no el
/// declarado.
/// </summary>
public sealed class AlmacenObjetosSimulado : IAlmacenObjetos
{
    private readonly ConcurrentDictionary<string, byte[]> _objetos = new(StringComparer.Ordinal);

    public Task GuardarAsync(string clave, byte[] contenido, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contenido);

        // Se copia: quien llamó podría reutilizar su arreglo, y un objeto guardado no debe cambiar
        // por eso.
        _objetos[clave] = (byte[])contenido.Clone();
        return Task.CompletedTask;
    }

    public Task<byte[]?> LeerAsync(string clave, CancellationToken ct) =>
        Task.FromResult(_objetos.TryGetValue(clave, out var contenido) ? (byte[]?)contenido.Clone() : null);

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
