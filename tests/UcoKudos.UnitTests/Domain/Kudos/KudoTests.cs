using UcoKudos.Domain.Kudos;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.UnitTests.Domain.Kudos;

/// <summary>
/// El agregado central (RF-07). Las invariantes se validan al construir, así que probarlas es
/// intentar construir una instancia inválida: no hace falta base de datos ni servidor.
/// </summary>
public sealed class KudoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 8, 17, 39, 0, TimeSpan.Zero);
    private static readonly Guid Autor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Destinatario = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Categoria = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static Kudo Nuevo(string contenido = "Gracias por acompañar al grupo toda la semana.") =>
        new(Autor, Destinatario, Categoria, contenido, Ahora);

    [Fact]
    public void Un_kudo_recien_publicado_no_esta_vetado_y_no_tiene_interaccion()
    {
        var kudo = Nuevo();

        Assert.False(kudo.Vetado);
        Assert.Equal(0, kudo.TotalReplicas);
        Assert.Equal(0, kudo.TotalCalificaciones);
        Assert.Null(kudo.PromedioCalificacion);
        Assert.Empty(kudo.Adjuntos);
    }

    /// <summary>
    /// La invariante más característica: un reconocimiento es para otra persona. Si esto se validara
    /// en el endpoint, el segundo endpoint que cree Kudos se olvidaría de comprobarlo.
    /// </summary>
    [Fact]
    public void No_se_puede_publicar_un_kudo_para_uno_mismo()
    {
        var error = Assert.Throws<ExcepcionDominio>(() => new Kudo(Autor, Autor, Categoria, "Me felicito.", Ahora));

        Assert.Contains("uno mismo", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void El_contenido_es_obligatorio(string contenido)
    {
        Assert.Throws<ExcepcionDominio>(() => Nuevo(contenido));
    }

    /// <summary>RN-03: el contenido no tiene límite de longitud.</summary>
    [Fact]
    public void El_contenido_no_tiene_limite_de_longitud()
    {
        var muyLargo = new string('a', 100_000);

        var kudo = Nuevo(muyLargo);

        Assert.Equal(100_000, kudo.Contenido.Length);
    }

    [Fact]
    public void Faltar_el_autor_el_destinatario_o_la_categoria_impide_construirlo()
    {
        Assert.Throws<ExcepcionDominio>(() => new Kudo(Guid.Empty, Destinatario, Categoria, "x", Ahora));
        Assert.Throws<ExcepcionDominio>(() => new Kudo(Autor, Guid.Empty, Categoria, "x", Ahora));
        Assert.Throws<ExcepcionDominio>(() => new Kudo(Autor, Destinatario, Guid.Empty, "x", Ahora));
    }

    [Fact]
    public void Se_pueden_agregar_adjuntos_hasta_el_maximo()
    {
        var kudo = Nuevo();

        for (var i = 0; i < Adjunto.MaximoPorKudo; i++)
            kudo.AgregarAdjunto("clave-" + i, "archivo-" + i + ".pdf", Autor, Ahora);

        Assert.Equal(Adjunto.MaximoPorKudo, kudo.Adjuntos.Count);
        Assert.Throws<ExcepcionDominio>(() => kudo.AgregarAdjunto("uno-mas", "x.pdf", Autor, Ahora));
    }

    [Fact]
    public void Vetar_dos_veces_el_mismo_kudo_es_un_error()
    {
        var kudo = Nuevo();
        kudo.Vetar();

        Assert.True(kudo.Vetado);
        Assert.Throws<ExcepcionDominio>(kudo.Vetar);
    }

    [Fact]
    public void Los_contadores_materializados_se_fijan_desde_el_caso_de_uso()
    {
        var kudo = Nuevo();

        kudo.FijarTotalReplicas(14);
        kudo.FijarCalificacion(4, 4.25m);

        Assert.Equal(14, kudo.TotalReplicas);
        Assert.Equal(4, kudo.TotalCalificaciones);
        Assert.Equal(4.25m, kudo.PromedioCalificacion);
    }

    [Fact]
    public void Sin_calificaciones_el_promedio_queda_nulo()
    {
        var kudo = Nuevo();
        kudo.FijarCalificacion(3, 4m);

        kudo.FijarCalificacion(0, null);

        Assert.Null(kudo.PromedioCalificacion);
    }

    [Theory]
    [InlineData(0.9)]
    [InlineData(5.1)]
    public void Un_promedio_fuera_de_la_escala_de_estrellas_se_rechaza(double promedio)
    {
        var kudo = Nuevo();

        Assert.Throws<ExcepcionDominio>(() => kudo.FijarCalificacion(1, (decimal)promedio));
    }

    [Fact]
    public void Un_contador_negativo_se_rechaza()
    {
        var kudo = Nuevo();

        Assert.Throws<ExcepcionDominio>(() => kudo.FijarTotalReplicas(-1));
        Assert.Throws<ExcepcionDominio>(() => kudo.FijarCalificacion(-1, null));
    }
}
