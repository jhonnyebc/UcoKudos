using UcoKudos.Domain.Transversal;

namespace UcoKudos.Domain.Kudos;

/// <summary>
/// Categoría de reconocimiento. El enunciado fija quince como mínimo y restringe su administración
/// a tres roles (RF-06), que es lo que comprueba el servicio de la jerarquía.
///
/// Las quince se siembran por migración: una base recién levantada tiene que quedar usable.
/// </summary>
public sealed class Categoria
{
    private Categoria()
    {
        Nombre = null!;
        Descripcion = null!;
    }

    public Categoria(string nombre, string descripcion, Guid creadaPor, DateTimeOffset ahora)
    {
        Nombre = ExigirNombre(nombre);
        Descripcion = (descripcion ?? string.Empty).Trim();
        CreadaPor = creadaPor;
        Id = Guid.CreateVersion7(ahora);
        Activa = true;
        CreadaEn = ahora;
    }

    public Guid Id { get; private set; }
    public string Nombre { get; private set; }
    public string Descripcion { get; private set; }
    public bool Activa { get; private set; }
    public Guid CreadaPor { get; private set; }
    public DateTimeOffset CreadaEn { get; private set; }

    public void Renombrar(string nombre) => Nombre = ExigirNombre(nombre);

    public void CambiarDescripcion(string descripcion) => Descripcion = (descripcion ?? string.Empty).Trim();

    /// <summary>
    /// Desactivar en lugar de borrar: los Kudos ya publicados conservan su categoría, así que
    /// borrarla dejaría reconocimientos sin clasificar. Una categoría inactiva no se puede elegir
    /// al publicar, pero los Kudos existentes siguen mostrándola.
    /// </summary>
    public void Desactivar() => Activa = false;

    public void Activar() => Activa = true;

    private static string ExigirNombre(string? nombre)
    {
        var limpio = (nombre ?? string.Empty).Trim();
        return limpio.Length == 0
            ? throw new ExcepcionDominio("El nombre de la categoría es obligatorio.")
            : limpio;
    }

    /// <summary>
    /// Las quince categorías del enunciado, con su descripción. Se siembran en la migración
    /// inicial; el identificador se genera al sembrar.
    /// </summary>
    public static IReadOnlyList<(string Nombre, string Descripcion)> CatalogoInicial { get; } =
    [
        ("Rendimiento Académico", "Resultados académicos destacados y constancia en el estudio."),
        ("Mejor Investigador", "Aportes relevantes a la investigación y producción académica."),
        ("Empatía", "Capacidad de comprender y acompañar a los demás."),
        ("Comunicación Asertiva", "Expresa ideas con claridad y respeto, y escucha de verdad."),
        ("Orientación al Logro", "Persigue objetivos con determinación y los cumple."),
        ("Trabajo en Equipo", "Aporta al grupo y hace mejor el trabajo de los demás."),
        ("Trabajo Bajo Presión", "Mantiene la calidad y la calma cuando las condiciones aprietan."),
        ("Visión Estratégica", "Ve el panorama completo y anticipa lo que viene."),
        ("Innovación", "Propone soluciones nuevas a problemas conocidos."),
        ("Liderazgo", "Guía e inspira a otros hacia un propósito común."),
        ("Transformación Digital", "Impulsa el uso de la tecnología para mejorar procesos."),
        ("Mentor Inspirador", "Acompaña el crecimiento de otros y comparte lo que sabe."),
        ("Espíritu UCO", "Encarna los valores y la identidad de la Universidad Católica de Oriente."),
        ("Servicio a la Comunidad", "Pone su tiempo y capacidades al servicio de los demás."),
        ("Compromiso Institucional", "Responde con lealtad y dedicación a la institución.")
    ];
}
