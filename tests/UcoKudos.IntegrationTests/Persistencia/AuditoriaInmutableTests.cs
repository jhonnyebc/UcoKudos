using Microsoft.EntityFrameworkCore;
using UcoKudos.Domain.Auditoria;
using UcoKudos.IntegrationTests.Infraestructura;

namespace UcoKudos.IntegrationTests.Persistencia;

/// <summary>
/// La inmutabilidad de la auditoría, probada contra la base real (RF-17, ADR-0013).
///
/// Son las pruebas que demuestran las **dos** capas de protección, que es la parte que conviene tener
/// clara al sustentar:
///
///   - La cadena de hashes **detecta** una alteración. Eso se prueba con pruebas unitarias.
///   - El disparador de solo inserción la **impide**. Eso solo se puede probar aquí, contra el motor.
///
/// Y además verifica que el hash sobrevive al viaje por la base, que es el detalle que ADR-0013 avisa
/// que es fácil equivocar: si la fecha no se truncara, la verificación fallaría al releer aunque nadie
/// hubiera tocado nada.
/// </summary>
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Category", "Integration")]
public sealed class AuditoriaInmutableTests(BaseDeDatosFixture baseDatos) : IAsyncLifetime
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 8, 17, 39, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Entidad = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public async Task InitializeAsync() => await baseDatos.LimpiarAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// El disparador rechaza una actualización. Ni la propia aplicación puede alterar el registro:
    /// es lo que distingue una auditoría de una tabla que **se parece** a una auditoría.
    /// </summary>
    [Fact]
    public async Task La_base_rechaza_actualizar_un_evento_de_auditoria()
    {
        await SembrarCadenaAsync(3);

        await using var contexto = baseDatos.CrearContexto();

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            contexto.Database.ExecuteSqlRawAsync(
                "UPDATE audit.evento_auditoria SET datos = '{\"alterado\":true}' WHERE id = 1"));

        Assert.Contains("solo insercion", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>El disparador rechaza un borrado, por el mismo mecanismo.</summary>
    [Fact]
    public async Task La_base_rechaza_borrar_un_evento_de_auditoria()
    {
        await SembrarCadenaAsync(3);

        await using var contexto = baseDatos.CrearContexto();

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            contexto.Database.ExecuteSqlRawAsync("DELETE FROM audit.evento_auditoria WHERE id = 2"));

        Assert.Contains("solo insercion", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// El hash sobrevive al viaje por la base. Es la prueba que ADR-0013 señala como la delicada: el
    /// motor guarda microsegundos en su tipo de instante, así que sin truncar la fecha antes de
    /// calcular el hash, el valor recalculado al releer no coincidiría y la verificación fallaría sin
    /// que nada estuviera alterado.
    /// </summary>
    [Fact]
    public async Task El_hash_coincide_despues_de_releer_el_evento_desde_la_base()
    {
        await SembrarCadenaAsync(5);

        await using var contexto = baseDatos.CrearContexto();
        var eventos = await contexto.EventosAuditoria.OrderBy(e => e.Id).AsNoTracking().ToListAsync();

        Assert.Equal(5, eventos.Count);
        Assert.All(eventos, evento => Assert.Equal(evento.Hash, evento.RecalcularHash()));
    }

    /// <summary>
    /// La verificación completa de la cadena, releída desde la base. Es exactamente la operación que
    /// la plataforma expone como consulta y que se ejecuta en vivo durante la sustentación.
    /// </summary>
    [Fact]
    public async Task La_cadena_releida_desde_la_base_verifica_intacta()
    {
        await SembrarCadenaAsync(10);

        await using var contexto = baseDatos.CrearContexto();
        var eventos = await contexto.EventosAuditoria.OrderBy(e => e.Id).AsNoTracking().ToListAsync();

        var resultado = CadenaAuditoria.Verificar(eventos);

        Assert.True(resultado.Intacta, resultado.Detalle);
        Assert.Equal(10, resultado.EventosRevisados);
    }

    /// <summary>
    /// El JSON canónico sobrevive a la columna de tipo documento. El motor reordena las claves de un
    /// documento JSON al guardarlo, así que si el hash se calculara sobre un texto no estable, la
    /// verificación fallaría al releer.
    /// </summary>
    [Fact]
    public async Task El_hash_sobrevive_al_reordenamiento_de_claves_del_documento()
    {
        await using (var escritura = baseDatos.CrearContexto())
        {
            var evento = new EventoAuditoria(
                Actor,
                AccionesAuditoria.KudoPublicar,
                "Kudo",
                Entidad,
                "{\"zeta\":1,\"alfa\":2,\"media\":3}",
                "correlacion-orden",
                null,
                Ahora);

            escritura.EventosAuditoria.Add(evento);
            await escritura.SaveChangesAsync();
        }

        await using var lectura = baseDatos.CrearContexto();
        var releido = await lectura.EventosAuditoria.AsNoTracking().FirstAsync();

        Assert.Equal(releido.Hash, releido.RecalcularHash());
    }

    /// <summary>
    /// Los eventos de una misma petición se recuperan con un solo identificador. Es la consulta que
    /// sostiene QA-OBS-01: reconstruir qué pasó en una publicación concreta.
    /// </summary>
    [Fact]
    public async Task Los_eventos_de_una_peticion_se_recuperan_por_su_correlacion()
    {
        await SembrarCadenaAsync(4, "peticion-A");
        await SembrarCadenaAsync(3, "peticion-B", continuar: true);

        await using var contexto = baseDatos.CrearContexto();

        var deA = await contexto.EventosAuditoria.Where(e => e.CorrelacionId == "peticion-A").CountAsync();
        var deB = await contexto.EventosAuditoria.Where(e => e.CorrelacionId == "peticion-B").CountAsync();

        Assert.Equal(4, deA);
        Assert.Equal(3, deB);
    }

    private async Task SembrarCadenaAsync(int cuantos, string correlacion = "correlacion-1", bool continuar = false)
    {
        await using var contexto = baseDatos.CrearContexto();

        var anterior = continuar
            ? await contexto.EventosAuditoria.OrderByDescending(e => e.Id).Select(e => e.Hash).FirstOrDefaultAsync()
            : null;

        for (var i = 0; i < cuantos; i++)
        {
            var evento = new EventoAuditoria(
                Actor,
                AccionesAuditoria.KudoPublicar,
                "Kudo",
                Entidad,
                "{\"indice\":" + i + "}",
                correlacion,
                anterior,
                Ahora.AddSeconds(i));

            contexto.EventosAuditoria.Add(evento);
            anterior = evento.Hash;
        }

        await contexto.SaveChangesAsync();
    }
}
