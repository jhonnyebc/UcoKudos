using UcoKudos.Domain.Transversal;

namespace UcoKudos.Domain.Interaccion;

/// <summary>
/// Réplica sobre un Kudo o sobre otra réplica (RF-09): sin límite de profundidad, de cantidad ni de
/// longitud.
///
/// Guarda dos cosas complementarias (ADR-0011): la referencia al padre, que es la verdad de la
/// estructura y da integridad referencial, y la ruta materializada, que es lo que permite leer el
/// hilo completo en una consulta. La ruta se calcula **aquí**, en un solo lugar, porque es el único
/// modo de que no se desincronice con el padre.
/// </summary>
public sealed class Replica
{
    private Replica()
    {
        Contenido = null!;
        Ruta = null!;
    }

    private Replica(Guid kudoId, Guid? replicaPadreId, Guid autorId, string contenido, RutaReplica ruta, DateTimeOffset ahora)
    {
        Id = ruta.Valor.Length > 0 ? ExtraerIdDe(ruta) : Guid.CreateVersion7(ahora);
        KudoId = kudoId;
        ReplicaPadreId = replicaPadreId;
        AutorId = autorId;
        Contenido = contenido;
        Ruta = ruta.Valor;
        Profundidad = ruta.Profundidad;
        PublicadaEn = ahora;
    }

    public Guid Id { get; private set; }
    public Guid KudoId { get; private set; }

    /// <summary>Null en una réplica de primer nivel, que cuelga directamente del Kudo.</summary>
    public Guid? ReplicaPadreId { get; private set; }

    public Guid AutorId { get; private set; }
    public string Contenido { get; private set; }

    /// <summary>Ruta materializada. Dato derivado del padre, mantenido para leer rápido.</summary>
    public string Ruta { get; private set; }

    public short Profundidad { get; private set; }
    public bool Vetado { get; private set; }
    public DateTimeOffset PublicadaEn { get; private set; }

    /// <summary>Contadores materializados, igual que en el Kudo (ADR-0010).</summary>
    public int TotalReplicas { get; private set; }

    public decimal? PromedioCalificacion { get; private set; }
    public int TotalCalificaciones { get; private set; }

    public RutaReplica RutaMaterializada => RutaReplica.Desde(Ruta);

    /// <summary>Réplica directa sobre un Kudo.</summary>
    public static Replica SobreKudo(Guid kudoId, Guid autorId, string contenido, DateTimeOffset ahora)
    {
        ExigirDatos(kudoId, autorId, contenido);
        var id = Guid.CreateVersion7(ahora);
        return new Replica(kudoId, null, autorId, Limpiar(contenido), RutaReplica.DePrimerNivel(id), ahora);
    }

    /// <summary>
    /// Réplica sobre otra réplica. Si el padre ya está en la profundidad máxima técnica, la nueva
    /// se cuelga de él pero conserva su ruta: se acepta igual, que es lo que exige «sin límite de
    /// profundidad» (RF-09).
    /// </summary>
    public static Replica SobreReplica(Replica padre, Guid autorId, string contenido, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(padre);
        ExigirDatos(padre.KudoId, autorId, contenido);

        if (padre.Vetado)
            throw new ExcepcionDominio("No se puede responder a una réplica vetada.");

        var id = Guid.CreateVersion7(ahora);
        var rutaDelPadre = padre.RutaMaterializada;

        // Al alcanzar la profundidad máxima técnica la réplica no se rechaza: se queda al nivel del
        // padre en lugar de bajar otro, para no degradar el índice de prefijo.
        var ruta = rutaDelPadre.Profundidad >= RutaReplica.ProfundidadMaxima
            ? RutaReplica.Bajo(RutaReplica.Desde(RecortarUltimoNivel(rutaDelPadre)), id)
            : RutaReplica.Bajo(rutaDelPadre, id);

        return new Replica(padre.KudoId, padre.Id, autorId, Limpiar(contenido), ruta, ahora);
    }

    public void Vetar()
    {
        if (Vetado)
            throw new ExcepcionDominio("Esta réplica ya estaba vetada.");

        Vetado = true;
    }

    public void LevantarVeto() => Vetado = false;

    public void FijarTotalReplicas(int total)
    {
        if (total < 0)
            throw new ExcepcionDominio("El total de réplicas no puede ser negativo.");

        TotalReplicas = total;
    }

    public void FijarCalificacion(int totalCalificaciones, decimal? promedio)
    {
        if (totalCalificaciones < 0)
            throw new ExcepcionDominio("El total de calificaciones no puede ser negativo.");
        if (promedio is < 1 or > 5)
            throw new ExcepcionDominio("El promedio de calificación tiene que estar entre 1 y 5.");

        TotalCalificaciones = totalCalificaciones;
        PromedioCalificacion = totalCalificaciones == 0 ? null : promedio;
    }

    private static void ExigirDatos(Guid kudoId, Guid autorId, string? contenido)
    {
        if (kudoId == Guid.Empty)
            throw new ExcepcionDominio("Una réplica tiene que pertenecer a un Kudo.");
        if (autorId == Guid.Empty)
            throw new ExcepcionDominio("Una réplica necesita un autor.");
        if (string.IsNullOrWhiteSpace(contenido))
            throw new ExcepcionDominio("El contenido de la réplica es obligatorio.");
    }

    private static string Limpiar(string contenido) => contenido.Trim();

    private static Guid ExtraerIdDe(RutaReplica ruta)
    {
        var partes = ruta.Valor.Split(RutaReplica.Separador, StringSplitOptions.RemoveEmptyEntries);
        return Guid.ParseExact(partes[^1], "N");
    }

    private static string RecortarUltimoNivel(RutaReplica ruta)
    {
        var partes = ruta.Valor.Split(RutaReplica.Separador, StringSplitOptions.RemoveEmptyEntries);
        return RutaReplica.Separador + string.Join(RutaReplica.Separador, partes[..^1]) + RutaReplica.Separador;
    }
}
