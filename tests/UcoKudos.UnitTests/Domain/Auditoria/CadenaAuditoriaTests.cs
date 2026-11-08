using UcoKudos.Domain.Auditoria;

namespace UcoKudos.UnitTests.Domain.Auditoria;

/// <summary>
/// La cadena de hashes es lo que convierte la inmutabilidad de la auditoría en algo **demostrable**
/// y no declarado (ADR-0013). Estas pruebas son esa demostración, y la verificación que ejercitan
/// es la misma que se ejecuta en vivo durante la sustentación.
/// </summary>
public sealed class CadenaAuditoriaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 8, 17, 39, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Entidad = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Una_cadena_vacia_se_considera_intacta()
    {
        var resultado = CadenaAuditoria.Verificar([]);

        Assert.True(resultado.Intacta);
        Assert.Equal(0, resultado.EventosRevisados);
    }

    [Fact]
    public void Una_cadena_bien_formada_verifica()
    {
        var cadena = ConstruirCadena(5);

        var resultado = CadenaAuditoria.Verificar(cadena);

        Assert.True(resultado.Intacta);
        Assert.Equal(5, resultado.EventosRevisados);
        Assert.Null(resultado.PrimerEventoAlterado);
    }

    /// <summary>
    /// Alguien elimina un evento intermedio. La cadena se rompe porque el hash anterior del
    /// siguiente ya no apunta a nada que esté presente.
    /// </summary>
    [Fact]
    public void Eliminar_un_evento_intermedio_rompe_la_cadena()
    {
        var cadena = ConstruirCadena(5).ToList();
        cadena.RemoveAt(2);

        var resultado = CadenaAuditoria.Verificar(cadena);

        Assert.False(resultado.Intacta);
        Assert.Contains("eliminó", resultado.Detalle, StringComparison.Ordinal);
    }

    /// <summary>
    /// Alguien inserta un evento que no pertenece a la cadena. Se detecta por el mismo mecanismo.
    /// </summary>
    [Fact]
    public void Insertar_un_evento_ajeno_rompe_la_cadena()
    {
        var cadena = ConstruirCadena(4).ToList();
        var intruso = new EventoAuditoria(
            Actor, AccionesAuditoria.ContenidoVetar, "Kudo", Entidad, "{}", null, "hash-inventado", Ahora);
        cadena.Insert(2, intruso);

        var resultado = CadenaAuditoria.Verificar(cadena);

        Assert.False(resultado.Intacta);
    }

    /// <summary>
    /// El caso que importa de verdad: un evento sigue en su sitio pero alguien le cambió los datos
    /// en la base. El enlace con el anterior encaja, pero su propio hash ya no corresponde.
    /// </summary>
    [Fact]
    public void Modificar_los_datos_de_un_evento_rompe_su_hash()
    {
        var original = ConstruirCadena(1)[0];

        // Se reconstruye el mismo evento con los datos alterados. En la base equivaldría a editar
        // la fila sin recalcular el hash, que es lo que haría alguien con acceso directo.
        var alterado = new EventoAuditoria(
            Actor, original.Accion, original.TipoEntidad, original.EntidadId,
            "{\"alterado\":true}", original.CorrelacionId, original.HashAnterior, Ahora);

        Assert.NotEqual(original.Hash, alterado.Hash);
    }

    /// <summary>
    /// El hash se calcula sobre la fecha truncada. Sin ese truncado, la verificación fallaría al
    /// releer el evento desde la base aunque nadie lo hubiera tocado, porque el motor guarda
    /// microsegundos y el valor recalculado no coincidiría (ADR-0013).
    /// </summary>
    [Fact]
    public void El_hash_sobrevive_a_la_precision_de_la_base_porque_la_fecha_va_truncada()
    {
        var conMicrosegundos = Ahora.AddTicks(7);

        var evento = new EventoAuditoria(
            Actor, AccionesAuditoria.KudoPublicar, "Kudo", Entidad, "{}", null, null, conMicrosegundos);

        // Recalcular sobre los datos guardados da el mismo hash: la fecha ya está truncada dentro.
        Assert.Equal(evento.Hash, evento.RecalcularHash());
        Assert.Equal(0, evento.OcurridoEn.UtcDateTime.Ticks % TimeSpan.TicksPerMillisecond);
    }

    [Fact]
    public void El_primer_evento_de_la_cadena_no_tiene_hash_anterior()
    {
        var primero = ConstruirCadena(1)[0];

        Assert.Null(primero.HashAnterior);
    }

    [Fact]
    public void Dos_eventos_con_los_mismos_datos_pero_distinto_anterior_tienen_hash_distinto()
    {
        var uno = new EventoAuditoria(Actor, AccionesAuditoria.KudoPublicar, "Kudo", Entidad, "{}", null, "aaa", Ahora);
        var otro = new EventoAuditoria(Actor, AccionesAuditoria.KudoPublicar, "Kudo", Entidad, "{}", null, "bbb", Ahora);

        // Es lo que hace que la cadena sea una cadena y no una lista de hashes independientes.
        Assert.NotEqual(uno.Hash, otro.Hash);
    }

    private static IReadOnlyList<EventoAuditoria> ConstruirCadena(int cuantos)
    {
        var eventos = new List<EventoAuditoria>();
        string? anterior = null;

        for (var i = 0; i < cuantos; i++)
        {
            var evento = new EventoAuditoria(
                Actor,
                AccionesAuditoria.KudoPublicar,
                "Kudo",
                Entidad,
                "{\"indice\":" + i + "}",
                "correlacion-1",
                anterior,
                Ahora.AddSeconds(i));

            eventos.Add(evento);
            anterior = evento.Hash;
        }

        return eventos;
    }
}
