using UcoKudos.Domain.Interaccion;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.UnitTests.Domain.Interaccion;

/// <summary>
/// RF-09: réplicas sobre Kudos y sobre réplicas, sin límite de profundidad, cantidad ni longitud.
///
/// La prueba del límite técnico es la interesante: el enunciado dice «sin límite de profundidad», y
/// la defensa de ADR-0011 lo respeta porque al alcanzar el máximo la réplica **se acepta igual**,
/// no se rechaza.
/// </summary>
public sealed class ReplicaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 8, 17, 39, 0, TimeSpan.Zero);
    private static readonly Guid Kudo = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Autor = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Una_replica_de_primer_nivel_cuelga_del_kudo_y_no_tiene_padre()
    {
        var replica = Replica.SobreKudo(Kudo, Autor, "De acuerdo con esto.", Ahora);

        Assert.Equal(Kudo, replica.KudoId);
        Assert.Null(replica.ReplicaPadreId);
        Assert.Equal(1, replica.Profundidad);
    }

    [Fact]
    public void Una_replica_sobre_otra_hereda_el_kudo_y_apunta_a_su_padre()
    {
        var madre = Replica.SobreKudo(Kudo, Autor, "Primera.", Ahora);

        var hija = Replica.SobreReplica(madre, Autor, "Respuesta.", Ahora.AddMinutes(1));

        Assert.Equal(Kudo, hija.KudoId);
        Assert.Equal(madre.Id, hija.ReplicaPadreId);
        Assert.Equal(2, hija.Profundidad);
    }

    /// <summary>
    /// La ruta de la hija empieza por la de la madre. Es lo que permite leer el hilo completo en una
    /// consulta y vetar en cascada en una operación.
    /// </summary>
    [Fact]
    public void La_ruta_de_una_hija_empieza_por_la_ruta_de_su_madre()
    {
        var madre = Replica.SobreKudo(Kudo, Autor, "Primera.", Ahora);
        var hija = Replica.SobreReplica(madre, Autor, "Respuesta.", Ahora.AddMinutes(1));

        Assert.StartsWith(madre.Ruta, hija.Ruta, StringComparison.Ordinal);
        Assert.True(hija.RutaMaterializada.EstaBajo(madre.RutaMaterializada));
    }

    [Fact]
    public void El_identificador_de_la_replica_coincide_con_el_ultimo_tramo_de_su_ruta()
    {
        var replica = Replica.SobreKudo(Kudo, Autor, "Contenido.", Ahora);

        // Si no coincidieran, la ruta y la entidad apuntarían a cosas distintas y el veto en
        // cascada se equivocaría.
        Assert.Contains(replica.Id.ToString("N"), replica.Ruta, StringComparison.Ordinal);
    }

    [Fact]
    public void No_se_puede_responder_a_una_replica_vetada()
    {
        var madre = Replica.SobreKudo(Kudo, Autor, "Primera.", Ahora);
        madre.Vetar();

        Assert.Throws<ExcepcionDominio>(() => Replica.SobreReplica(madre, Autor, "Respuesta.", Ahora));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void El_contenido_de_la_replica_es_obligatorio(string contenido)
    {
        Assert.Throws<ExcepcionDominio>(() => Replica.SobreKudo(Kudo, Autor, contenido, Ahora));
    }

    [Fact]
    public void El_contenido_de_la_replica_no_tiene_limite_de_longitud()
    {
        var replica = Replica.SobreKudo(Kudo, Autor, new string('b', 50_000), Ahora);

        Assert.Equal(50_000, replica.Contenido.Length);
    }

    /// <summary>
    /// Sin límite de cantidad (RF-09): se pueden colgar muchas réplicas del mismo padre, y todas
    /// quedan al mismo nivel.
    /// </summary>
    [Fact]
    public void Un_mismo_padre_admite_muchas_replicas_al_mismo_nivel()
    {
        var madre = Replica.SobreKudo(Kudo, Autor, "Primera.", Ahora);

        var hijas = Enumerable.Range(0, 50)
            .Select(i => Replica.SobreReplica(madre, Autor, "Respuesta " + i, Ahora.AddSeconds(i)))
            .ToList();

        Assert.All(hijas, h => Assert.Equal(2, h.Profundidad));
        Assert.Equal(50, hijas.Select(h => h.Id).Distinct().Count());
    }

    /// <summary>
    /// El límite técnico de ADR-0011: al alcanzar la profundidad máxima, la réplica **se acepta** y
    /// se queda a ese nivel en lugar de bajar otro. Así se respeta «sin límite de profundidad» del
    /// enunciado sin degradar el índice de prefijo.
    /// </summary>
    [Fact]
    public void Al_alcanzar_la_profundidad_maxima_la_replica_se_acepta_y_no_baja_mas()
    {
        var actual = Replica.SobreKudo(Kudo, Autor, "Nivel 1", Ahora);

        for (var i = 2; i <= RutaReplica.ProfundidadMaxima; i++)
            actual = Replica.SobreReplica(actual, Autor, "Nivel " + i, Ahora.AddSeconds(i));

        Assert.Equal(RutaReplica.ProfundidadMaxima, actual.Profundidad);

        // Una más: no lanza, y la profundidad no crece.
        var siguiente = Replica.SobreReplica(actual, Autor, "Una más", Ahora.AddHours(1));

        Assert.Equal(RutaReplica.ProfundidadMaxima, siguiente.Profundidad);
        Assert.Equal(actual.Id, siguiente.ReplicaPadreId);
    }
}
