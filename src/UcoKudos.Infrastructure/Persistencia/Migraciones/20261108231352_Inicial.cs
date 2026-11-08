using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace UcoKudos.Infrastructure.Persistencia.Migraciones;

/// <inheritdoc />
public partial class Inicial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "nucleo");

        migrationBuilder.EnsureSchema(
            name: "notif");

        migrationBuilder.EnsureSchema(
            name: "interaccion");

        migrationBuilder.EnsureSchema(
            name: "audit");

        migrationBuilder.CreateTable(
            name: "categoria",
            schema: "nucleo",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                activa = table.Column<bool>(type: "boolean", nullable: false),
                creada_por = table.Column<Guid>(type: "uuid", nullable: false),
                creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_categoria", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "evento_auditoria",
            schema: "audit",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                accion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                tipo_entidad = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                entidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                datos = table.Column<string>(type: "jsonb", nullable: false),
                correlacion_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                hash_anterior = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ocurrido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_evento_auditoria", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "facultad",
            schema: "nucleo",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                activa = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_facultad", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "outbox_mensaje",
            schema: "notif",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                tipo_evento = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                ocurrido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                proximo_intento = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                intentos = table.Column<short>(type: "smallint", nullable: false),
                ultimo_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                contexto_traza = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                procesado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_mensaje", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "persona",
            schema: "nucleo",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                correo_institucional = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                celular_e164 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                nombre_completo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                correo_verificado = table.Column<bool>(type: "boolean", nullable: false),
                celular_verificado = table.Column<bool>(type: "boolean", nullable: false),
                solicitud_aprobada = table.Column<bool>(type: "boolean", nullable: false),
                estado_cuenta = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_persona", x => x.id);
                table.CheckConstraint("ck_persona_activa_exige_verificaciones", "estado_cuenta <> 'Activa' OR (correo_verificado AND celular_verificado AND solicitud_aprobada)");
            });

        migrationBuilder.CreateTable(
            name: "programa",
            schema: "nucleo",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                facultad_id = table.Column<Guid>(type: "uuid", nullable: false),
                nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                activo = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_programa", x => x.id);
                table.ForeignKey(
                    name: "FK_programa_facultad_facultad_id",
                    column: x => x.facultad_id,
                    principalSchema: "nucleo",
                    principalTable: "facultad",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "aviso",
            schema: "notif",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                clave_mensaje = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                variables = table.Column<string>(type: "jsonb", nullable: false),
                leido = table.Column<bool>(type: "boolean", nullable: false),
                generado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_aviso", x => x.id);
                table.ForeignKey(
                    name: "FK_aviso_persona_persona_id",
                    column: x => x.persona_id,
                    principalSchema: "nucleo",
                    principalTable: "persona",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "calificacion",
            schema: "interaccion",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                tipo_contenido = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                contenido_id = table.Column<Guid>(type: "uuid", nullable: false),
                estrellas = table.Column<short>(type: "smallint", nullable: false),
                activa = table.Column<bool>(type: "boolean", nullable: false),
                calificada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_calificacion", x => x.id);
                table.CheckConstraint("ck_calificacion_escala", "estrellas BETWEEN 1 AND 5");
                table.ForeignKey(
                    name: "FK_calificacion_persona_persona_id",
                    column: x => x.persona_id,
                    principalSchema: "nucleo",
                    principalTable: "persona",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "kudo",
            schema: "nucleo",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                autor_id = table.Column<Guid>(type: "uuid", nullable: false),
                destinatario_id = table.Column<Guid>(type: "uuid", nullable: false),
                categoria_id = table.Column<Guid>(type: "uuid", nullable: false),
                contenido = table.Column<string>(type: "text", nullable: false),
                vetado = table.Column<bool>(type: "boolean", nullable: false),
                publicado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                total_replicas = table.Column<int>(type: "integer", nullable: false),
                promedio_calificacion = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                total_calificaciones = table.Column<int>(type: "integer", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_kudo", x => x.id);
                table.CheckConstraint("ck_kudo_autor_distinto_destinatario", "autor_id <> destinatario_id");
                table.ForeignKey(
                    name: "FK_kudo_categoria_categoria_id",
                    column: x => x.categoria_id,
                    principalSchema: "nucleo",
                    principalTable: "categoria",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_kudo_persona_autor_id",
                    column: x => x.autor_id,
                    principalSchema: "nucleo",
                    principalTable: "persona",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_kudo_persona_destinatario_id",
                    column: x => x.destinatario_id,
                    principalSchema: "nucleo",
                    principalTable: "persona",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "notificacion",
            schema: "notif",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                destinatario_id = table.Column<Guid>(type: "uuid", nullable: false),
                canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                clave_mensaje = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                id_externo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                enviada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notificacion", x => x.id);
                table.ForeignKey(
                    name: "FK_notificacion_persona_destinatario_id",
                    column: x => x.destinatario_id,
                    principalSchema: "nucleo",
                    principalTable: "persona",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "reporte",
            schema: "interaccion",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                tipo_contenido = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                contenido_id = table.Column<Guid>(type: "uuid", nullable: false),
                motivo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                presentado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_reporte", x => x.id);
                table.ForeignKey(
                    name: "FK_reporte_persona_persona_id",
                    column: x => x.persona_id,
                    principalSchema: "nucleo",
                    principalTable: "persona",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "suscripcion_push",
            schema: "notif",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                clave_p256dh = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                clave_auth = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                activa = table.Column<bool>(type: "boolean", nullable: false),
                registrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_suscripcion_push", x => x.id);
                table.ForeignKey(
                    name: "FK_suscripcion_push_persona_persona_id",
                    column: x => x.persona_id,
                    principalSchema: "nucleo",
                    principalTable: "persona",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "veto",
            schema: "interaccion",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                aplicado_por = table.Column<Guid>(type: "uuid", nullable: false),
                tipo_contenido = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                contenido_id = table.Column<Guid>(type: "uuid", nullable: false),
                motivo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                contenidos_afectados = table.Column<int>(type: "integer", nullable: false),
                en_cascada = table.Column<bool>(type: "boolean", nullable: false),
                aplicado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_veto", x => x.id);
                table.ForeignKey(
                    name: "FK_veto_persona_aplicado_por",
                    column: x => x.aplicado_por,
                    principalSchema: "nucleo",
                    principalTable: "persona",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "asignacion_rol",
            schema: "nucleo",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                rol_id = table.Column<short>(type: "smallint", nullable: false),
                facultad_id = table.Column<Guid>(type: "uuid", nullable: true),
                programa_id = table.Column<Guid>(type: "uuid", nullable: true),
                activa = table.Column<bool>(type: "boolean", nullable: false),
                desde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                hasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_asignacion_rol", x => x.id);
                table.ForeignKey(
                    name: "FK_asignacion_rol_facultad_facultad_id",
                    column: x => x.facultad_id,
                    principalSchema: "nucleo",
                    principalTable: "facultad",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_asignacion_rol_persona_persona_id",
                    column: x => x.persona_id,
                    principalSchema: "nucleo",
                    principalTable: "persona",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_asignacion_rol_programa_programa_id",
                    column: x => x.programa_id,
                    principalSchema: "nucleo",
                    principalTable: "programa",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "vinculo_academico",
            schema: "nucleo",
            columns: table => new
            {
                persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                programa_id = table.Column<Guid>(type: "uuid", nullable: false),
                tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                activo = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_vinculo_academico", x => new { x.persona_id, x.programa_id, x.tipo });
                table.ForeignKey(
                    name: "FK_vinculo_academico_persona_persona_id",
                    column: x => x.persona_id,
                    principalSchema: "nucleo",
                    principalTable: "persona",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_vinculo_academico_programa_programa_id",
                    column: x => x.programa_id,
                    principalSchema: "nucleo",
                    principalTable: "programa",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "adjunto",
            schema: "nucleo",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                kudo_id = table.Column<Guid>(type: "uuid", nullable: false),
                clave_objeto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                nombre_original = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                tipo_detectado = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                tamano_bytes = table.Column<long>(type: "bigint", nullable: false),
                hash_contenido = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                cargado_por = table.Column<Guid>(type: "uuid", nullable: false),
                cargado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                confirmado = table.Column<bool>(type: "boolean", nullable: false),
                eliminado = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_adjunto", x => x.id);
                table.ForeignKey(
                    name: "FK_adjunto_kudo_kudo_id",
                    column: x => x.kudo_id,
                    principalSchema: "nucleo",
                    principalTable: "kudo",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "replica",
            schema: "interaccion",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                kudo_id = table.Column<Guid>(type: "uuid", nullable: false),
                replica_padre_id = table.Column<Guid>(type: "uuid", nullable: true),
                autor_id = table.Column<Guid>(type: "uuid", nullable: false),
                contenido = table.Column<string>(type: "text", nullable: false),
                ruta = table.Column<string>(type: "character varying(2200)", maxLength: 2200, nullable: false),
                profundidad = table.Column<short>(type: "smallint", nullable: false),
                vetado = table.Column<bool>(type: "boolean", nullable: false),
                publicada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                total_replicas = table.Column<int>(type: "integer", nullable: false),
                promedio_calificacion = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                total_calificaciones = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_replica", x => x.id);
                table.ForeignKey(
                    name: "FK_replica_kudo_kudo_id",
                    column: x => x.kudo_id,
                    principalSchema: "nucleo",
                    principalTable: "kudo",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_replica_persona_autor_id",
                    column: x => x.autor_id,
                    principalSchema: "nucleo",
                    principalTable: "persona",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_replica_replica_replica_padre_id",
                    column: x => x.replica_padre_id,
                    principalSchema: "interaccion",
                    principalTable: "replica",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_adjunto_kudo",
            schema: "nucleo",
            table: "adjunto",
            column: "kudo_id");

        migrationBuilder.CreateIndex(
            name: "ux_adjunto_clave",
            schema: "nucleo",
            table: "adjunto",
            column: "clave_objeto",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_asignacion_rol_facultad_id",
            schema: "nucleo",
            table: "asignacion_rol",
            column: "facultad_id");

        migrationBuilder.CreateIndex(
            name: "IX_asignacion_rol_programa_id",
            schema: "nucleo",
            table: "asignacion_rol",
            column: "programa_id");

        migrationBuilder.CreateIndex(
            name: "ux_rol_activo_por_persona",
            schema: "nucleo",
            table: "asignacion_rol",
            column: "persona_id",
            unique: true,
            filter: "activa");

        migrationBuilder.CreateIndex(
            name: "ux_rol_unico_facultad",
            schema: "nucleo",
            table: "asignacion_rol",
            columns: new[] { "rol_id", "facultad_id" },
            unique: true,
            filter: "activa AND rol_id = 4");

        migrationBuilder.CreateIndex(
            name: "ux_rol_unico_programa",
            schema: "nucleo",
            table: "asignacion_rol",
            columns: new[] { "rol_id", "programa_id" },
            unique: true,
            filter: "activa AND rol_id = 5");

        migrationBuilder.CreateIndex(
            name: "ux_rol_unico_universidad",
            schema: "nucleo",
            table: "asignacion_rol",
            column: "rol_id",
            unique: true,
            filter: "activa AND rol_id IN (2, 3)");

        migrationBuilder.CreateIndex(
            name: "ix_aviso_persona",
            schema: "notif",
            table: "aviso",
            columns: new[] { "persona_id", "id" },
            descending: new[] { false, true });

        migrationBuilder.CreateIndex(
            name: "ix_calificacion_contenido",
            schema: "interaccion",
            table: "calificacion",
            columns: new[] { "tipo_contenido", "contenido_id" },
            filter: "activa");

        migrationBuilder.CreateIndex(
            name: "ux_calificacion_activa",
            schema: "interaccion",
            table: "calificacion",
            columns: new[] { "persona_id", "tipo_contenido", "contenido_id" },
            unique: true,
            filter: "activa");

        migrationBuilder.CreateIndex(
            name: "ux_categoria_nombre",
            schema: "nucleo",
            table: "categoria",
            column: "nombre",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_auditoria_correlacion",
            schema: "audit",
            table: "evento_auditoria",
            column: "correlacion_id");

        migrationBuilder.CreateIndex(
            name: "ix_auditoria_entidad",
            schema: "audit",
            table: "evento_auditoria",
            columns: new[] { "entidad_id", "ocurrido_en" },
            descending: new[] { false, true });

        migrationBuilder.CreateIndex(
            name: "ux_auditoria_hash",
            schema: "audit",
            table: "evento_auditoria",
            column: "hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_facultad_codigo",
            schema: "nucleo",
            table: "facultad",
            column: "codigo",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_kudo_autor_id",
            schema: "nucleo",
            table: "kudo",
            column: "autor_id");

        migrationBuilder.CreateIndex(
            name: "ix_kudo_categoria",
            schema: "nucleo",
            table: "kudo",
            columns: new[] { "categoria_id", "publicado_en" },
            descending: new[] { false, true },
            filter: "NOT vetado");

        migrationBuilder.CreateIndex(
            name: "ix_kudo_muro",
            schema: "nucleo",
            table: "kudo",
            columns: new[] { "publicado_en", "id" },
            descending: new bool[0],
            filter: "NOT vetado");

        migrationBuilder.CreateIndex(
            name: "ix_kudo_recibidos",
            schema: "nucleo",
            table: "kudo",
            columns: new[] { "destinatario_id", "publicado_en" },
            descending: new[] { false, true },
            filter: "NOT vetado");

        migrationBuilder.CreateIndex(
            name: "IX_notificacion_destinatario_id",
            schema: "notif",
            table: "notificacion",
            column: "destinatario_id");

        migrationBuilder.CreateIndex(
            name: "ux_notificacion_idempotencia",
            schema: "notif",
            table: "notificacion",
            columns: new[] { "evento_id", "destinatario_id", "canal" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_outbox_evento",
            schema: "notif",
            table: "outbox_mensaje",
            column: "evento_id");

        migrationBuilder.CreateIndex(
            name: "ix_outbox_pendientes",
            schema: "notif",
            table: "outbox_mensaje",
            columns: new[] { "proximo_intento", "ocurrido_en" },
            filter: "procesado_en IS NULL");

        migrationBuilder.CreateIndex(
            name: "ux_persona_correo",
            schema: "nucleo",
            table: "persona",
            column: "correo_institucional",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_programa_facultad",
            schema: "nucleo",
            table: "programa",
            column: "facultad_id");

        migrationBuilder.CreateIndex(
            name: "ux_programa_codigo",
            schema: "nucleo",
            table: "programa",
            column: "codigo",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_replica_autor_id",
            schema: "interaccion",
            table: "replica",
            column: "autor_id");

        migrationBuilder.CreateIndex(
            name: "ix_replica_hilo",
            schema: "interaccion",
            table: "replica",
            columns: new[] { "kudo_id", "ruta" });

        migrationBuilder.CreateIndex(
            name: "IX_replica_replica_padre_id",
            schema: "interaccion",
            table: "replica",
            column: "replica_padre_id");

        migrationBuilder.CreateIndex(
            name: "ix_replica_ruta_prefijo",
            schema: "interaccion",
            table: "replica",
            column: "ruta")
            .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

        migrationBuilder.CreateIndex(
            name: "ix_reporte_pendiente",
            schema: "interaccion",
            table: "reporte",
            column: "estado",
            filter: "estado = 'Pendiente'");

        migrationBuilder.CreateIndex(
            name: "IX_reporte_persona_id",
            schema: "interaccion",
            table: "reporte",
            column: "persona_id");

        migrationBuilder.CreateIndex(
            name: "ix_suscripcion_persona_activa",
            schema: "notif",
            table: "suscripcion_push",
            column: "persona_id",
            filter: "activa");

        migrationBuilder.CreateIndex(
            name: "ux_suscripcion_endpoint",
            schema: "notif",
            table: "suscripcion_push",
            column: "endpoint",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_veto_aplicado_por",
            schema: "interaccion",
            table: "veto",
            column: "aplicado_por");

        migrationBuilder.CreateIndex(
            name: "IX_vinculo_academico_programa_id",
            schema: "nucleo",
            table: "vinculo_academico",
            column: "programa_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "adjunto",
            schema: "nucleo");

        migrationBuilder.DropTable(
            name: "asignacion_rol",
            schema: "nucleo");

        migrationBuilder.DropTable(
            name: "aviso",
            schema: "notif");

        migrationBuilder.DropTable(
            name: "calificacion",
            schema: "interaccion");

        migrationBuilder.DropTable(
            name: "evento_auditoria",
            schema: "audit");

        migrationBuilder.DropTable(
            name: "notificacion",
            schema: "notif");

        migrationBuilder.DropTable(
            name: "outbox_mensaje",
            schema: "notif");

        migrationBuilder.DropTable(
            name: "replica",
            schema: "interaccion");

        migrationBuilder.DropTable(
            name: "reporte",
            schema: "interaccion");

        migrationBuilder.DropTable(
            name: "suscripcion_push",
            schema: "notif");

        migrationBuilder.DropTable(
            name: "veto",
            schema: "interaccion");

        migrationBuilder.DropTable(
            name: "vinculo_academico",
            schema: "nucleo");

        migrationBuilder.DropTable(
            name: "kudo",
            schema: "nucleo");

        migrationBuilder.DropTable(
            name: "programa",
            schema: "nucleo");

        migrationBuilder.DropTable(
            name: "categoria",
            schema: "nucleo");

        migrationBuilder.DropTable(
            name: "persona",
            schema: "nucleo");

        migrationBuilder.DropTable(
            name: "facultad",
            schema: "nucleo");
    }
}
