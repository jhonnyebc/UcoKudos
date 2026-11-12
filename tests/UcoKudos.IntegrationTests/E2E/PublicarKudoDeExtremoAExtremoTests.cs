using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UcoKudos.Application.Identidad.Entrada.Dto;
using UcoKudos.Application.Interaccion.Entrada.Puertos;
using UcoKudos.Application.Kudos.Entrada.Dto;
using UcoKudos.Application.Notificaciones.Entrada.Dto;
using UcoKudos.Domain.Identidad;
using UcoKudos.Infrastructure.Canal;
using UcoKudos.Infrastructure.Notificaciones;
using UcoKudos.IntegrationTests.Infraestructura;

namespace UcoKudos.IntegrationTests.E2E;

/// <summary>
/// Escenarios completos contra la API levantada de verdad (pirámide de pruebas, nivel
/// extremo a extremo).
///
/// **Qué los distingue de las pruebas de integración**, porque conviene saber decirlo: aquellas
/// invocan casos de uso con un contexto ya construido; estas **hacen peticiones HTTP** y recorren
/// todo el camino de una petición real. Por eso cogen una clase de fallo que las otras no ven:
/// una ruta mal registrada, una autorización que no se exige, un error de dominio que llega al
/// cliente como un 500 o como un mensaje genérico, un contrato que cambia de forma.
///
/// Dos de estos escenarios existen por defectos reales de esta misma plataforma que ninguna prueba
/// anterior detectó: el mensaje de «no puedes reconocerte a ti mismo», que el cliente descartaba,
/// y la verificación de auditoría, que estaba implementada y era inalcanzable.
/// </summary>
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Category", "E2E")]
public sealed class PublicarKudoDeExtremoAExtremoTests(BaseDeDatosFixture baseDatos) : IAsyncLifetime
{
    private const string CorreoAutor = "docente@uco.edu.co";
    private const string CorreoDestinatario = "estudiante@uco.edu.co";
    private const string CorreoRector = "rector@uco.edu.co";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private PlataformaWebFactory _web = null!;
    private DatosSembrados _datos = null!;

    public async Task InitializeAsync()
    {
        await baseDatos.LimpiarAsync();

        var ahora = DateTimeOffset.UtcNow;
        await using var contexto = baseDatos.CrearContexto();
        _datos = await Semillas.SembrarAsync(contexto, ahora);

        // Un Rector, que es quien puede consultar la auditoría. Tiene ámbito de universidad, así
        // que no lleva facultad ni programa.
        var rector = Semillas.Activa(new Persona(CorreoRector, "+573009990000", "Marta Restrepo", ahora));
        contexto.Personas.Add(rector);
        contexto.AsignacionesRol.Add(new AsignacionRol(rector.Id, Rol.Rector, null, null, ahora));
        await contexto.SaveChangesAsync();

        _web = new PlataformaWebFactory(baseDatos.CadenaConexion);
    }

    public async Task DisposeAsync() => await _web.DisposeAsync();

    /// <summary>Un cliente que hace las peticiones como esa persona. Sin argumento, anónimo.</summary>
    private HttpClient Como(string? correo = null)
    {
        var cliente = _web.CreateClient();

        if (correo is not null)
        {
            cliente.DefaultRequestHeaders.Add(EsquemaDePrueba.Cabecera, correo);
        }

        return cliente;
    }

    /// <summary>Vacía la bandeja, como lo haría el despachador en segundo plano.</summary>
    private async Task DespacharAsync()
    {
        var proceso = _web.Services.GetServices<IHostedService>().OfType<ProcesoNotificaciones>().Single();

        // Una vuelta atiende un evento: se repite hasta que no quede ninguno, que es lo que haría
        // el temporizador real en unos segundos.
        while (await proceso.ProcesarVueltaAsync(CancellationToken.None) > 0)
        {
        }
    }

    [Fact]
    public async Task Publicar_un_Kudo_cumple_los_cinco_pasos_del_enunciado()
    {
        using var ana = Como(CorreoAutor);
        using var carlos = Como(CorreoDestinatario);
        using var rector = Como(CorreoRector);

        // Paso 1: el Kudo se almacena, y se ve en el muro del destinatario.
        var respuesta = await ana.PostAsJsonAsync(
            "/api/kudos",
            new PublicarKudoDto(_datos.DestinatarioId, _datos.CategoriaId, "Gracias por quedarte a terminar el informe."));

        Assert.True(respuesta.IsSuccessStatusCode, await respuesta.Content.ReadAsStringAsync());
        var publicado = await respuesta.Content.ReadFromJsonAsync<KudoPublicadoDto>(Json);
        Assert.NotNull(publicado);

        var muro = await carlos.GetFromJsonAsync<PaginaMuroDto>("/api/kudos", Json);
        var enElMuro = Assert.Single(muro!.Elementos, k => k.Id == publicado.Id);
        Assert.Equal("Ana Gómez", enElMuro.AutorNombre);
        Assert.Equal("Carlos Mejía", enElMuro.DestinatarioNombre);

        // El correo, el WhatsApp y el aviso no salen en la petición: salen después, desde la
        // bandeja. Hasta que el despachador pasa, no ha salido nada.
        var correo = _web.Services.GetRequiredService<EnviadorCorreoSimulado>();
        Assert.Empty(correo.Enviados);

        await DespacharAsync();

        // Paso 2: correo al destinatario.
        var enviado = Assert.Single(correo.Enviados);
        Assert.Equal(CorreoDestinatario, enviado.Destino);
        Assert.Contains("Ana Gómez", enviado.Texto, StringComparison.Ordinal);

        // Paso 3: WhatsApp, siempre por plantilla aprobada.
        var whatsApp = Assert.Single(
            _web.Services.GetRequiredService<WhatsAppSimuladoAdapter>().Enviados);
        Assert.Equal("kudo_recibido", whatsApp.Plantilla);

        // Paso 4: el aviso queda en la bandeja del destinatario, que es lo que lo hace visible aunque
        // el canal en vivo no estuviera abierto.
        var avisos = await carlos.GetFromJsonAsync<PaginaAvisosDto>("/api/avisos", Json);
        Assert.Equal(1, avisos!.SinLeer);
        Assert.Contains("Kudo", Assert.Single(avisos.Elementos).Titulo, StringComparison.Ordinal);

        // Paso 5: todo quedó auditado, y la cadena de hashes cuadra.
        var verificacion = await rector.GetFromJsonAsync<VerificacionDto>("/api/auditoria/verificacion", Json);
        Assert.True(verificacion!.Intacta, verificacion.Detalle);
        Assert.True(verificacion.EventosRevisados >= 1);
    }

    [Fact]
    public async Task Una_peticion_sin_identidad_recibe_401()
    {
        using var anonimo = Como();

        var respuesta = await anonimo.GetAsync("/api/kudos");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    /// <summary>
    /// **Hallazgo de esta misma prueba, y el motivo de que exista el filtro de cuenta activa.**
    /// El muro, los Kudos recibidos, el detalle, las réplicas y las categorías solo exigían un token
    /// válido: quien se autenticaba en el proveedor leía todo sin tener cuenta aquí. Autenticarse no
    /// es tener cuenta, y el propio código de la API afirma que una cuenta desactivada pierde el
    /// acceso de inmediato.
    /// </summary>
    [Theory]
    [InlineData("/api/kudos")]
    [InlineData("/api/categorias")]
    public async Task Una_identidad_sin_cuenta_en_la_plataforma_no_puede_leer(string ruta)
    {
        using var desconocido = Como("nadie@uco.edu.co");

        var respuesta = await desconocido.GetAsync(ruta);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Una_cuenta_que_no_esta_activa_no_puede_leer_el_muro()
    {
        // Registrada pero sin aprobar ni verificar: RF-02 dice que no puede usar la plataforma.
        await using (var contexto = baseDatos.CrearContexto())
        {
            contexto.Personas.Add(new Persona("pendiente@uco.edu.co", "+573008887766", "Paula Pendiente", DateTimeOffset.UtcNow));
            await contexto.SaveChangesAsync();
        }

        using var pendiente = Como("pendiente@uco.edu.co");

        var respuesta = await pendiente.GetAsync("/api/kudos");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    /// <summary>
    /// **Regresión de un defecto real.** La API respondía 400 con el motivo exacto en el cuerpo y el
    /// cliente lo descartaba, así que el usuario veía «hay algo mal en los datos» sin ningún campo
    /// mal. Aquí se comprueba el lado del servidor: que el motivo **existe y llega** en el cuerpo.
    /// </summary>
    [Fact]
    public async Task Reconocerse_a_uno_mismo_es_un_400_que_explica_el_motivo()
    {
        using var ana = Como(CorreoAutor);

        var respuesta = await ana.PostAsJsonAsync(
            "/api/kudos",
            new PublicarKudoDto(_datos.AutorId, _datos.CategoriaId, "Me felicito."));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);

        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        var detalle = cuerpo.RootElement.GetProperty("detail").GetString();
        Assert.Contains("uno mismo", detalle, StringComparison.OrdinalIgnoreCase);

    }

    /// <summary>
    /// **Regresión de otro defecto real.** La verificación existía y estaba probada, pero nadie con
    /// permiso podía ejecutarla. Aquí se comprueba la autorización de punta a punta: el mismo
    /// endpoint, dos roles, dos resultados.
    /// </summary>
    [Fact]
    public async Task La_auditoria_solo_la_consulta_un_rol_de_universidad()
    {
        using var docente = Como(CorreoAutor);
        using var rector = Como(CorreoRector);

        var rechazada = await docente.GetAsync("/api/auditoria/verificacion");
        Assert.Equal(HttpStatusCode.Forbidden, rechazada.StatusCode);

        // El motivo llega íntegro, con el nombre del rol: es lo que la pantalla enseña.
        using var cuerpo = JsonDocument.Parse(await rechazada.Content.ReadAsStringAsync());
        Assert.Contains("Docente", cuerpo.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);

        var aceptada = await rector.GetAsync("/api/auditoria/verificacion");
        Assert.Equal(HttpStatusCode.OK, aceptada.StatusCode);
    }

    [Fact]
    public async Task El_buscador_encuentra_sin_tildes_y_no_expone_datos_de_contacto()
    {
        using var ana = Como(CorreoAutor);

        var respuesta = await ana.GetAsync("/api/identidad/personas?busqueda=mejia");
        var texto = await respuesta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        var encontradas = JsonSerializer.Deserialize<List<PersonaBuscadaDto>>(texto, Json);
        Assert.Equal("Carlos Mejía", Assert.Single(encontradas!).NombreCompleto);

        // Lo que importa del buscador no es lo que devuelve sino lo que NO devuelve: una caja de
        // búsqueda abierta a cualquier usuario no puede ser un volcado del directorio.
        Assert.DoesNotContain(CorreoDestinatario, texto, StringComparison.Ordinal);
        Assert.DoesNotContain("+57", texto, StringComparison.Ordinal);
    }
}
