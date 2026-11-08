using UcoKudos.Domain.Transversal;

namespace UcoKudos.Domain.Interaccion;

/// <summary>Qué clase de contenido se califica, reporta o veta. Kudos y réplicas (RF-10 a RF-12).</summary>
public enum TipoContenido
{
    Kudo = 1,
    Replica = 2
}

/// <summary>
/// Calificación de un Kudo o de una réplica, de 1 a 5 estrellas (RF-10).
///
/// El enunciado exige que un usuario tenga **una** calificación activa sobre el mismo contenido.
/// Eso se defiende con un índice único parcial en la base (ADR-0003); aquí se modela cambiándola en
/// lugar de editarla, de modo que quede el histórico de lo que alguien opinó antes.
/// </summary>
public sealed class Calificacion
{
    public const short Minimo = 1;
    public const short Maximo = 5;

    private Calificacion()
    {
    }

    public Calificacion(Guid personaId, TipoContenido tipoContenido, Guid contenidoId, short estrellas, DateTimeOffset ahora)
    {
        if (personaId == Guid.Empty)
            throw new ExcepcionDominio("Una calificación necesita quien la otorga.");
        if (contenidoId == Guid.Empty)
            throw new ExcepcionDominio("Una calificación necesita el contenido calificado.");
        if (estrellas is < Minimo or > Maximo)
            throw new ExcepcionDominio("La calificación tiene que estar entre " + Minimo + " y " + Maximo + " estrellas.");

        Id = Guid.CreateVersion7(ahora);
        PersonaId = personaId;
        TipoContenido = tipoContenido;
        ContenidoId = contenidoId;
        Estrellas = estrellas;
        Activa = true;
        CalificadaEn = ahora;
    }

    public Guid Id { get; private set; }
    public Guid PersonaId { get; private set; }
    public TipoContenido TipoContenido { get; private set; }
    public Guid ContenidoId { get; private set; }
    public short Estrellas { get; private set; }
    public bool Activa { get; private set; }
    public DateTimeOffset CalificadaEn { get; private set; }

    /// <summary>
    /// Desactiva esta calificación porque la persona cambió de opinión. No se edita el valor: se
    /// desactiva y se crea otra, así queda el rastro.
    /// </summary>
    public void Reemplazar()
    {
        if (!Activa)
            throw new ExcepcionDominio("Esta calificación ya no estaba activa.");

        Activa = false;
    }
}

/// <summary>
/// Reporte de contenido inapropiado. Todos los usuarios pueden reportar (RF-11); vetar es otra cosa
/// y la restringe RF-12 a dos roles.
/// </summary>
public sealed class Reporte
{
    private Reporte()
    {
        Motivo = null!;
    }

    public Reporte(Guid personaId, TipoContenido tipoContenido, Guid contenidoId, string motivo, DateTimeOffset ahora)
    {
        if (personaId == Guid.Empty)
            throw new ExcepcionDominio("Un reporte necesita quien lo presenta.");
        if (contenidoId == Guid.Empty)
            throw new ExcepcionDominio("Un reporte necesita el contenido reportado.");

        Motivo = (motivo ?? string.Empty).Trim();
        if (Motivo.Length == 0)
            throw new ExcepcionDominio("El motivo del reporte es obligatorio.");

        Id = Guid.CreateVersion7(ahora);
        PersonaId = personaId;
        TipoContenido = tipoContenido;
        ContenidoId = contenidoId;
        Estado = EstadoReporte.Pendiente;
        PresentadoEn = ahora;
    }

    public Guid Id { get; private set; }
    public Guid PersonaId { get; private set; }
    public TipoContenido TipoContenido { get; private set; }
    public Guid ContenidoId { get; private set; }
    public string Motivo { get; private set; }
    public EstadoReporte Estado { get; private set; }
    public DateTimeOffset PresentadoEn { get; private set; }

    public void Resolver(bool seVeto)
    {
        if (Estado != EstadoReporte.Pendiente)
            throw new ExcepcionDominio("Este reporte ya estaba resuelto.");

        Estado = seVeto ? EstadoReporte.Aceptado : EstadoReporte.Descartado;
    }
}

public enum EstadoReporte
{
    Pendiente = 1,
    Aceptado = 2,
    Descartado = 3
}

/// <summary>
/// Veto de un contenido (RF-12). Solo Rector y Coordinador Académico, que lo comprueba el servicio
/// de la jerarquía.
///
/// Vetar una réplica oculta también a sus descendientes (ADR-0011): no tendría sentido ocultar un
/// comentario y dejar visibles las respuestas que lo citan. <see cref="ContenidosAfectados"/> deja
/// constancia de cuántos se ocultaron.
/// </summary>
public sealed class Veto
{
    private Veto()
    {
        Motivo = null!;
    }

    public Veto(Guid aplicadoPor, TipoContenido tipoContenido, Guid contenidoId, string motivo, int contenidosAfectados, DateTimeOffset ahora)
    {
        if (aplicadoPor == Guid.Empty)
            throw new ExcepcionDominio("Un veto necesita quién lo aplica.");
        if (contenidoId == Guid.Empty)
            throw new ExcepcionDominio("Un veto necesita el contenido vetado.");

        Motivo = (motivo ?? string.Empty).Trim();
        if (Motivo.Length == 0)
            throw new ExcepcionDominio("El motivo del veto es obligatorio.");

        Id = Guid.CreateVersion7(ahora);
        AplicadoPor = aplicadoPor;
        TipoContenido = tipoContenido;
        ContenidoId = contenidoId;
        ContenidosAfectados = contenidosAfectados;
        EnCascada = contenidosAfectados > 1;
        AplicadoEn = ahora;
    }

    public Guid Id { get; private set; }
    public Guid AplicadoPor { get; private set; }
    public TipoContenido TipoContenido { get; private set; }
    public Guid ContenidoId { get; private set; }
    public string Motivo { get; private set; }
    public int ContenidosAfectados { get; private set; }
    public bool EnCascada { get; private set; }
    public DateTimeOffset AplicadoEn { get; private set; }
}
