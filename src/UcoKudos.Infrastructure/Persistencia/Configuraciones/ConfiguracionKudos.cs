using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Interaccion;
using UcoKudos.Domain.Kudos;

namespace UcoKudos.Infrastructure.Persistencia.Configuraciones;

internal sealed class ConfiguracionCategoria : IEntityTypeConfiguration<Categoria>
{
    public void Configure(EntityTypeBuilder<Categoria> entidad)
    {
        entidad.ToTable("categoria", UcoKudosDbContext.EsquemaNucleo);
        entidad.HasKey(c => c.Id);

        entidad.Property(c => c.Id).HasColumnName("id");
        entidad.Property(c => c.Nombre).HasColumnName("nombre").HasMaxLength(120).IsRequired();
        entidad.Property(c => c.Descripcion).HasColumnName("descripcion").HasMaxLength(500);
        entidad.Property(c => c.Activa).HasColumnName("activa");
        entidad.Property(c => c.CreadaPor).HasColumnName("creada_por");
        entidad.Property(c => c.CreadaEn).HasColumnName("creada_en");

        entidad.HasIndex(c => c.Nombre).IsUnique().HasDatabaseName("ux_categoria_nombre");
    }
}

/// <summary>
/// Mapeo de <see cref="Kudo"/>. Dos cosas que conviene mirar aquí:
///
///  - El **token de concurrencia** se mapea a la columna de sistema del motor, así que no ocupa
///    espacio propio y el ORM lo administra solo (ADR-0005).
///  - Los **índices del muro** son parciales: excluyen el contenido vetado, porque el muro nunca lo
///    muestra. Incluirlo sería espacio de índice que ninguna consulta recorre (ADR-0010).
/// </summary>
internal sealed class ConfiguracionKudo : IEntityTypeConfiguration<Kudo>
{
    public void Configure(EntityTypeBuilder<Kudo> entidad)
    {
        entidad.ToTable("kudo", UcoKudosDbContext.EsquemaNucleo);
        entidad.HasKey(k => k.Id);

        entidad.Property(k => k.Id).HasColumnName("id");
        entidad.Property(k => k.AutorId).HasColumnName("autor_id");
        entidad.Property(k => k.DestinatarioId).HasColumnName("destinatario_id");
        entidad.Property(k => k.CategoriaId).HasColumnName("categoria_id");

        // Sin límite de longitud (RN-03): el tipo de texto de PostgreSQL no tiene límite práctico.
        entidad.Property(k => k.Contenido).HasColumnName("contenido").IsRequired();

        entidad.Property(k => k.Vetado).HasColumnName("vetado");
        entidad.Property(k => k.PublicadoEn).HasColumnName("publicado_en");
        entidad.Property(k => k.TotalReplicas).HasColumnName("total_replicas");
        entidad.Property(k => k.TotalCalificaciones).HasColumnName("total_calificaciones");
        entidad.Property(k => k.PromedioCalificacion).HasColumnName("promedio_calificacion").HasPrecision(3, 2);

        // Concurrencia optimista sobre la columna de sistema xmin (ADR-0005).
        entidad.Property(k => k.Version).HasColumnName("xmin").IsRowVersion();

        // El autor no puede ser el destinatario. El dominio ya lo impide; aquí queda también en la
        // base, que es donde no se puede eludir.
        entidad.ToTable(t => t.HasCheckConstraint("ck_kudo_autor_distinto_destinatario", "autor_id <> destinatario_id"));

        // Muro general: fecha descendente con el identificador para desempatar, que es exactamente
        // el orden de la paginación por cursor.
        entidad.HasIndex(k => new { k.PublicadoEn, k.Id })
            .IsDescending(true, true)
            .HasFilter("NOT vetado")
            .HasDatabaseName("ix_kudo_muro");

        entidad.HasIndex(k => new { k.DestinatarioId, k.PublicadoEn })
            .IsDescending(false, true)
            .HasFilter("NOT vetado")
            .HasDatabaseName("ix_kudo_recibidos");

        entidad.HasIndex(k => new { k.CategoriaId, k.PublicadoEn })
            .IsDescending(false, true)
            .HasFilter("NOT vetado")
            .HasDatabaseName("ix_kudo_categoria");

        entidad.HasOne<Persona>().WithMany().HasForeignKey(k => k.AutorId).OnDelete(DeleteBehavior.Restrict);
        entidad.HasOne<Persona>().WithMany().HasForeignKey(k => k.DestinatarioId).OnDelete(DeleteBehavior.Restrict);
        entidad.HasOne<Categoria>().WithMany().HasForeignKey(k => k.CategoriaId).OnDelete(DeleteBehavior.Restrict);

        // Los adjuntos son parte del agregado: se cargan y se guardan con el Kudo.
        entidad.HasMany(k => k.Adjuntos)
            .WithOne()
            .HasForeignKey(a => a.KudoId)
            .OnDelete(DeleteBehavior.Cascade);

        entidad.Navigation(k => k.Adjuntos).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>
/// Mapeo de <see cref="Adjunto"/>. Guarda **metadatos**, nunca el archivo: el contenido vive en el
/// almacenamiento de objetos (ADR-0012).
/// </summary>
internal sealed class ConfiguracionAdjunto : IEntityTypeConfiguration<Adjunto>
{
    public void Configure(EntityTypeBuilder<Adjunto> entidad)
    {
        entidad.ToTable("adjunto", UcoKudosDbContext.EsquemaNucleo);
        entidad.HasKey(a => a.Id);

        // **La clave la asigna el dominio, y hay que decírselo a EF.** Para una clave Guid, EF asume
        // por omisión que la genera él al insertar. Con esa suposición, cuando una entidad nueva
        // aparece dentro de la colección de un agregado ya cargado y **su clave ya viene rellena**
        // —que es lo que hace `Kudo.AgregarAdjunto`—, EF concluye que la fila ya existe y emite un
        // UPDATE en lugar de un INSERT. Cero filas afectadas, y lo traduce como un conflicto de
        // concurrencia: el usuario veía «otra operación modificó este contenido» sin que nadie
        // estuviera tocando nada.
        //
        // Se descubrió con el primer uso real desde un celular. Las pruebas anteriores no lo vieron
        // porque ninguna añadía un adjunto a un Kudo **cargado de la base**, que es el camino de
        // verdad. Con la clave declarada como no generada, EF marca la entidad como nueva.
        entidad.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
        entidad.Property(a => a.KudoId).HasColumnName("kudo_id");
        entidad.Property(a => a.ClaveObjeto).HasColumnName("clave_objeto").HasMaxLength(300).IsRequired();
        entidad.Property(a => a.NombreOriginal).HasColumnName("nombre_original").HasMaxLength(255).IsRequired();
        entidad.Property(a => a.TipoDetectado).HasColumnName("tipo_detectado").HasMaxLength(100);
        entidad.Property(a => a.TamanoBytes).HasColumnName("tamano_bytes");
        entidad.Property(a => a.HashContenido).HasColumnName("hash_contenido").HasMaxLength(64);
        entidad.Property(a => a.CargadoPor).HasColumnName("cargado_por");
        entidad.Property(a => a.CargadoEn).HasColumnName("cargado_en");
        entidad.Property(a => a.Confirmado).HasColumnName("confirmado");
        entidad.Property(a => a.Eliminado).HasColumnName("eliminado");

        entidad.Ignore(a => a.EsServible);

        entidad.HasIndex(a => a.ClaveObjeto).IsUnique().HasDatabaseName("ux_adjunto_clave");
        entidad.HasIndex(a => a.KudoId).HasDatabaseName("ix_adjunto_kudo");
    }
}

/// <summary>
/// Mapeo de <see cref="Replica"/>, con el índice que hace rentable la ruta materializada (ADR-0011).
///
/// El índice de prefijo usa la clase de operadores de patrón de texto: sin ella, la búsqueda por
/// prefijo recorrería la tabla en lugar de usar el índice, y toda la decisión de la ruta materializada
/// perdería su sentido.
/// </summary>
internal sealed class ConfiguracionReplica : IEntityTypeConfiguration<Replica>
{
    public void Configure(EntityTypeBuilder<Replica> entidad)
    {
        entidad.ToTable("replica", UcoKudosDbContext.EsquemaInteraccion);
        entidad.HasKey(r => r.Id);

        entidad.Property(r => r.Id).HasColumnName("id");
        entidad.Property(r => r.KudoId).HasColumnName("kudo_id");
        entidad.Property(r => r.ReplicaPadreId).HasColumnName("replica_padre_id");
        entidad.Property(r => r.AutorId).HasColumnName("autor_id");
        entidad.Property(r => r.Contenido).HasColumnName("contenido").IsRequired();
        entidad.Property(r => r.Ruta).HasColumnName("ruta").HasMaxLength(2200).IsRequired();
        entidad.Property(r => r.Profundidad).HasColumnName("profundidad");
        entidad.Property(r => r.Vetado).HasColumnName("vetado");
        entidad.Property(r => r.PublicadaEn).HasColumnName("publicada_en");
        entidad.Property(r => r.TotalReplicas).HasColumnName("total_replicas");
        entidad.Property(r => r.TotalCalificaciones).HasColumnName("total_calificaciones");
        entidad.Property(r => r.PromedioCalificacion).HasColumnName("promedio_calificacion").HasPrecision(3, 2);

        entidad.Ignore(r => r.RutaMaterializada);

        // Hilo completo de un Kudo: una consulta, ordenada por ruta, que ya es el orden de presentación.
        entidad.HasIndex(r => new { r.KudoId, r.Ruta }).HasDatabaseName("ix_replica_hilo");

        // Búsqueda por prefijo para el veto en cascada.
        entidad.HasIndex(r => r.Ruta)
            .HasDatabaseName("ix_replica_ruta_prefijo")
            .HasOperators("text_pattern_ops");

        entidad.HasOne<Kudo>().WithMany().HasForeignKey(r => r.KudoId).OnDelete(DeleteBehavior.Cascade);
        entidad.HasOne<Replica>().WithMany().HasForeignKey(r => r.ReplicaPadreId).OnDelete(DeleteBehavior.Restrict);
        entidad.HasOne<Persona>().WithMany().HasForeignKey(r => r.AutorId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapeo de <see cref="Calificacion"/>. El índice único parcial es la garantía de «una calificación
/// activa por usuario y contenido» (RF-10).
/// </summary>
internal sealed class ConfiguracionCalificacion : IEntityTypeConfiguration<Calificacion>
{
    public void Configure(EntityTypeBuilder<Calificacion> entidad)
    {
        entidad.ToTable("calificacion", UcoKudosDbContext.EsquemaInteraccion);
        entidad.HasKey(c => c.Id);

        entidad.Property(c => c.Id).HasColumnName("id");
        entidad.Property(c => c.PersonaId).HasColumnName("persona_id");
        entidad.Property(c => c.TipoContenido).HasColumnName("tipo_contenido").HasConversion<string>().HasMaxLength(20);
        entidad.Property(c => c.ContenidoId).HasColumnName("contenido_id");
        entidad.Property(c => c.Estrellas).HasColumnName("estrellas");
        entidad.Property(c => c.Activa).HasColumnName("activa");
        entidad.Property(c => c.CalificadaEn).HasColumnName("calificada_en");

        entidad.ToTable(t => t.HasCheckConstraint("ck_calificacion_escala", "estrellas BETWEEN 1 AND 5"));

        entidad.HasIndex(c => new { c.PersonaId, c.TipoContenido, c.ContenidoId })
            .IsUnique()
            .HasFilter("activa")
            .HasDatabaseName("ux_calificacion_activa");

        entidad.HasIndex(c => new { c.TipoContenido, c.ContenidoId })
            .HasFilter("activa")
            .HasDatabaseName("ix_calificacion_contenido");

        entidad.HasOne<Persona>().WithMany().HasForeignKey(c => c.PersonaId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ConfiguracionReporte : IEntityTypeConfiguration<Reporte>
{
    public void Configure(EntityTypeBuilder<Reporte> entidad)
    {
        entidad.ToTable("reporte", UcoKudosDbContext.EsquemaInteraccion);
        entidad.HasKey(r => r.Id);

        entidad.Property(r => r.Id).HasColumnName("id");
        entidad.Property(r => r.PersonaId).HasColumnName("persona_id");
        entidad.Property(r => r.TipoContenido).HasColumnName("tipo_contenido").HasConversion<string>().HasMaxLength(20);
        entidad.Property(r => r.ContenidoId).HasColumnName("contenido_id");
        entidad.Property(r => r.Motivo).HasColumnName("motivo").HasMaxLength(1000).IsRequired();
        entidad.Property(r => r.Estado).HasColumnName("estado").HasConversion<string>().HasMaxLength(20);
        entidad.Property(r => r.PresentadoEn).HasColumnName("presentado_en");

        entidad.HasIndex(r => r.Estado).HasFilter("estado = 'Pendiente'").HasDatabaseName("ix_reporte_pendiente");

        entidad.HasOne<Persona>().WithMany().HasForeignKey(r => r.PersonaId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ConfiguracionVeto : IEntityTypeConfiguration<Veto>
{
    public void Configure(EntityTypeBuilder<Veto> entidad)
    {
        entidad.ToTable("veto", UcoKudosDbContext.EsquemaInteraccion);
        entidad.HasKey(v => v.Id);

        entidad.Property(v => v.Id).HasColumnName("id");
        entidad.Property(v => v.AplicadoPor).HasColumnName("aplicado_por");
        entidad.Property(v => v.TipoContenido).HasColumnName("tipo_contenido").HasConversion<string>().HasMaxLength(20);
        entidad.Property(v => v.ContenidoId).HasColumnName("contenido_id");
        entidad.Property(v => v.Motivo).HasColumnName("motivo").HasMaxLength(1000).IsRequired();
        entidad.Property(v => v.ContenidosAfectados).HasColumnName("contenidos_afectados");
        entidad.Property(v => v.EnCascada).HasColumnName("en_cascada");
        entidad.Property(v => v.AplicadoEn).HasColumnName("aplicado_en");

        entidad.HasOne<Persona>().WithMany().HasForeignKey(v => v.AplicadoPor).OnDelete(DeleteBehavior.Restrict);
    }
}
