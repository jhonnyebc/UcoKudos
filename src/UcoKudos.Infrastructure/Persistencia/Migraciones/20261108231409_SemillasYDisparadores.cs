using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UcoKudos.Infrastructure.Persistencia.Migraciones;

/// <summary>
/// Lo que el esquema no puede expresar por sí solo: el disparador que hace la auditoría inmutable
/// y las semillas que dejan la base usable al levantarla.
///
/// Va en una migración aparte de la inicial a propósito: la inicial la genera la herramienta a
/// partir del modelo, y esta es la parte escrita a mano. Mezclarlas haría que regenerar la
/// primera borrara la segunda.
/// </summary>
public partial class SemillasYDisparadores : Migration
{
    /// <summary>
    /// Identificador del sistema, para las filas que no crea una persona. No apunta a ninguna
    /// fila de persona, y por eso la columna que lo usa no tiene clave externa.
    /// </summary>
    private const string ActorSistema = "00000000-0000-0000-0000-000000000001";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        CrearDisparadorDeAuditoria(migrationBuilder);
        SembrarCategorias(migrationBuilder);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_auditoria_solo_insercion ON audit.evento_auditoria;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit.rechazar_cambio();");
        migrationBuilder.Sql("DELETE FROM nucleo.categoria WHERE creada_por = '" + ActorSistema + "';");
    }

    /// <summary>
    /// Hace la tabla de auditoría de **solo inserción** (ADR-0013).
    ///
    /// Esta es la pieza que convierte la inmutabilidad en una garantía y no en una convención: el
    /// disparador rechaza toda actualización y todo borrado, así que ni la propia aplicación puede
    /// alterar el registro. La cadena de hashes **detecta** una alteración; esto la **impide**.
    /// </summary>
    private static void CrearDisparadorDeAuditoria(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "CREATE OR REPLACE FUNCTION audit.rechazar_cambio() " +
            "RETURNS trigger LANGUAGE plpgsql AS $$ " +
            "BEGIN " +
            "  RAISE EXCEPTION 'La tabla de auditoria es de solo insercion: no se permite % (ADR-0013).', TG_OP; " +
            "END; $$;");

        migrationBuilder.Sql(
            "CREATE TRIGGER tr_auditoria_solo_insercion " +
            "BEFORE UPDATE OR DELETE ON audit.evento_auditoria " +
            "FOR EACH ROW EXECUTE FUNCTION audit.rechazar_cambio();");
    }

    /// <summary>
    /// Siembra las quince categorías del enunciado.
    ///
    /// Se siembran por migración y no a mano porque una base recién levantada tiene que quedar
    /// usable: sin categorías no se puede publicar ni un Kudo, y eso es lo primero que hace
    /// alguien al levantar el entorno.
    ///
    /// Los identificadores son **fijos** y no aleatorios, así que la misma migración produce la
    /// misma base en cualquier entorno y las pruebas pueden referirse a una categoría concreta.
    /// </summary>
    private static void SembrarCategorias(MigrationBuilder migrationBuilder)
    {
        var categorias = new (string Id, string Nombre, string Descripcion)[]
        {
            ("0193a000-0000-7000-8000-000000000001", "Rendimiento Académico", "Resultados académicos destacados y constancia en el estudio."),
            ("0193a000-0000-7000-8000-000000000002", "Mejor Investigador", "Aportes relevantes a la investigación y producción académica."),
            ("0193a000-0000-7000-8000-000000000003", "Empatía", "Capacidad de comprender y acompañar a los demás."),
            ("0193a000-0000-7000-8000-000000000004", "Comunicación Asertiva", "Expresa ideas con claridad y respeto, y escucha de verdad."),
            ("0193a000-0000-7000-8000-000000000005", "Orientación al Logro", "Persigue objetivos con determinación y los cumple."),
            ("0193a000-0000-7000-8000-000000000006", "Trabajo en Equipo", "Aporta al grupo y hace mejor el trabajo de los demás."),
            ("0193a000-0000-7000-8000-000000000007", "Trabajo Bajo Presión", "Mantiene la calidad y la calma cuando las condiciones aprietan."),
            ("0193a000-0000-7000-8000-000000000008", "Visión Estratégica", "Ve el panorama completo y anticipa lo que viene."),
            ("0193a000-0000-7000-8000-000000000009", "Innovación", "Propone soluciones nuevas a problemas conocidos."),
            ("0193a000-0000-7000-8000-00000000000a", "Liderazgo", "Guía e inspira a otros hacia un propósito común."),
            ("0193a000-0000-7000-8000-00000000000b", "Transformación Digital", "Impulsa el uso de la tecnología para mejorar procesos."),
            ("0193a000-0000-7000-8000-00000000000c", "Mentor Inspirador", "Acompaña el crecimiento de otros y comparte lo que sabe."),
            ("0193a000-0000-7000-8000-00000000000d", "Espíritu UCO", "Encarna los valores y la identidad de la Universidad Católica de Oriente."),
            ("0193a000-0000-7000-8000-00000000000e", "Servicio a la Comunidad", "Pone su tiempo y capacidades al servicio de los demás."),
            ("0193a000-0000-7000-8000-00000000000f", "Compromiso Institucional", "Responde con lealtad y dedicación a la institución.")
        };

        var sembradoEn = new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);

        foreach (var categoria in categorias)
        {
            migrationBuilder.InsertData(
                schema: "nucleo",
                table: "categoria",
                columns: ["id", "nombre", "descripcion", "activa", "creada_por", "creada_en"],
                values:
                [
                    Guid.Parse(categoria.Id),
                    categoria.Nombre,
                    categoria.Descripcion,
                    true,
                    Guid.Parse(ActorSistema),
                    sembradoEn
                ]);
        }
    }
}
