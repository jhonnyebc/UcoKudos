using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UcoKudos.Application.Comun.Entrada.Puertos;
using UcoKudos.Application.Kudos.Entrada.Dto;
using UcoKudos.Application.Kudos.Entrada.Puertos;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Transversal;
using UcoKudos.IntegrationTests.Infraestructura;

namespace UcoKudos.IntegrationTests.Persistencia;

/// <summary>
/// La bandeja de avisos (RF-16), contra PostgreSQL real.
///
/// Es la pieza que hace cierta la promesa de ADR-0009: el canal en vivo es efímero y Web Push puede
/// no llegar nunca, así que el aviso se guarda siempre y aquí se comprueba que se puede recuperar.
///
/// Se prueba **a través del caso de uso y con el contexto de una persona**, no consultando la tabla
/// directamente, porque lo que hay que verificar no es solo que la fila esté: es que cada quien ve
/// lo suyo y nada más.
/// </summary>
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Category", "Integration")]
public sealed class BandejaDeAvisosTests(BaseDeDatosFixture baseDatos) : IAsyncLifetime
{
    private PlataformaFixture _plataforma = null!;
    private DatosSembrados _datos = null!;
    private Guid _facultadId;
    private Guid _programaId;

    public async Task InitializeAsync()
    {
        await baseDatos.LimpiarAsync();

        _plataforma = new PlataformaFixture(baseDatos);

        await using var contexto = baseDatos.CrearContexto();
        _datos = await Semillas.SembrarAsync(contexto, PlataformaFixture.Ahora);

        var asignacion = await contexto.AsignacionesRol.FirstAsync(a => a.PersonaId == _datos.AutorId);
        _facultadId = asignacion.FacultadId!.Value;
        _programaId = asignacion.ProgramaId!.Value;
    }

    public Task DisposeAsync()
    {
        _plataforma.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Una_bandeja_sin_avisos_responde_vacia()
    {
        var pagina = await BandejaDe(_datos.DestinatarioId);

        Assert.Empty(pagina.Elementos);
        Assert.Equal(0, pagina.SinLeer);
    }

    /// <summary>
    /// El flujo completo: se publica un Kudo, se procesa la bandeja de salida y el destinatario
    /// encuentra su aviso. Es el camino real, no una fila insertada a mano.
    /// </summary>
    [Fact]
    public async Task El_destinatario_encuentra_el_aviso_del_kudo_que_recibio()
    {
        await PublicarYProcesarAsync("Gracias por el acompañamiento de esta semana.");

        var pagina = await BandejaDe(_datos.DestinatarioId);

        var aviso = Assert.Single(pagina.Elementos);
        Assert.Equal(1, pagina.SinLeer);
        Assert.False(aviso.Leido);

        // El texto llega compuesto a partir de la clave y las variables guardadas, no vacío ni con
        // marcadores sin rellenar.
        Assert.False(string.IsNullOrWhiteSpace(aviso.Titulo));
        Assert.False(string.IsNullOrWhiteSpace(aviso.Cuerpo));
        Assert.DoesNotContain("{0}", aviso.Cuerpo, StringComparison.Ordinal);
    }

    /// <summary>
    /// **La prueba que de verdad importa: nadie ve los avisos de otro.**
    ///
    /// El identificador de persona no llega por parámetro sino del contexto de la petición, que
    /// sale del token. Esta prueba comprueba que esa decisión funciona: el autor del Kudo no ve el
    /// aviso que se generó para el destinatario.
    /// </summary>
    [Fact]
    public async Task Nadie_ve_los_avisos_de_otra_persona()
    {
        await PublicarYProcesarAsync("Un reconocimiento para ti.");

        var delDestinatario = await BandejaDe(_datos.DestinatarioId);
        var delAutor = await BandejaDe(_datos.AutorId);

        Assert.NotEmpty(delDestinatario.Elementos);
        Assert.Empty(delAutor.Elementos);
    }

    [Fact]
    public async Task Marcar_como_leido_baja_el_contador()
    {
        await PublicarYProcesarAsync("Gracias por todo.");

        var antes = await BandejaDe(_datos.DestinatarioId);
        Assert.Equal(1, antes.SinLeer);

        await MarcarLeidoAsync(_datos.DestinatarioId, antes.Elementos[0].Id);

        var despues = await BandejaDe(_datos.DestinatarioId);
        Assert.Equal(0, despues.SinLeer);
        Assert.True(despues.Elementos[0].Leido);
    }

    /// <summary>
    /// **Marcar como leído un aviso ajeno se rechaza.**
    ///
    /// Sin esto, bastaría con probar identificadores para ir marcando los avisos de los demás. Y el
    /// error es el mismo que el de un aviso inexistente, a propósito: distinguirlos dejaría
    /// averiguar qué avisos tiene cada quien.
    /// </summary>
    [Fact]
    public async Task No_se_puede_marcar_como_leido_el_aviso_de_otro()
    {
        await PublicarYProcesarAsync("Para el destinatario.");

        var delDestinatario = await BandejaDe(_datos.DestinatarioId);
        var ajeno = delDestinatario.Elementos[0].Id;

        await Assert.ThrowsAsync<ExcepcionAutorizacion>(
            () => MarcarLeidoAsync(_datos.AutorId, ajeno));

        // Y sigue sin leer: el intento no tuvo ningún efecto.
        var sigueIgual = await BandejaDe(_datos.DestinatarioId);
        Assert.Equal(1, sigueIgual.SinLeer);
    }

    [Fact]
    public async Task Un_aviso_inexistente_falla_igual_que_uno_ajeno()
    {
        await Assert.ThrowsAsync<ExcepcionAutorizacion>(
            () => MarcarLeidoAsync(_datos.DestinatarioId, 999_999));
    }

    /// <summary>Los más recientes primero, que es como se lee una bandeja.</summary>
    [Fact]
    public async Task Los_avisos_llegan_del_mas_reciente_al_mas_antiguo()
    {
        await PublicarYProcesarAsync("Primero");
        await PublicarYProcesarAsync("Segundo");
        await PublicarYProcesarAsync("Tercero");

        var pagina = await BandejaDe(_datos.DestinatarioId);

        Assert.Equal(3, pagina.Elementos.Count);
        var instantes = pagina.Elementos.Select(e => e.GeneradoEn).ToList();
        Assert.Equal(instantes.OrderByDescending(i => i).ToList(), instantes);
    }

    /// <summary>
    /// El tamaño se acota en el servidor: un `tamano` enorme pedido desde fuera no debe convertirse
    /// en una consulta enorme.
    /// </summary>
    [Fact]
    public async Task El_tamano_pedido_se_respeta_y_se_acota()
    {
        for (var i = 0; i < 4; i++)
        {
            await PublicarYProcesarAsync("Reconocimiento " + i);
        }

        var dos = await BandejaDe(_datos.DestinatarioId, tamano: 2);
        Assert.Equal(2, dos.Elementos.Count);

        // Aunque solo se pidan dos, el contador sigue diciendo cuántos hay sin leer en total.
        Assert.Equal(4, dos.SinLeer);

        var enorme = await BandejaDe(_datos.DestinatarioId, tamano: 1_000_000);
        Assert.Equal(4, enorme.Elementos.Count);
    }

    private async Task<Application.Notificaciones.Entrada.Dto.PaginaAvisosDto> BandejaDe(
        Guid personaId, int? tamano = null)
    {
        using var alcance = _plataforma.CrearAlcance();
        var puerto = alcance.ServiceProvider.GetRequiredService<IAvisosPuertoEntrada>();

        return await puerto.MisAvisosAsync(ContextoDe(personaId), tamano, CancellationToken.None);
    }

    private async Task MarcarLeidoAsync(Guid personaId, long avisoId)
    {
        using var alcance = _plataforma.CrearAlcance();
        var puerto = alcance.ServiceProvider.GetRequiredService<IAvisosPuertoEntrada>();

        await puerto.MarcarLeidoAsync(ContextoDe(personaId), avisoId, CancellationToken.None);
    }

    private async Task PublicarYProcesarAsync(string contenido)
    {
        using (var alcance = _plataforma.CrearAlcance())
        {
            var puerto = alcance.ServiceProvider.GetRequiredService<IKudosPuertoEntrada>();

            await puerto.PublicarAsync(
                ContextoDe(_datos.AutorId),
                new PublicarKudoDto(_datos.DestinatarioId, _datos.CategoriaId, contenido),
                CancellationToken.None);
        }

        await _plataforma.Proceso.ProcesarVueltaAsync(CancellationToken.None);
    }

    private Application.Comun.ContextoPeticion ContextoDe(Guid personaId) =>
        Semillas.ContextoDe(personaId, Rol.Docente, _facultadId, _programaId, "peticion-avisos");
}
