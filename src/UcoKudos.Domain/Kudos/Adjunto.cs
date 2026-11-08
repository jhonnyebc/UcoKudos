using UcoKudos.Domain.Transversal;

namespace UcoKudos.Domain.Kudos;

/// <summary>
/// Metadatos de un archivo adjunto a un Kudo (RF-08, DRV-004). El archivo **no** está aquí: vive en
/// el almacenamiento de objetos (ADR-0012). Esta entidad guarda lo necesario para servirlo, auditar
/// su uso y verificar su integridad.
///
/// La separación importa: un archivo en una columna binaria infla los respaldos, llena la memoria
/// caché del motor con datos que nadie consulta y hace que cada lectura del Kudo arriesgue arrastrar
/// megabytes.
/// </summary>
public sealed class Adjunto
{
    /// <summary>Tipos permitidos. Se comprueba contra el contenido real del archivo, no la extensión.</summary>
    public static readonly IReadOnlySet<string> TiposPermitidos =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp", "application/pdf" };

    public const long TamanoMaximoBytes = 5 * 1024 * 1024;
    public const int MaximoPorKudo = 5;

    private Adjunto()
    {
        ClaveObjeto = null!;
        NombreOriginal = null!;
    }

    internal Adjunto(Guid kudoId, string claveObjeto, string nombreOriginal, Guid cargadoPor, DateTimeOffset ahora)
    {
        ClaveObjeto = (claveObjeto ?? string.Empty).Trim();
        if (ClaveObjeto.Length == 0)
            throw new ExcepcionDominio("El adjunto necesita su clave en el almacenamiento.");

        NombreOriginal = (nombreOriginal ?? string.Empty).Trim();
        if (NombreOriginal.Length == 0)
            throw new ExcepcionDominio("El adjunto necesita un nombre.");

        Id = Guid.CreateVersion7(ahora);
        KudoId = kudoId;
        CargadoPor = cargadoPor;
        CargadoEn = ahora;
    }

    public Guid Id { get; private set; }
    public Guid KudoId { get; private set; }

    /// <summary>
    /// Nombre del objeto en el almacenamiento. Es un identificador **generado**, nunca el nombre que
    /// puso el usuario: un nombre de archivo controlado por quien lo sube es un vector de recorrido
    /// de rutas y de colisiones.
    /// </summary>
    public string ClaveObjeto { get; private set; }

    /// <summary>Lo que escribió el usuario. Solo para mostrarlo, nunca para construir rutas.</summary>
    public string NombreOriginal { get; private set; }

    /// <summary>Tipo detectado del contenido real. Null hasta que se confirma la carga.</summary>
    public string? TipoDetectado { get; private set; }

    public long TamanoBytes { get; private set; }

    /// <summary>Hash del contenido: verifica integridad al restaurar y detecta duplicados.</summary>
    public string? HashContenido { get; private set; }

    public Guid CargadoPor { get; private set; }
    public DateTimeOffset CargadoEn { get; private set; }

    /// <summary>La carga terminó y el contenido se verificó. Solo los confirmados se sirven.</summary>
    public bool Confirmado { get; private set; }

    /// <summary>
    /// Marcado, no borrado. Un borrado accidental es reversible y la limpieza definitiva del objeto
    /// es un proceso aparte (ADR-0012).
    /// </summary>
    public bool Eliminado { get; private set; }

    /// <summary>
    /// Confirma la carga tras leer el contenido real del objeto. Aquí se rechaza un archivo cuyo
    /// tipo real no está permitido, aunque la extensión y lo que declaró el cliente dijeran otra
    /// cosa: las dos las controla quien sube el archivo.
    /// </summary>
    public void Confirmar(string tipoDetectado, long tamanoBytes, string hashContenido)
    {
        if (Confirmado)
            throw new ExcepcionDominio("Este adjunto ya estaba confirmado.");

        var tipo = (tipoDetectado ?? string.Empty).Trim();
        if (!TiposPermitidos.Contains(tipo))
            throw new ExcepcionDominio("El tipo de archivo " + tipo + " no está permitido.");

        if (tamanoBytes <= 0)
            throw new ExcepcionDominio("El adjunto está vacío.");
        if (tamanoBytes > TamanoMaximoBytes)
            throw new ExcepcionDominio("El adjunto supera el tamaño máximo de " + (TamanoMaximoBytes / 1024 / 1024) + " MB.");

        var hash = (hashContenido ?? string.Empty).Trim();
        if (hash.Length == 0)
            throw new ExcepcionDominio("El adjunto necesita el hash de su contenido.");

        TipoDetectado = tipo;
        TamanoBytes = tamanoBytes;
        HashContenido = hash;
        Confirmado = true;
    }

    internal void MarcarEliminado() => Eliminado = true;

    /// <summary>Se puede servir: confirmado y no eliminado.</summary>
    public bool EsServible => Confirmado && !Eliminado;
}
