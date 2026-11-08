using UcoKudos.Domain.Transversal;

namespace UcoKudos.Domain.Academico;

/// <summary>
/// Facultad de la universidad. Es el ámbito del Coordinador de Facultad (RF-04) y agrupa programas.
/// </summary>
public sealed class Facultad
{
    private Facultad()
    {
        Nombre = null!;
        Codigo = null!;
    }

    public Facultad(string nombre, string codigo, DateTimeOffset ahora)
    {
        Nombre = Texto.Exigir(nombre, "El nombre de la facultad es obligatorio.");
        Codigo = Texto.ExigirCodigo(codigo, "El código de la facultad es obligatorio.");
        Id = Guid.CreateVersion7(ahora);
        Activa = true;
    }

    public Guid Id { get; private set; }
    public string Nombre { get; private set; }
    public string Codigo { get; private set; }
    public bool Activa { get; private set; }

    public void Desactivar() => Activa = false;
    public void Activar() => Activa = true;
}

/// <summary>
/// Programa académico. Es el ámbito del Coordinador de Programa, del Docente y del Estudiante
/// (RF-04), y la unidad contra la que se comprueba si alguien manda sobre otra persona.
/// </summary>
public sealed class Programa
{
    private Programa()
    {
        Nombre = null!;
        Codigo = null!;
    }

    public Programa(Guid facultadId, string nombre, string codigo, DateTimeOffset ahora)
    {
        if (facultadId == Guid.Empty)
            throw new ExcepcionDominio("Un programa académico tiene que pertenecer a una facultad.");

        FacultadId = facultadId;
        Nombre = Texto.Exigir(nombre, "El nombre del programa es obligatorio.");
        Codigo = Texto.ExigirCodigo(codigo, "El código del programa es obligatorio.");
        Id = Guid.CreateVersion7(ahora);
        Activo = true;
    }

    public Guid Id { get; private set; }
    public Guid FacultadId { get; private set; }
    public string Nombre { get; private set; }
    public string Codigo { get; private set; }
    public bool Activo { get; private set; }

    public void Desactivar() => Activo = false;
    public void Activar() => Activo = true;
}

/// <summary>
/// Relación de una persona con un programa. Cubre los dos casos del enunciado: un estudiante puede
/// estudiar varios programas y un docente puede enseñar en varios (RF-05).
/// </summary>
public sealed class VinculoAcademico
{
    private VinculoAcademico()
    {
    }

    public VinculoAcademico(Guid personaId, Guid programaId, TipoVinculo tipo)
    {
        if (personaId == Guid.Empty || programaId == Guid.Empty)
            throw new ExcepcionDominio("Un vínculo académico necesita la persona y el programa.");

        PersonaId = personaId;
        ProgramaId = programaId;
        Tipo = tipo;
        Activo = true;
    }

    public Guid PersonaId { get; private set; }
    public Guid ProgramaId { get; private set; }
    public TipoVinculo Tipo { get; private set; }
    public bool Activo { get; private set; }

    public void Desactivar() => Activo = false;
}

/// <summary>Qué hace la persona en el programa.</summary>
public enum TipoVinculo
{
    /// <summary>Lo estudia (RF-05).</summary>
    Matricula = 1,

    /// <summary>Enseña en él (RF-05).</summary>
    Docencia = 2
}

/// <summary>Validaciones de texto compartidas por las entidades del dominio.</summary>
internal static class Texto
{
    internal static string Exigir(string? valor, string mensajeSiFalta)
    {
        var limpio = (valor ?? string.Empty).Trim();
        return limpio.Length == 0 ? throw new ExcepcionDominio(mensajeSiFalta) : limpio;
    }

    internal static string ExigirCodigo(string? valor, string mensajeSiFalta) =>
        Exigir(valor, mensajeSiFalta).ToUpperInvariant();
}
