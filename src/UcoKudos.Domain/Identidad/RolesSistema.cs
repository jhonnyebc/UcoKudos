namespace UcoKudos.Domain.Identidad;

/// <summary>Ámbito sobre el que se ejerce un rol. Determina qué identificador debe acompañarlo.</summary>
public enum AmbitoRol
{
    /// <summary>Toda la universidad: Administrador, Rector y Coordinador Académico.</summary>
    Universidad = 1,
    Facultad = 2,
    Programa = 3
}

/// <summary>Estado de una cuenta. Solo <see cref="Activa"/> permite usar la plataforma.</summary>
public enum EstadoCuenta
{
    /// <summary>Registrada, sin aprobar o sin verificar correo o celular.</summary>
    Pendiente = 1,
    Activa = 2,
    Inactiva = 3,
    Rechazada = 4
}

/// <summary>
/// Catálogo de los siete roles del enunciado. Es un catálogo y no una enumeración suelta porque
/// cada rol lleva datos que las reglas consultan: su nivel en la jerarquía (RF-03) y sus dos
/// banderas de autorización (RF-06 y RF-12).
///
/// Tener esos datos aquí, y no como condicionales repartidos por los casos de uso, es lo que
/// permite probar la jerarquía con pruebas unitarias rápidas (ADR-0004).
/// </summary>
public sealed record Rol
{
    private Rol(short id, string nombre, short nivel, AmbitoRol ambito, bool administraCategorias, bool puedeVetar)
    {
        Id = id;
        Nombre = nombre;
        Nivel = nivel;
        Ambito = ambito;
        AdministraCategorias = administraCategorias;
        PuedeVetar = puedeVetar;
    }

    public short Id { get; }
    public string Nombre { get; }

    /// <summary>Nivel en la jerarquía: mayor es más alto. Determina quién aprueba a quién (RF-03).</summary>
    public short Nivel { get; }

    public AmbitoRol Ambito { get; }

    /// <summary>RF-06: solo Administrador, Rector y Coordinador Académico.</summary>
    public bool AdministraCategorias { get; }

    /// <summary>RF-12: solo Rector y Coordinador Académico. El Administrador NO veta.</summary>
    public bool PuedeVetar { get; }

    public static readonly Rol Administrador = new(1, "Administrador del Sistema", 7, AmbitoRol.Universidad, administraCategorias: true, puedeVetar: false);
    public static readonly Rol Rector = new(2, "Rector", 6, AmbitoRol.Universidad, administraCategorias: true, puedeVetar: true);
    public static readonly Rol CoordinadorAcademico = new(3, "Coordinador Académico", 5, AmbitoRol.Universidad, administraCategorias: true, puedeVetar: true);
    public static readonly Rol CoordinadorFacultad = new(4, "Coordinador de Facultad", 4, AmbitoRol.Facultad, administraCategorias: false, puedeVetar: false);
    public static readonly Rol CoordinadorPrograma = new(5, "Coordinador de Programa", 3, AmbitoRol.Programa, administraCategorias: false, puedeVetar: false);
    public static readonly Rol Docente = new(6, "Docente", 2, AmbitoRol.Programa, administraCategorias: false, puedeVetar: false);
    public static readonly Rol Estudiante = new(7, "Estudiante", 1, AmbitoRol.Programa, administraCategorias: false, puedeVetar: false);

    public static IReadOnlyList<Rol> Todos { get; } =
    [
        Administrador, Rector, CoordinadorAcademico, CoordinadorFacultad, CoordinadorPrograma, Docente, Estudiante
    ];

    public static Rol Por(short id) =>
        Todos.FirstOrDefault(r => r.Id == id)
        ?? throw new Transversal.ExcepcionDominio("No existe un rol con el identificador " + id + ".");

    /// <summary>
    /// Los roles de los que la universidad tiene uno solo activo, sin importar el ámbito (RF-04).
    /// La unicidad por facultad y por programa se resuelve con el ámbito, no aquí.
    /// </summary>
    public bool EsUnicoEnLaUniversidad => Ambito == AmbitoRol.Universidad && Id != Administrador.Id;
}
