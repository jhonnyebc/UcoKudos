using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Identidad;
using UcoKudos.Infrastructure.Persistencia.Outbox;

namespace UcoKudos.Infrastructure.Persistencia.Configuraciones;

/// <summary>
/// Mapeo de la bandeja de salida (ADR-0005). El índice parcial es el que atiende la vuelta del
/// proceso: solo los eventos pendientes cuyo próximo intento ya llegó.
/// </summary>
internal sealed class ConfiguracionMensajeOutbox : IEntityTypeConfiguration<MensajeOutbox>
{
    public void Configure(EntityTypeBuilder<MensajeOutbox> entidad)
    {
        entidad.ToTable("outbox_mensaje", UcoKudosDbContext.EsquemaNotificaciones);
        entidad.HasKey(m => m.Id);

        entidad.Property(m => m.Id).HasColumnName("id");
        entidad.Property(m => m.EventoId).HasColumnName("evento_id");
        entidad.Property(m => m.TipoEvento).HasColumnName("tipo_evento").HasMaxLength(100).IsRequired();
        entidad.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        entidad.Property(m => m.OcurridoEn).HasColumnName("ocurrido_en");
        entidad.Property(m => m.ProximoIntento).HasColumnName("proximo_intento");
        entidad.Property(m => m.Intentos).HasColumnName("intentos");
        entidad.Property(m => m.UltimoError).HasColumnName("ultimo_error").HasMaxLength(500);
        entidad.Property(m => m.ContextoTraza).HasColumnName("contexto_traza").HasMaxLength(100);
        entidad.Property(m => m.ProcesadoEn).HasColumnName("procesado_en");

        // La consulta de la vuelta del proceso: pendientes listos, en orden de próximo intento.
        entidad.HasIndex(m => new { m.ProximoIntento, m.OcurridoEn })
            .HasFilter("procesado_en IS NULL")
            .HasDatabaseName("ix_outbox_pendientes");

        entidad.HasIndex(m => m.EventoId).HasDatabaseName("ix_outbox_evento");
    }
}

/// <summary>
/// Mapeo del registro de notificaciones. **El índice único es la idempotencia** (QA-CONF-02): la
/// combinación de evento, destinatario y canal no se puede repetir, así que reprocesar un evento no
/// produce un segundo correo ni un segundo WhatsApp.
/// </summary>
internal sealed class ConfiguracionRegistroNotificacion : IEntityTypeConfiguration<RegistroNotificacion>
{
    public void Configure(EntityTypeBuilder<RegistroNotificacion> entidad)
    {
        entidad.ToTable("notificacion", UcoKudosDbContext.EsquemaNotificaciones);
        entidad.HasKey(n => n.Id);

        entidad.Property(n => n.Id).HasColumnName("id");
        entidad.Property(n => n.EventoId).HasColumnName("evento_id");
        entidad.Property(n => n.DestinatarioId).HasColumnName("destinatario_id");
        entidad.Property(n => n.Canal).HasColumnName("canal").HasMaxLength(20).IsRequired();
        entidad.Property(n => n.ClaveMensaje).HasColumnName("clave_mensaje").HasMaxLength(100).IsRequired();
        entidad.Property(n => n.Estado).HasColumnName("estado").HasMaxLength(20).IsRequired();
        entidad.Property(n => n.IdExterno).HasColumnName("id_externo").HasMaxLength(200);
        entidad.Property(n => n.Error).HasColumnName("error").HasMaxLength(500);
        entidad.Property(n => n.EnviadaEn).HasColumnName("enviada_en");

        entidad.HasIndex(n => new { n.EventoId, n.DestinatarioId, n.Canal })
            .IsUnique()
            .HasDatabaseName("ux_notificacion_idempotencia");

        entidad.HasOne<Persona>().WithMany().HasForeignKey(n => n.DestinatarioId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ConfiguracionSuscripcionPush : IEntityTypeConfiguration<SuscripcionPushFila>
{
    public void Configure(EntityTypeBuilder<SuscripcionPushFila> entidad)
    {
        entidad.ToTable("suscripcion_push", UcoKudosDbContext.EsquemaNotificaciones);
        entidad.HasKey(s => s.Id);

        entidad.Property(s => s.Id).HasColumnName("id");
        entidad.Property(s => s.PersonaId).HasColumnName("persona_id");
        entidad.Property(s => s.Endpoint).HasColumnName("endpoint").HasMaxLength(500).IsRequired();
        entidad.Property(s => s.ClaveP256dh).HasColumnName("clave_p256dh").HasMaxLength(200).IsRequired();
        entidad.Property(s => s.ClaveAuth).HasColumnName("clave_auth").HasMaxLength(100).IsRequired();
        entidad.Property(s => s.Activa).HasColumnName("activa");
        entidad.Property(s => s.RegistradaEn).HasColumnName("registrada_en");

        // Un navegador registra un endpoint único: si vuelve a suscribirse, es el mismo.
        entidad.HasIndex(s => s.Endpoint).IsUnique().HasDatabaseName("ux_suscripcion_endpoint");

        entidad.HasIndex(s => s.PersonaId)
            .HasFilter("activa")
            .HasDatabaseName("ix_suscripcion_persona_activa");

        entidad.HasOne<Persona>().WithMany().HasForeignKey(s => s.PersonaId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Mapeo del aviso persistido. La clave es una **secuencia numérica** y no un identificador
/// aleatorio, porque el envío en vivo necesita saber qué avisos son posteriores al último entregado.
/// </summary>
internal sealed class ConfiguracionAviso : IEntityTypeConfiguration<AvisoFila>
{
    public void Configure(EntityTypeBuilder<AvisoFila> entidad)
    {
        entidad.ToTable("aviso", UcoKudosDbContext.EsquemaNotificaciones);
        entidad.HasKey(a => a.Id);

        entidad.Property(a => a.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        entidad.Property(a => a.PersonaId).HasColumnName("persona_id");
        entidad.Property(a => a.ClaveMensaje).HasColumnName("clave_mensaje").HasMaxLength(100).IsRequired();
        entidad.Property(a => a.Variables).HasColumnName("variables").HasColumnType("jsonb").IsRequired();
        entidad.Property(a => a.Leido).HasColumnName("leido");
        entidad.Property(a => a.GeneradoEn).HasColumnName("generado_en");

        entidad.HasIndex(a => new { a.PersonaId, a.Id })
            .IsDescending(false, true)
            .HasDatabaseName("ix_aviso_persona");

        entidad.HasOne<Persona>().WithMany().HasForeignKey(a => a.PersonaId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Mapeo de la auditoría (ADR-0013). La tabla es de **solo inserción**, y eso no se consigue aquí:
/// lo garantiza un disparador que se crea en la migración, de modo que ni la propia aplicación pueda
/// actualizar ni borrar.
///
/// El identificador es una secuencia porque el orden de inserción **es** el orden de la cadena: con
/// un identificador aleatorio no habría forma de recorrerla.
/// </summary>
internal sealed class ConfiguracionEventoAuditoria : IEntityTypeConfiguration<EventoAuditoria>
{
    public void Configure(EntityTypeBuilder<EventoAuditoria> entidad)
    {
        entidad.ToTable("evento_auditoria", UcoKudosDbContext.EsquemaAuditoria);
        entidad.HasKey(e => e.Id);

        entidad.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        entidad.Property(e => e.ActorId).HasColumnName("actor_id");
        entidad.Property(e => e.Accion).HasColumnName("accion").HasMaxLength(100).IsRequired();
        entidad.Property(e => e.TipoEntidad).HasColumnName("tipo_entidad").HasMaxLength(100).IsRequired();
        entidad.Property(e => e.EntidadId).HasColumnName("entidad_id");
        entidad.Property(e => e.Datos).HasColumnName("datos").HasColumnType("jsonb").IsRequired();
        entidad.Property(e => e.CorrelacionId).HasColumnName("correlacion_id").HasMaxLength(100);
        entidad.Property(e => e.HashAnterior).HasColumnName("hash_anterior").HasMaxLength(64);
        entidad.Property(e => e.Hash).HasColumnName("hash").HasMaxLength(64).IsRequired();
        entidad.Property(e => e.OcurridoEn).HasColumnName("ocurrido_en");

        // Reconstruir una publicación concreta con un solo identificador (QA-OBS-01).
        entidad.HasIndex(e => e.CorrelacionId).HasDatabaseName("ix_auditoria_correlacion");

        entidad.HasIndex(e => new { e.EntidadId, e.OcurridoEn })
            .IsDescending(false, true)
            .HasDatabaseName("ix_auditoria_entidad");

        entidad.HasIndex(e => e.Hash).IsUnique().HasDatabaseName("ux_auditoria_hash");
    }
}
