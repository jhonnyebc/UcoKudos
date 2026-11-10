using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UcoKudos.Application.Kudos.Entrada.Dto;
using UcoKudos.Application.Kudos.Entrada.Puertos;
using UcoKudos.Domain.Identidad;
using UcoKudos.IntegrationTests.Infraestructura;

namespace UcoKudos.IntegrationTests.Persistencia;

/// <summary>
/// La consulta del muro, contra PostgreSQL real (RF-13, ADR-0010).
///
/// **Estas pruebas nacen de un fallo que llegó hasta el navegador.** La consulta compilaba, las
/// pruebas unitarias pasaban y el muro devolvía un 500 en cuanto se pedía: proyectaba primero y
/// ordenaba después sobre el resultado proyectado, y Entity Framework no supo traducir eso a SQL.
/// El compilador no puede detectarlo porque es LINQ válido; solo falla al ejecutarse contra una
/// base de datos de verdad.
///
/// Por eso viven aquí y no entre las unitarias: **una consulta solo está probada si se ha ejecutado
/// contra el motor**. Un doble en memoria habría dado verde con la consulta rota.
/// </summary>
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Category", "Integration")]
public sealed class ConsultaDelMuroTests(BaseDeDatosFixture baseDatos) : IAsyncLifetime
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

    /// <summary>
    /// La prueba de regresión del 500: pedir el muro vacío tiene que responder, no reventar.
    ///
    /// Parece trivial y es justo la que faltaba. El fallo no estaba en los datos sino en la forma
    /// de la consulta, así que se manifestaba **siempre**, incluso sin un solo Kudo.
    /// </summary>
    [Fact]
    public async Task El_muro_vacio_responde_sin_error()
    {
        var pagina = await ConsultarAsync();

        Assert.Empty(pagina.Elementos);
        Assert.Null(pagina.CursorSiguiente);
    }

    [Fact]
    public async Task El_muro_trae_el_kudo_con_los_nombres_resueltos()
    {
        await PublicarAsync("Gracias por quedarte a cerrar el laboratorio.");

        var pagina = await ConsultarAsync();

        var fila = Assert.Single(pagina.Elementos);
        Assert.Equal(_datos.AutorId, fila.AutorId);
        Assert.Equal(_datos.DestinatarioId, fila.DestinatarioId);

        // Los nombres vienen resueltos en la misma consulta: es lo que evita las N+1 que DRV-002
        // obliga a evitar. Si estuvieran vacíos, la unión no se estaría haciendo.
        Assert.False(string.IsNullOrWhiteSpace(fila.AutorNombre));
        Assert.False(string.IsNullOrWhiteSpace(fila.DestinatarioNombre));
        Assert.False(string.IsNullOrWhiteSpace(fila.CategoriaNombre));
    }

    /// <summary>
    /// El orden es del más reciente al más antiguo. Es lo que la pantalla espera, y además es el
    /// orden sobre el que se apoya el cursor: si se rompiera, la paginación repetiría o saltaría
    /// elementos sin que nada fallara visiblemente.
    /// </summary>
    [Fact]
    public async Task El_muro_ordena_del_mas_reciente_al_mas_antiguo()
    {
        await PublicarAsync("Primero");
        await PublicarAsync("Segundo");
        await PublicarAsync("Tercero");

        var pagina = await ConsultarAsync();

        Assert.Equal(3, pagina.Elementos.Count);

        var instantes = pagina.Elementos.Select(e => e.PublicadoEn).ToList();
        Assert.Equal(instantes.OrderByDescending(i => i).ToList(), instantes);
    }

    /// <summary>
    /// **La paginación por cursor no repite ni se salta elementos.**
    ///
    /// Es la propiedad que justifica el cursor frente al número de página (ADR-0010), y la única
    /// forma de comprobarla es recorrer las páginas de verdad y mirar el conjunto resultante.
    /// </summary>
    [Fact]
    public async Task Recorrer_el_muro_por_paginas_devuelve_cada_kudo_una_sola_vez()
    {
        for (var i = 1; i <= 5; i++)
        {
            await PublicarAsync("Reconocimiento número " + i);
        }

        var vistos = new List<Guid>();
        string? cursor = null;

        do
        {
            var pagina = await ConsultarAsync(cursor, tamano: 2);
            vistos.AddRange(pagina.Elementos.Select(e => e.Id));
            cursor = pagina.CursorSiguiente;
        }
        while (cursor is not null);

        Assert.Equal(5, vistos.Count);
        Assert.Equal(5, vistos.Distinct().Count());
    }

    /// <summary>Un Kudo vetado por moderación desaparece del muro (RF-15).</summary>
    [Fact]
    public async Task El_muro_no_muestra_lo_vetado()
    {
        var kudoId = await PublicarAsync("Esto se va a retirar.");

        await using (var contexto = baseDatos.CrearContexto())
        {
            var kudo = await contexto.Kudos.FirstAsync(k => k.Id == kudoId);
            kudo.Vetar();
            await contexto.SaveChangesAsync();
        }

        var pagina = await ConsultarAsync();

        Assert.Empty(pagina.Elementos);
    }

    private async Task<PaginaMuroDto> ConsultarAsync(string? cursor = null, int? tamano = null)
    {
        using var alcance = _plataforma.CrearAlcance();
        var puerto = alcance.ServiceProvider.GetRequiredService<IKudosPuertoEntrada>();

        return await puerto.MuroAsync(cursor, tamano, CancellationToken.None);
    }

    private async Task<Guid> PublicarAsync(string contenido)
    {
        using var alcance = _plataforma.CrearAlcance();
        var puerto = alcance.ServiceProvider.GetRequiredService<IKudosPuertoEntrada>();

        var resultado = await puerto.PublicarAsync(
            Semillas.ContextoDe(_datos.AutorId, Rol.Docente, _facultadId, _programaId, "peticion-muro"),
            new PublicarKudoDto(_datos.DestinatarioId, _datos.CategoriaId, contenido),
            CancellationToken.None);

        return resultado.Id;
    }
}
