using Microsoft.EntityFrameworkCore;
using UcoKudos.Infrastructure.Persistencia.Outbox;
using UcoKudos.IntegrationTests.Infraestructura;

namespace UcoKudos.IntegrationTests.Persistencia;

/// <summary>
/// La bandeja de eventos contra la base real (ADR-0005).
///
/// Son las pruebas de los escenarios que no se pueden verificar de otra forma: el comportamiento de
/// <c>FOR UPDATE SKIP LOCKED</c> lo implementa el motor, así que un doble en memoria no probaría nada.
///
/// La más importante respalda **QA-ELAS-01**: dos procesos compitiendo por la misma cola no toman el
/// mismo evento. Es lo que permite decir que el diseño está preparado para escalar horizontalmente
/// aunque hoy se despliegue una sola instancia.
/// </summary>
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Category", "Integration")]
public sealed class BandejaEventosTests(BaseDeDatosFixture baseDatos) : IAsyncLifetime
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 8, 17, 39, 0, TimeSpan.Zero);

    public async Task InitializeAsync() => await baseDatos.LimpiarAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// QA-ELAS-01: dos lectores simultáneos no toman el mismo evento.
    ///
    /// El primero toma el evento y lo bloquea; el segundo, al ver el bloqueo, **lo salta** en lugar de
    /// esperar. Eso es lo que hace `SKIP LOCKED`, y es la diferencia entre poder levantar una segunda
    /// instancia y duplicar todas las notificaciones.
    /// </summary>
    [Fact]
    public async Task Dos_lectores_simultaneos_no_toman_el_mismo_evento()
    {
        await SembrarAsync(1);

        await using var primerContexto = baseDatos.CrearContexto();
        await using var segundoContexto = baseDatos.CrearContexto();

        await using var primeraTransaccion = await primerContexto.Database.BeginTransactionAsync();
        var tomadosPorElPrimero = await TomarAsync(primerContexto);

        // El primero tiene el evento bloqueado y su transacción sigue abierta.
        Assert.Single(tomadosPorElPrimero);

        await using var segundaTransaccion = await segundoContexto.Database.BeginTransactionAsync();
        var tomadosPorElSegundo = await TomarAsync(segundoContexto);

        // El segundo no espera: salta el bloqueado y se va con las manos vacías.
        Assert.Empty(tomadosPorElSegundo);

        await primeraTransaccion.RollbackAsync();
        await segundaTransaccion.RollbackAsync();
    }

    /// <summary>
    /// Con dos eventos, dos lectores toman uno cada uno: el reparto funciona, no es que el segundo
    /// nunca pueda trabajar.
    /// </summary>
    [Fact]
    public async Task Con_dos_eventos_cada_lector_toma_uno()
    {
        await SembrarAsync(2);

        await using var primerContexto = baseDatos.CrearContexto();
        await using var segundoContexto = baseDatos.CrearContexto();

        await using var primeraTransaccion = await primerContexto.Database.BeginTransactionAsync();
        var delPrimero = await TomarAsync(primerContexto);

        await using var segundaTransaccion = await segundoContexto.Database.BeginTransactionAsync();
        var delSegundo = await TomarAsync(segundoContexto);

        Assert.Single(delPrimero);
        Assert.Single(delSegundo);
        Assert.NotEqual(delPrimero[0].Id, delSegundo[0].Id);

        await primeraTransaccion.RollbackAsync();
        await segundaTransaccion.RollbackAsync();
    }

    /// <summary>
    /// QA-CONF-01: si el proceso muere antes de confirmar, el evento **queda pendiente**, no a medio
    /// procesar.
    ///
    /// Se simula deshaciendo la transacción, que es exactamente lo que ocurre cuando el proceso se
    /// cae: el bloqueo se libera y el evento vuelve a estar disponible. Esto es lo que hace que
    /// ninguna notificación se pierda.
    /// </summary>
    [Fact]
    public async Task Si_la_transaccion_se_deshace_el_evento_sigue_pendiente()
    {
        await SembrarAsync(1);

        await using (var contexto = baseDatos.CrearContexto())
        {
            await using var transaccion = await contexto.Database.BeginTransactionAsync();

            var tomados = await TomarAsync(contexto);
            Assert.Single(tomados);

            tomados[0].MarcarProcesado(Ahora);
            await contexto.SaveChangesAsync();

            // El proceso muere aquí: la transacción nunca se confirma.
            await transaccion.RollbackAsync();
        }

        await using var verificacion = baseDatos.CrearContexto();
        var pendientes = await verificacion.MensajesOutbox.CountAsync(m => m.ProcesadoEn == null);

        Assert.Equal(1, pendientes);
    }

    /// <summary>
    /// Un evento programado al futuro no se toma todavía. Es lo que permite que un recordatorio no
    /// necesite un planificador aparte.
    /// </summary>
    [Fact]
    public async Task Un_evento_programado_al_futuro_no_se_toma()
    {
        await using (var contexto = baseDatos.CrearContexto())
        {
            contexto.MensajesOutbox.Add(new MensajeOutbox(
                Guid.CreateVersion7(Ahora), "prueba.futuro", "{}", Ahora, Ahora.AddHours(1), null));

            await contexto.SaveChangesAsync();
        }

        await using var lectura = baseDatos.CrearContexto();
        await using var transaccion = await lectura.Database.BeginTransactionAsync();

        var tomados = await TomarAsync(lectura);

        Assert.Empty(tomados);
        await transaccion.RollbackAsync();
    }

    /// <summary>
    /// El reintento con espera creciente, y el descarte al agotar los intentos. Un evento descartado
    /// queda marcado como procesado para que no se reintente eternamente, y conserva el error.
    /// </summary>
    [Fact]
    public async Task Al_agotar_los_intentos_el_evento_se_descarta_y_conserva_el_error()
    {
        await SembrarAsync(1);

        await using var contexto = baseDatos.CrearContexto();
        var evento = await contexto.MensajesOutbox.FirstAsync();

        var esperaAnterior = evento.ProximoIntento;

        for (var intento = 1; intento <= MensajeOutbox.MaximoIntentos; intento++)
        {
            evento.RegistrarFallo("fallo simulado " + intento, Ahora);

            if (intento < MensajeOutbox.MaximoIntentos)
            {
                // La espera crece en cada intento.
                Assert.True(evento.ProximoIntento > esperaAnterior);
                esperaAnterior = evento.ProximoIntento;
                Assert.Null(evento.ProcesadoEn);
            }
        }

        await contexto.SaveChangesAsync();

        Assert.NotNull(evento.ProcesadoEn);
        Assert.Contains("fallo simulado", evento.UltimoError ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// El contexto de la traza se guarda y se recupera. Es lo que hace que la traza cruce la frontera
    /// asíncrona (QA-OBS-01): sin este campo, la publicación y sus avisos serían trazas inconexas.
    /// </summary>
    [Fact]
    public async Task El_contexto_de_traza_viaja_con_el_evento()
    {
        const string traza = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

        await using (var contexto = baseDatos.CrearContexto())
        {
            contexto.MensajesOutbox.Add(new MensajeOutbox(
                Guid.CreateVersion7(Ahora), "prueba.traza", "{}", Ahora, Ahora, traza));

            await contexto.SaveChangesAsync();
        }

        await using var lectura = baseDatos.CrearContexto();
        var evento = await lectura.MensajesOutbox.AsNoTracking().FirstAsync();

        Assert.Equal(traza, evento.ContextoTraza);
    }

    /// <summary>La consulta real del proceso, con el bloqueo que salta las filas tomadas.</summary>
    private static async Task<List<MensajeOutbox>> TomarAsync(Infrastructure.Persistencia.UcoKudosDbContext contexto) =>
        await contexto.MensajesOutbox
            .FromSql(
                $"""
                 SELECT * FROM notif.outbox_mensaje
                 WHERE procesado_en IS NULL AND proximo_intento <= {Ahora}
                 ORDER BY proximo_intento, ocurrido_en
                 LIMIT 1
                 FOR UPDATE SKIP LOCKED
                 """)
            .ToListAsync();

    private async Task SembrarAsync(int cuantos)
    {
        await using var contexto = baseDatos.CrearContexto();

        for (var i = 0; i < cuantos; i++)
        {
            contexto.MensajesOutbox.Add(new MensajeOutbox(
                Guid.CreateVersion7(Ahora.AddSeconds(i)),
                "prueba.evento",
                "{\"indice\":" + i + "}",
                Ahora.AddSeconds(i),
                // El próximo intento es Ahora para todos, no Ahora+i: el filtro del proceso es
                // «proximo_intento <= ahora», así que sembrarlos en el futuro los dejaría fuera y la
                // prueba no estaría midiendo lo que cree.
                Ahora,
                null));
        }

        await contexto.SaveChangesAsync();
    }
}
