using UcoKudos.Domain.Transversal;

namespace UcoKudos.Domain.Interaccion;

/// <summary>
/// Ruta materializada de una réplica dentro de su hilo (ADR-0011): la cadena con los identificadores
/// de todos sus ancestros, de la raíz hacia abajo.
///
/// Es un dato derivado de la referencia al padre, y existe por rendimiento: con un índice de prefijo
/// sobre esta columna, leer un hilo completo de cualquier profundidad es **una** consulta, el orden
/// por ruta ya es el orden anidado de presentación, y vetar en cascada es una sola operación.
///
/// Detalle que es fácil equivocar: los identificadores van con longitud fija y separador. Sin eso,
/// el prefijo de «/1/» coincidiría con «/12/» y el veto en cascada ocultaría contenido ajeno.
/// </summary>
public readonly record struct RutaReplica
{
    public const char Separador = '/';

    /// <summary>
    /// Profundidad máxima técnica. **No es un límite de negocio**: el enunciado dice «sin límite de
    /// profundidad» (RF-09) y se respeta, porque al alcanzarlo la réplica se acepta igual y se
    /// cuelga del último ancestro válido. Es una defensa para que una secuencia automatizada de
    /// respuestas a respuestas no degrade el índice.
    /// </summary>
    public const int ProfundidadMaxima = 64;

    private RutaReplica(string valor)
    {
        Valor = valor;
    }

    /// <summary>La ruta, con separador al principio y al final. Por ejemplo <c>/a1b2.../7f8e.../</c>.</summary>
    public string Valor { get; }

    /// <summary>Cuántos ancestros tiene, incluida ella misma. Una réplica de primer nivel vale 1.</summary>
    public short Profundidad => (short)(Valor.Count(c => c == Separador) - 1);

    /// <summary>Ruta de una réplica de primer nivel, que cuelga directamente del Kudo.</summary>
    public static RutaReplica DePrimerNivel(Guid replicaId) =>
        new(Separador + replicaId.ToString("N") + Separador);

    /// <summary>Ruta de una réplica que responde a otra: la del padre más la propia.</summary>
    public static RutaReplica Bajo(RutaReplica rutaDelPadre, Guid replicaId) =>
        new(rutaDelPadre.Valor + replicaId.ToString("N") + Separador);

    /// <summary>Reconstruye una ruta ya guardada.</summary>
    public static RutaReplica Desde(string valor)
    {
        var limpio = valor ?? string.Empty;
        return limpio.Length >= 2 && limpio[0] == Separador && limpio[^1] == Separador
            ? new RutaReplica(limpio)
            : throw new ExcepcionDominio("La ruta de réplica " + valor + " no tiene el formato esperado.");
    }

    /// <summary>
    /// Prefijo para buscar todos los descendientes. Es lo que usa la consulta del hilo y el veto en
    /// cascada: todas las réplicas cuya ruta empiece por esto.
    /// </summary>
    public string PrefijoDeDescendientes() => Valor;

    /// <summary>¿Esta ruta está por debajo de la otra?</summary>
    public bool EstaBajo(RutaReplica posibleAncestro) =>
        Valor.StartsWith(posibleAncestro.Valor, StringComparison.Ordinal) && Valor != posibleAncestro.Valor;

    public override string ToString() => Valor;
}
