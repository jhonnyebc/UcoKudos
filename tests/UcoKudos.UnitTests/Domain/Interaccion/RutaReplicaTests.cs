using UcoKudos.Domain.Interaccion;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.UnitTests.Domain.Interaccion;

/// <summary>
/// La ruta materializada es la decisión de ADR-0011 y es aritmética de cadenas, así que es el nivel
/// perfecto para pruebas unitarias.
///
/// La prueba que de verdad importa aquí es la del prefijo que coincide parcialmente: es el error
/// clásico de este patrón y, si estuviera presente, el veto en cascada ocultaría contenido ajeno.
/// </summary>
public sealed class RutaReplicaTests
{
    private static readonly Guid Raiz = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Hija = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Nieta = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    [Fact]
    public void Una_replica_de_primer_nivel_tiene_profundidad_uno()
    {
        var ruta = RutaReplica.DePrimerNivel(Raiz);

        Assert.Equal(1, ruta.Profundidad);
    }

    [Fact]
    public void Cada_nivel_suma_uno_a_la_profundidad()
    {
        var nivel1 = RutaReplica.DePrimerNivel(Raiz);
        var nivel2 = RutaReplica.Bajo(nivel1, Hija);
        var nivel3 = RutaReplica.Bajo(nivel2, Nieta);

        Assert.Equal(2, nivel2.Profundidad);
        Assert.Equal(3, nivel3.Profundidad);
    }

    [Fact]
    public void La_ruta_lleva_separador_al_principio_y_al_final()
    {
        var ruta = RutaReplica.DePrimerNivel(Raiz);

        Assert.StartsWith("/", ruta.Valor, StringComparison.Ordinal);
        Assert.EndsWith("/", ruta.Valor, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_descendiente_reconoce_a_su_ancestro()
    {
        var abuela = RutaReplica.DePrimerNivel(Raiz);
        var madre = RutaReplica.Bajo(abuela, Hija);
        var nieta = RutaReplica.Bajo(madre, Nieta);

        Assert.True(nieta.EstaBajo(abuela));
        Assert.True(nieta.EstaBajo(madre));
        Assert.True(madre.EstaBajo(abuela));
    }

    [Fact]
    public void Una_ruta_no_esta_bajo_si_misma()
    {
        var ruta = RutaReplica.DePrimerNivel(Raiz);

        Assert.False(ruta.EstaBajo(ruta));
    }

    [Fact]
    public void Dos_ramas_distintas_no_son_ancestras_una_de_otra()
    {
        var rama1 = RutaReplica.DePrimerNivel(Raiz);
        var rama2 = RutaReplica.DePrimerNivel(Hija);

        Assert.False(rama1.EstaBajo(rama2));
        Assert.False(rama2.EstaBajo(rama1));
    }

    /// <summary>
    /// El error clásico del árbol materializado: con identificadores de longitud variable y sin
    /// separador, el prefijo de «/1/» coincidiría con «/12/» y el veto en cascada ocultaría
    /// contenido de otra rama.
    ///
    /// Aquí no puede pasar porque los identificadores son de longitud fija (32 caracteres) y van
    /// entre separadores. Esta prueba lo fija como contrato.
    /// </summary>
    [Fact]
    public void El_prefijo_de_una_rama_no_coincide_parcialmente_con_otra()
    {
        var ruta = RutaReplica.DePrimerNivel(Raiz);
        var prefijo = ruta.PrefijoDeDescendientes();

        // Toda ruta de descendiente empieza por el prefijo completo, con su separador final.
        Assert.EndsWith("/", prefijo, StringComparison.Ordinal);

        // Y un identificador que comparte los primeros caracteres NO produce un prefijo compatible.
        var parecida = RutaReplica.DePrimerNivel(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011"));
        Assert.False(parecida.Valor.StartsWith(prefijo, StringComparison.Ordinal));
    }

    [Fact]
    public void Una_ruta_guardada_se_puede_reconstruir()
    {
        var original = RutaReplica.Bajo(RutaReplica.DePrimerNivel(Raiz), Hija);

        var reconstruida = RutaReplica.Desde(original.Valor);

        Assert.Equal(original, reconstruida);
        Assert.Equal(original.Profundidad, reconstruida.Profundidad);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sin-separadores")]
    [InlineData("/falta-el-final")]
    [InlineData("falta-el-inicio/")]
    public void Una_ruta_con_formato_invalido_se_rechaza(string valor)
    {
        Assert.Throws<ExcepcionDominio>(() => RutaReplica.Desde(valor));
    }
}
