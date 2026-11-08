using UcoKudos.Domain.Transversal;

namespace UcoKudos.Domain.Kudos;

/// <summary>
/// Reconocimiento publicado por una persona para otra. Es el agregado central de la plataforma y el
/// sujeto de la funcionalidad obligatoria (RF-07).
///
/// Lleva los tres contadores materializados de ADR-0010 (total de réplicas, promedio de
/// calificación y total de calificaciones). Son datos derivados que se mantienen aquí, dentro de la
/// misma transacción que la réplica o la calificación que los cambia, porque calcularlos al leer
/// convertiría cada fila del muro en una agregación sobre todo el árbol de réplicas.
/// </summary>
public sealed class Kudo
{
    private readonly List<Adjunto> _adjuntos = [];

    private Kudo()
    {
        Contenido = null!;
    }

    public Kudo(Guid autorId, Guid destinatarioId, Guid categoriaId, string contenido, DateTimeOffset ahora)
    {
        if (autorId == Guid.Empty)
            throw new ExcepcionDominio("Un Kudo necesita un autor.");
        if (destinatarioId == Guid.Empty)
            throw new ExcepcionDominio("Un Kudo necesita un destinatario.");
        if (autorId == destinatarioId)
            throw new ExcepcionDominio("No se puede publicar un Kudo para uno mismo.");
        if (categoriaId == Guid.Empty)
            throw new ExcepcionDominio("Un Kudo necesita una categoría.");

        // Sin límite de longitud (RN-03), pero no vacío: un reconocimiento sin contenido no dice nada.
        Contenido = (contenido ?? string.Empty).Trim();
        if (Contenido.Length == 0)
            throw new ExcepcionDominio("El contenido del Kudo es obligatorio.");

        Id = Guid.CreateVersion7(ahora);
        AutorId = autorId;
        DestinatarioId = destinatarioId;
        CategoriaId = categoriaId;
        PublicadoEn = ahora;
    }

    public Guid Id { get; private set; }
    public Guid AutorId { get; private set; }
    public Guid DestinatarioId { get; private set; }
    public Guid CategoriaId { get; private set; }
    public string Contenido { get; private set; }
    public bool Vetado { get; private set; }
    public DateTimeOffset PublicadoEn { get; private set; }

    /// <summary>Contador materializado (ADR-0010): evita agregar sobre el árbol de réplicas al leer.</summary>
    public int TotalReplicas { get; private set; }

    /// <summary>Contador materializado (ADR-0010). Null mientras nadie haya calificado.</summary>
    public decimal? PromedioCalificacion { get; private set; }

    public int TotalCalificaciones { get; private set; }

    public IReadOnlyList<Adjunto> Adjuntos => _adjuntos;

    /// <summary>
    /// Token de concurrencia optimista (ADR-0005). Lo administra la capa de persistencia mapeándolo
    /// a la columna de sistema del motor; el dominio solo lo expone para que exista la propiedad.
    /// </summary>
    public uint Version { get; private set; }

    public Adjunto AgregarAdjunto(string claveObjeto, string nombreOriginal, Guid cargadoPor, DateTimeOffset ahora)
    {
        if (_adjuntos.Count >= Adjunto.MaximoPorKudo)
            throw new ExcepcionDominio("Un Kudo admite como máximo " + Adjunto.MaximoPorKudo + " adjuntos.");

        var adjunto = new Adjunto(Id, claveObjeto, nombreOriginal, cargadoPor, ahora);
        _adjuntos.Add(adjunto);
        return adjunto;
    }

    public void QuitarAdjunto(Guid adjuntoId)
    {
        var adjunto = _adjuntos.FirstOrDefault(a => a.Id == adjuntoId)
                      ?? throw new ExcepcionDominio("Este Kudo no tiene el adjunto indicado.");
        adjunto.MarcarEliminado();
    }

    /// <summary>Vetar oculta el Kudo del muro (RF-12). No lo borra: la trazabilidad se conserva.</summary>
    public void Vetar()
    {
        if (Vetado)
            throw new ExcepcionDominio("Este Kudo ya estaba vetado.");

        Vetado = true;
    }

    public void LevantarVeto() => Vetado = false;

    /// <summary>
    /// Mantiene el contador de réplicas. Lo llama el caso de uso que crea o veta una réplica, en la
    /// misma transacción, que es lo que evita que el contador se desfase.
    /// </summary>
    public void FijarTotalReplicas(int total)
    {
        if (total < 0)
            throw new ExcepcionDominio("El total de réplicas no puede ser negativo.");

        TotalReplicas = total;
    }

    /// <summary>Mantiene los contadores de calificación (RF-10).</summary>
    public void FijarCalificacion(int totalCalificaciones, decimal? promedio)
    {
        if (totalCalificaciones < 0)
            throw new ExcepcionDominio("El total de calificaciones no puede ser negativo.");
        if (promedio is < 1 or > 5)
            throw new ExcepcionDominio("El promedio de calificación tiene que estar entre 1 y 5.");

        TotalCalificaciones = totalCalificaciones;
        PromedioCalificacion = totalCalificaciones == 0 ? null : promedio;
    }
}
