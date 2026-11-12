using UcoKudos.Infrastructure.Lectura;
using UcoKudos.IntegrationTests.Infraestructura;

namespace UcoKudos.IntegrationTests.Persistencia;

/// <summary>
/// La búsqueda de personas contra PostgreSQL real.
///
/// Va contra base real y no con dobles porque **lo que hay que verificar es el SQL**: la
/// comparación sin acentos se hace con `translate` dentro de la consulta, y eso un doble no lo
/// ejecuta. Una prueba con datos en memoria pasaría aunque la consulta estuviera mal escrita.
/// </summary>
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Category", "Integration")]
public sealed class BusquedaDePersonasTests(BaseDeDatosFixture baseDatos) : IAsyncLifetime
{
    private DatosSembrados _datos = null!;

    public async Task InitializeAsync()
    {
        await baseDatos.LimpiarAsync();

        await using var contexto = baseDatos.CrearContexto();
        _datos = await Semillas.SembrarAsync(contexto, PlataformaFixture.Ahora);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<IReadOnlyList<string>> BuscarAsync(
        string texto, Guid? excepto = null, int tamano = 10)
    {
        await using var contexto = baseDatos.CrearContexto();
        var consultas = new ConsultasPersonasEf(contexto);

        var filas = await consultas.BuscarAsync(
            texto, excepto ?? Guid.NewGuid(), tamano, CancellationToken.None);

        return [.. filas.Select(f => f.NombreCompleto)];
    }

    [Fact]
    public async Task EncuentraPorNombreExacto()
    {
        var nombres = await BuscarAsync("Gómez");

        Assert.Contains("Ana Gómez", nombres);
    }

    /// <summary>
    /// **La prueba que justifica el `translate` de la consulta.**
    ///
    /// Nadie escribe las tildes en una caja de búsqueda. Si «gomez» no encuentra «Gómez», el
    /// buscador es inútil justo para los apellidos más frecuentes, y el fallo no se nota en
    /// desarrollo si los datos de prueba no llevan acentos.
    /// </summary>
    [Theory]
    [InlineData("gomez")]
    [InlineData("GOMEZ")]
    [InlineData("Gomez")]
    [InlineData("gómez")]
    public async Task EncuentraAunqueFaltenLasTildes(string texto)
    {
        var nombres = await BuscarAsync(texto);

        Assert.Contains("Ana Gómez", nombres);
    }

    [Fact]
    public async Task EncuentraPorElNombreYNoSoloPorElApellido()
    {
        var nombres = await BuscarAsync("Carlos");

        Assert.Contains("Carlos Mejía", nombres);
    }

    [Fact]
    public async Task NoDevuelveAQuienBusca()
    {
        var nombres = await BuscarAsync("a", excepto: _datos.AutorId);

        // El dominio prohíbe reconocerse a uno mismo, así que ofrecerse como destinatario solo
        // llevaría a un error evitable.
        Assert.DoesNotContain("Ana Gómez", nombres);
    }

    /// <summary>
    /// El comodín de LIKE escrito por el usuario no debe comportarse como comodín.
    ///
    /// Sin escapar, «%» devolvería el directorio entero a quien lo escriba, que es justo lo que el
    /// mínimo de dos caracteres intenta evitar.
    /// </summary>
    [Theory]
    [InlineData("%%")]
    [InlineData("__")]
    public async Task LosComodinesDeLikeNoDevuelvenATodoElMundo(string texto)
    {
        var nombres = await BuscarAsync(texto);

        Assert.Empty(nombres);
    }

    [Fact]
    public async Task RespetaElLimitePedido()
    {
        var nombres = await BuscarAsync("a", tamano: 1);

        Assert.True(nombres.Count <= 1);
    }

    /// <summary>
    /// Las dos cadenas de <c>translate</c> tienen que medir lo mismo.
    ///
    /// Si alguien añade una letra a una sola, PostgreSQL **borra** los caracteres sobrantes en
    /// lugar de fallar, y la búsqueda empieza a devolver resultados raros sin que nada avise. Esta
    /// comprobación es barata y convierte un fallo silencioso en una prueba en rojo.
    /// </summary>
    [Fact]
    public async Task LaTablaDeAcentosEstaEmparejada()
    {
        // Si las cadenas estuvieran descuadradas, «Mejía» dejaría de encontrarse por «mejia».
        var nombres = await BuscarAsync("mejia");

        Assert.Contains("Carlos Mejía", nombres);
    }
}
