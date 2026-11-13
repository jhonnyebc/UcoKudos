using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UcoKudos.Domain.Academico;
using UcoKudos.Domain.Identidad;

namespace UcoKudos.Infrastructure.Persistencia.Configuraciones;

/// <summary>
/// Mapeo de <see cref="Persona"/>. El índice único del correo es el que sostiene la consulta que
/// corre en **cada** petición para resolver el usuario del token (ADR-0004).
/// </summary>
internal sealed class ConfiguracionPersona : IEntityTypeConfiguration<Persona>
{
    public void Configure(EntityTypeBuilder<Persona> entidad)
    {
        entidad.ToTable("persona", UcoKudosDbContext.EsquemaNucleo);
        entidad.HasKey(p => p.Id);

        entidad.Property(p => p.Id).HasColumnName("id");
        entidad.Property(p => p.CorreoInstitucional).HasColumnName("correo_institucional").HasMaxLength(320).IsRequired();
        entidad.Property(p => p.CelularE164).HasColumnName("celular_e164").HasMaxLength(20).IsRequired();
        entidad.Property(p => p.NombreCompleto).HasColumnName("nombre_completo").HasMaxLength(200).IsRequired();
        entidad.Property(p => p.CorreoVerificado).HasColumnName("correo_verificado");
        entidad.Property(p => p.CelularVerificado).HasColumnName("celular_verificado");
        entidad.Property(p => p.SolicitudAprobada).HasColumnName("solicitud_aprobada");
        entidad.Property(p => p.CreadaEn).HasColumnName("creada_en");

        // Las enumeraciones se guardan como texto: un volcado de la base se lee sin consultar el
        // código (ADR-0003).
        entidad.Property(p => p.Estado).HasColumnName("estado_cuenta").HasConversion<string>().HasMaxLength(20);

        entidad.HasIndex(p => p.CorreoInstitucional).IsUnique().HasDatabaseName("ux_persona_correo");

        // RF-02: la cuenta solo puede estar activa con las tres condiciones. El dominio ya lo
        // garantiza; esta restricción lo defiende también en la base, por si algún día se escribiera
        // desde otro sitio.
        entidad.ToTable(t => t.HasCheckConstraint(
            "ck_persona_activa_exige_verificaciones",
            "estado_cuenta <> 'Activa' OR (correo_verificado AND celular_verificado AND solicitud_aprobada)"));
    }
}

/// <summary>Mapeo de <see cref="VerificacionContacto"/>: guarda la huella del código, nunca el código.</summary>
internal sealed class ConfiguracionVerificacionContacto : IEntityTypeConfiguration<VerificacionContacto>
{
    public void Configure(EntityTypeBuilder<VerificacionContacto> entidad)
    {
        entidad.ToTable("verificacion_contacto", UcoKudosDbContext.EsquemaNucleo);
        entidad.HasKey(v => v.Id);

        entidad.Property(v => v.Id).HasColumnName("id").ValueGeneratedNever();
        entidad.Property(v => v.PersonaId).HasColumnName("persona_id");
        entidad.Property(v => v.Tipo).HasColumnName("tipo").HasConversion<string>().HasMaxLength(20);
        entidad.Property(v => v.HuellaCodigo).HasColumnName("huella_codigo").HasMaxLength(64).IsRequired();
        entidad.Property(v => v.CreadaEn).HasColumnName("creada_en");
        entidad.Property(v => v.ExpiraEn).HasColumnName("expira_en");
        entidad.Property(v => v.Intentos).HasColumnName("intentos");
        entidad.Property(v => v.Consumida).HasColumnName("consumida");

        entidad.HasIndex(v => new { v.PersonaId, v.Tipo }).HasDatabaseName("ix_verificacion_persona_tipo");
    }
}

/// <summary>
/// Mapeo de <see cref="AsignacionRol"/>. Aquí viven los **índices únicos parciales** que defienden
/// las dos restricciones organizacionales del enunciado (RF-04, RN-04).
///
/// Son la pieza que hace que esas reglas resistan la concurrencia: una validación en la aplicación
/// deja pasar dos peticiones simultáneas, un índice único rechaza la segunda (QA-SEG-03).
/// </summary>
internal sealed class ConfiguracionAsignacionRol : IEntityTypeConfiguration<AsignacionRol>
{
    public void Configure(EntityTypeBuilder<AsignacionRol> entidad)
    {
        entidad.ToTable("asignacion_rol", UcoKudosDbContext.EsquemaNucleo);
        entidad.HasKey(a => a.Id);

        entidad.Property(a => a.Id).HasColumnName("id");
        entidad.Property(a => a.PersonaId).HasColumnName("persona_id");
        entidad.Property(a => a.RolId).HasColumnName("rol_id");
        entidad.Property(a => a.FacultadId).HasColumnName("facultad_id");
        entidad.Property(a => a.ProgramaId).HasColumnName("programa_id");
        entidad.Property(a => a.Activa).HasColumnName("activa");
        entidad.Property(a => a.Desde).HasColumnName("desde");
        entidad.Property(a => a.Hasta).HasColumnName("hasta");

        // El rol es un catálogo del dominio, no una tabla: no se mapea la propiedad calculada.
        entidad.Ignore(a => a.Rol);

        // EXCLUSIVIDAD (RN-04): una persona tiene un único rol activo.
        entidad.HasIndex(a => a.PersonaId)
            .IsUnique()
            .HasFilter("activa")
            .HasDatabaseName("ux_rol_activo_por_persona");

        // UNICIDAD en la universidad (RF-04): un Rector y un Coordinador Académico activos.
        // El filtro excluye al Administrador, del que puede haber varios.
        entidad.HasIndex(a => a.RolId)
            .IsUnique()
            .HasFilter("activa AND rol_id IN (2, 3)")
            .HasDatabaseName("ux_rol_unico_universidad");

        // UNICIDAD por facultad (RF-04): un Coordinador de Facultad activo por facultad.
        entidad.HasIndex(a => new { a.RolId, a.FacultadId })
            .IsUnique()
            .HasFilter("activa AND rol_id = 4")
            .HasDatabaseName("ux_rol_unico_facultad");

        // UNICIDAD por programa (RF-04): un Coordinador de Programa activo por programa.
        entidad.HasIndex(a => new { a.RolId, a.ProgramaId })
            .IsUnique()
            .HasFilter("activa AND rol_id = 5")
            .HasDatabaseName("ux_rol_unico_programa");

        entidad.HasOne<Persona>().WithMany().HasForeignKey(a => a.PersonaId).OnDelete(DeleteBehavior.Restrict);
        entidad.HasOne<Facultad>().WithMany().HasForeignKey(a => a.FacultadId).OnDelete(DeleteBehavior.Restrict);
        entidad.HasOne<Programa>().WithMany().HasForeignKey(a => a.ProgramaId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfiguracionFacultad : IEntityTypeConfiguration<Facultad>
{
    public void Configure(EntityTypeBuilder<Facultad> entidad)
    {
        entidad.ToTable("facultad", UcoKudosDbContext.EsquemaNucleo);
        entidad.HasKey(f => f.Id);

        entidad.Property(f => f.Id).HasColumnName("id");
        entidad.Property(f => f.Nombre).HasColumnName("nombre").HasMaxLength(200).IsRequired();
        entidad.Property(f => f.Codigo).HasColumnName("codigo").HasMaxLength(20).IsRequired();
        entidad.Property(f => f.Activa).HasColumnName("activa");

        entidad.HasIndex(f => f.Codigo).IsUnique().HasDatabaseName("ux_facultad_codigo");
    }
}

internal sealed class ConfiguracionPrograma : IEntityTypeConfiguration<Programa>
{
    public void Configure(EntityTypeBuilder<Programa> entidad)
    {
        entidad.ToTable("programa", UcoKudosDbContext.EsquemaNucleo);
        entidad.HasKey(p => p.Id);

        entidad.Property(p => p.Id).HasColumnName("id");
        entidad.Property(p => p.FacultadId).HasColumnName("facultad_id");
        entidad.Property(p => p.Nombre).HasColumnName("nombre").HasMaxLength(200).IsRequired();
        entidad.Property(p => p.Codigo).HasColumnName("codigo").HasMaxLength(20).IsRequired();
        entidad.Property(p => p.Activo).HasColumnName("activo");

        entidad.HasIndex(p => p.Codigo).IsUnique().HasDatabaseName("ux_programa_codigo");
        entidad.HasIndex(p => p.FacultadId).HasDatabaseName("ix_programa_facultad");

        entidad.HasOne<Facultad>().WithMany().HasForeignKey(p => p.FacultadId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapeo del vínculo académico (RF-05). La clave es compuesta por persona, programa y tipo: la misma
/// persona puede estudiar **y** enseñar en el mismo programa, que son dos vínculos distintos.
/// </summary>
internal sealed class ConfiguracionVinculoAcademico : IEntityTypeConfiguration<VinculoAcademico>
{
    public void Configure(EntityTypeBuilder<VinculoAcademico> entidad)
    {
        entidad.ToTable("vinculo_academico", UcoKudosDbContext.EsquemaNucleo);
        entidad.HasKey(v => new { v.PersonaId, v.ProgramaId, v.Tipo });

        entidad.Property(v => v.PersonaId).HasColumnName("persona_id");
        entidad.Property(v => v.ProgramaId).HasColumnName("programa_id");
        entidad.Property(v => v.Tipo).HasColumnName("tipo").HasConversion<string>().HasMaxLength(20);
        entidad.Property(v => v.Activo).HasColumnName("activo");

        entidad.HasOne<Persona>().WithMany().HasForeignKey(v => v.PersonaId).OnDelete(DeleteBehavior.Cascade);
        entidad.HasOne<Programa>().WithMany().HasForeignKey(v => v.ProgramaId).OnDelete(DeleteBehavior.Restrict);
    }
}
