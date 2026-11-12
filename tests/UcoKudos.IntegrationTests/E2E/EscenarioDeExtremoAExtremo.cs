using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UcoKudos.Domain.Identidad;
using UcoKudos.Infrastructure.Notificaciones;
using UcoKudos.IntegrationTests.Infraestructura;

namespace UcoKudos.IntegrationTests.E2E;

/// <summary>
/// La infraestructura común de los escenarios de punta a punta: la API levantada, los datos
/// sembrados y una persona por cada uno de los siete roles del enunciado.
///
/// Está en una clase base para que cada archivo de escenarios cuente **lo que prueba** y no cómo se
/// monta. Antes vivía dentro de la primera clase de pruebas, y añadir la segunda habría significado
/// copiarla entera.
/// </summary>
public abstract class EscenarioDeExtremoAExtremo(BaseDeDatosFixture baseDatos) : IAsyncLifetime
{
    // Las dos personas que siembran las pruebas de integración: un docente y un estudiante.
    protected const string CorreoDocente = "docente@uco.edu.co";
    protected const string CorreoEstudiante = "estudiante@uco.edu.co";

    // Una persona por cada uno de los otros cinco roles, creadas aquí.
    protected const string CorreoAdministrador = "administrador@uco.edu.co";
    protected const string CorreoRector = "rector@uco.edu.co";
    protected const string CorreoCoordinadorAcademico = "coordinador.academico@uco.edu.co";
    protected const string CorreoCoordinadorFacultad = "coordinador.facultad@uco.edu.co";
    protected const string CorreoCoordinadorPrograma = "coordinador.programa@uco.edu.co";

    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected BaseDeDatosFixture BaseDatos { get; } = baseDatos;

    protected PlataformaWebFactory Web { get; private set; } = null!;

    protected DatosSembrados Datos { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await BaseDatos.LimpiarAsync();

        var ahora = DateTimeOffset.UtcNow;
        await using var contexto = BaseDatos.CrearContexto();
        Datos = await Semillas.SembrarAsync(contexto, ahora);

        var facultadId = await contexto.Facultades.Select(f => f.Id).FirstAsync();
        var programaId = await contexto.Programas.Select(p => p.Id).FirstAsync();

        // El ámbito de cada rol sale del propio dominio: los de universidad no llevan facultad ni
        // programa, el de facultad lleva la suya y el de programa lleva las dos. Si estos datos
        // fueran incoherentes, el dominio los rechazaría al construir la asignación.
        Sembrar(contexto, CorreoAdministrador, "Admin del Sistema", "+573009990001", Rol.Administrador, null, null, ahora);
        Sembrar(contexto, CorreoRector, "Marta Restrepo", "+573009990002", Rol.Rector, null, null, ahora);
        Sembrar(contexto, CorreoCoordinadorAcademico, "Carlos Álvarez", "+573009990003", Rol.CoordinadorAcademico, null, null, ahora);
        Sembrar(contexto, CorreoCoordinadorFacultad, "Decana de Ingeniería", "+573009990004", Rol.CoordinadorFacultad, facultadId, null, ahora);
        Sembrar(contexto, CorreoCoordinadorPrograma, "Coordinador de Sistemas", "+573009990005", Rol.CoordinadorPrograma, facultadId, programaId, ahora);

        await contexto.SaveChangesAsync();

        Web = new PlataformaWebFactory(BaseDatos.CadenaConexion);
    }

    public async Task DisposeAsync() => await Web.DisposeAsync();

    private static void Sembrar(
        UcoKudos.Infrastructure.Persistencia.UcoKudosDbContext contexto,
        string correo, string nombre, string celular, Rol rol,
        Guid? facultadId, Guid? programaId, DateTimeOffset ahora)
    {
        var persona = Semillas.Activa(new Persona(correo, celular, nombre, ahora));
        contexto.Personas.Add(persona);
        contexto.AsignacionesRol.Add(new AsignacionRol(persona.Id, rol, facultadId, programaId, ahora));
    }

    /// <summary>Un cliente que hace las peticiones como esa persona. Sin argumento, anónimo.</summary>
    protected HttpClient Como(string? correo = null)
    {
        var cliente = Web.CreateClient();

        if (correo is not null)
        {
            cliente.DefaultRequestHeaders.Add(EsquemaDePrueba.Cabecera, correo);
        }

        return cliente;
    }

    /// <summary>Vacía la bandeja, como lo haría el despachador en segundo plano.</summary>
    protected async Task DespacharAsync()
    {
        var proceso = Web.Services.GetServices<IHostedService>().OfType<ProcesoNotificaciones>().Single();

        // Una vuelta atiende un evento: se repite hasta que no quede ninguno, que es lo que haría
        // el temporizador real en unos segundos.
        while (await proceso.ProcesarVueltaAsync(CancellationToken.None) > 0)
        {
        }
    }
}
