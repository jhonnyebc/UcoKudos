using Microsoft.EntityFrameworkCore;
using UcoKudos.Domain.Academico;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Interaccion;
using UcoKudos.Domain.Kudos;
using UcoKudos.Infrastructure.Persistencia.Outbox;

namespace UcoKudos.Infrastructure.Persistencia;

/// <summary>
/// Contexto de persistencia (ADR-0003).
///
/// El mapeo es **explícito** y vive aquí, en infraestructura: las entidades del dominio no llevan ni
/// una anotación del ORM (ADR-0001, regla 1). Eso es lo que permite probar las invariantes sin
/// levantar una base de datos, y es la razón por la que esta clase es más larga de lo que sería con
/// atributos.
///
/// Convenciones: nombres en snake_case, instantes en UTC, enumeraciones como texto y cuatro esquemas
/// por responsabilidad.
/// </summary>
public sealed class UcoKudosDbContext(DbContextOptions<UcoKudosDbContext> opciones) : DbContext(opciones)
{
    public const string EsquemaNucleo = "nucleo";
    public const string EsquemaInteraccion = "interaccion";
    public const string EsquemaNotificaciones = "notif";
    public const string EsquemaAuditoria = "audit";

    public DbSet<Persona> Personas => Set<Persona>();
    public DbSet<AsignacionRol> AsignacionesRol => Set<AsignacionRol>();
    public DbSet<VerificacionContacto> VerificacionesContacto => Set<VerificacionContacto>();
    public DbSet<Facultad> Facultades => Set<Facultad>();
    public DbSet<Programa> Programas => Set<Programa>();
    public DbSet<VinculoAcademico> VinculosAcademicos => Set<VinculoAcademico>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Kudo> Kudos => Set<Kudo>();
    public DbSet<Adjunto> Adjuntos => Set<Adjunto>();
    public DbSet<Replica> Replicas => Set<Replica>();
    public DbSet<Calificacion> Calificaciones => Set<Calificacion>();
    public DbSet<Reporte> Reportes => Set<Reporte>();
    public DbSet<Veto> Vetos => Set<Veto>();
    public DbSet<EventoAuditoria> EventosAuditoria => Set<EventoAuditoria>();
    public DbSet<MensajeOutbox> MensajesOutbox => Set<MensajeOutbox>();
    public DbSet<RegistroNotificacion> RegistrosNotificacion => Set<RegistroNotificacion>();
    public DbSet<SuscripcionPushFila> SuscripcionesPush => Set<SuscripcionPushFila>();
    public DbSet<AvisoFila> Avisos => Set<AvisoFila>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        ArgumentNullException.ThrowIfNull(modelo);

        modelo.HasDefaultSchema(EsquemaNucleo);
        modelo.ApplyConfigurationsFromAssembly(typeof(UcoKudosDbContext).Assembly);

        base.OnModelCreating(modelo);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configuracion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);

        // Npgsql solo admite desfase cero en su tipo de instante con zona, así que todo instante se
        // guarda en UTC. Este convertidor global evita tener que recordarlo en cada propiedad.
        configuracion.Properties<DateTimeOffset>().HaveConversion<ConvertidorInstanteUtc>();

        base.ConfigureConventions(configuracion);
    }
}

/// <summary>
/// Normaliza todo instante a UTC al guardarlo. Sin esto, un instante con desfase distinto de cero
/// hace fallar la escritura en PostgreSQL, y el error aparece lejos de donde está la causa.
/// </summary>
internal sealed class ConvertidorInstanteUtc()
    : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, DateTimeOffset>(
        aGuardar => aGuardar.ToUniversalTime(),
        alLeer => alLeer.ToUniversalTime());
