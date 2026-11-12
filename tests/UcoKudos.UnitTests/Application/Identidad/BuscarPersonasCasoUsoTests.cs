using UcoKudos.Application.Comun;
using UcoKudos.Application.Identidad.CasosUso;
using UcoKudos.Application.Identidad.Entrada.Dto;
using UcoKudos.Application.Identidad.Salida.Puertos;
using UcoKudos.Domain.Identidad;

namespace UcoKudos.UnitTests.Application.Identidad;

/// <summary>
/// Las reglas del buscador que no dependen de la base: cuándo se consulta y con qué tamaño.
///
/// Se prueban aquí, y no contra PostgreSQL, porque son decisiones del caso de uso. Lo que sí
/// necesita base real es el SQL de la búsqueda sin acentos, que tiene su propia prueba de
/// integración.
/// </summary>
public sealed class BuscarPersonasCasoUsoTests
{
    /// <summary>Espía: recuerda con qué lo llamaron y si lo llamaron siquiera.</summary>
    private sealed class ConsultasEspia : IConsultasPersonas
    {
        public int Llamadas { get; private set; }
        public string? UltimoTexto { get; private set; }
        public int UltimoTamano { get; private set; }
        public Guid UltimoExcepto { get; private set; }

        public Task<IReadOnlyList<PersonaBuscadaDto>> BuscarAsync(
            string texto, Guid exceptoPersonaId, int tamano, CancellationToken ct)
        {
            Llamadas++;
            UltimoTexto = texto;
            UltimoTamano = tamano;
            UltimoExcepto = exceptoPersonaId;

            return Task.FromResult<IReadOnlyList<PersonaBuscadaDto>>(
                [new PersonaBuscadaDto(Guid.NewGuid(), "Ana Gómez")]);
        }
    }

    private static readonly Guid QuienBusca = Guid.NewGuid();

    private static ContextoPeticion Contexto() =>
        new(new AmbitoUsuario(QuienBusca, Rol.Docente, null, null), "correlacion-de-prueba");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    public async Task NoConsultaConTextoDemasiadoCorto(string? texto)
    {
        var consultas = new ConsultasEspia();
        var casoUso = new BuscarPersonasCasoUso(consultas);

        var resultado = await casoUso.EjecutarAsync(Contexto(), texto, null, CancellationToken.None);

        Assert.Empty(resultado);

        // Lo que de verdad importa: no llega a la base. Devolver vacío pero consultar igual
        // significaría una consulta por cada pulsación de teclado.
        Assert.Equal(0, consultas.Llamadas);
    }

    [Fact]
    public async Task RecortaLosEspaciosAntesDeBuscar()
    {
        var consultas = new ConsultasEspia();
        var casoUso = new BuscarPersonasCasoUso(consultas);

        await casoUso.EjecutarAsync(Contexto(), "  Gómez  ", null, CancellationToken.None);

        Assert.Equal("Gómez", consultas.UltimoTexto);
    }

    [Fact]
    public async Task ExcluyeAQuienBusca()
    {
        var consultas = new ConsultasEspia();
        var casoUso = new BuscarPersonasCasoUso(consultas);

        await casoUso.EjecutarAsync(Contexto(), "Ana", null, CancellationToken.None);

        Assert.Equal(QuienBusca, consultas.UltimoExcepto);
    }

    [Theory]
    [InlineData(null, 10)]
    [InlineData(5, 5)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(1_000_000, 25)]
    public async Task AcotaElTamanoPedido(int? pedido, int esperado)
    {
        var consultas = new ConsultasEspia();
        var casoUso = new BuscarPersonasCasoUso(consultas);

        await casoUso.EjecutarAsync(Contexto(), "Ana", pedido, CancellationToken.None);

        // Un `tamano` enorme sería una forma barata de cargar el servidor desde fuera.
        Assert.Equal(esperado, consultas.UltimoTamano);
    }
}
