using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UcoKudos.Application.Comun;
using UcoKudos.Domain.Academico;
using UcoKudos.Domain.Identidad;
using UcoKudos.Infrastructure;
using UcoKudos.Infrastructure.Canal;
using UcoKudos.Infrastructure.Notificaciones;
using UcoKudos.Infrastructure.Objetos;
using UcoKudos.Infrastructure.Persistencia;

namespace UcoKudos.IntegrationTests.Infraestructura;

/// <summary>
/// Levanta la plataforma completa contra la base real, con los **adaptadores simulados** de los tres
/// canales.
///
/// Es lo que permite probar el flujo obligatorio de punta a punta sin tocar la red ni gastar cuota
/// de ningún proveedor, que es justo lo que ADR-0007 y ADR-0008 buscaban al exigir que cada canal
/// tuviera su versión simulada detrás del mismo puerto.
///
/// La diferencia con <see cref="BaseDeDatosFixture"/>: esa da una base y un contexto, y esta arma el
/// contenedor de dependencias entero, así que las pruebas ejercitan los casos de uso reales con sus
/// repositorios, su unidad de trabajo y su gateway, no piezas aisladas.
/// </summary>
public sealed class PlataformaFixture(BaseDeDatosFixture baseDatos) : IDisposable
{
    private readonly ServiceProvider _servicios = Construir(baseDatos.CadenaConexion);

    /// <summary>Instante fijo: el dominio recibe el tiempo como parámetro, así que las pruebas lo controlan.</summary>
    public static readonly DateTimeOffset Ahora = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    public IServiceScope CrearAlcance() => _servicios.CreateScope();

    /// <summary>Lo que se habría enviado por correo. Vive en el adaptador simulado, que es singleton.</summary>
    public EnviadorCorreoSimulado Correo => _servicios.GetRequiredService<EnviadorCorreoSimulado>();

    public WhatsAppSimuladoAdapter WhatsApp => _servicios.GetRequiredService<WhatsAppSimuladoAdapter>();

    public EnviadorPushSimulado Push => _servicios.GetRequiredService<EnviadorPushSimulado>();

    public AlmacenObjetosSimulado Objetos => _servicios.GetRequiredService<AlmacenObjetosSimulado>();

    /// <summary>El proceso de la bandeja, para invocar una vuelta sin esperar su temporizador.</summary>
    public ProcesoNotificaciones Proceso =>
        _servicios.GetServices<IHostedService>().OfType<ProcesoNotificaciones>().Single();

    public void Dispose() => _servicios.Dispose();

    private static ServiceProvider Construir(string cadenaConexion)
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = cadenaConexion,

                // Los tres canales en modo simulado: es el valor por omisión, pero se deja explícito
                // porque es la decisión que hace posible esta prueba.
                [OpcionesWhatsApp.Seccion + ":Proveedor"] = OpcionesWhatsApp.ProveedorSimulado,
                [OpcionesCorreo.Seccion + ":Proveedor"] = OpcionesCorreo.ProveedorSimulado,
                [OpcionesObjetos.Seccion + ":Proveedor"] = OpcionesObjetos.ProveedorSimulado,

                // El proceso de la bandeja no arranca por su cuenta: las pruebas invocan sus vueltas,
                // porque esperar un temporizador haría la suite lenta y frágil.
                [OpcionesProcesoNotificaciones.Seccion + ":Habilitado"] = "false"
            })
            .Build();

        var servicios = new ServiceCollection();
        servicios.AddLogging();

        // Reloj fijo: el mismo que el dominio recibe como parámetro.
        servicios.AddSingleton<TimeProvider>(new RelojFijo(Ahora));

        servicios.AgregarInfraestructura(configuracion);

        return servicios.BuildServiceProvider();
    }
}

/// <summary>Reloj que siempre devuelve el mismo instante. Hace deterministas las pruebas del flujo.</summary>
internal sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => ahora;
}

/// <summary>
/// Datos mínimos para que el flujo de publicación tenga sentido: una facultad, un programa, un autor
/// y un destinatario, los dos con cuenta activa.
/// </summary>
public sealed record DatosSembrados(Guid AutorId, Guid DestinatarioId, Guid CategoriaId, string CorreoDestinatario, string CelularDestinatario);

/// <summary>Siembra los datos de las pruebas del flujo.</summary>
public static class Semillas
{
    /// <summary>
    /// Identificador de la categoría «Rendimiento Académico», sembrada por la migración con un
    /// identificador fijo. Es precisamente para esto que la migración no los genera al azar.
    /// </summary>
    public static readonly Guid CategoriaRendimiento = Guid.Parse("0193a000-0000-7000-8000-000000000001");

    public static async Task<DatosSembrados> SembrarAsync(UcoKudosDbContext contexto, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var facultad = new Facultad("Ingenierías", "ING", ahora);
        contexto.Facultades.Add(facultad);

        var programa = new Programa(facultad.Id, "Ingeniería de Sistemas", "ISIS", ahora);
        contexto.Programas.Add(programa);

        var autor = Activa(new Persona("docente@uco.edu.co", "+573001112233", "Ana Gómez", ahora));
        var destinatario = Activa(new Persona("estudiante@uco.edu.co", "+573004445566", "Carlos Mejía", ahora));

        contexto.Personas.Add(autor);
        contexto.Personas.Add(destinatario);

        contexto.AsignacionesRol.Add(new AsignacionRol(autor.Id, Rol.Docente, facultad.Id, programa.Id, ahora));
        contexto.AsignacionesRol.Add(new AsignacionRol(destinatario.Id, Rol.Estudiante, facultad.Id, programa.Id, ahora));

        await contexto.SaveChangesAsync();

        return new DatosSembrados(
            autor.Id, destinatario.Id, CategoriaRendimiento,
            destinatario.CorreoInstitucional, destinatario.CelularE164);
    }

    /// <summary>Las tres condiciones de RF-02, que es lo único que deja usar la plataforma.</summary>
    public static Persona Activa(Persona persona)
    {
        persona.AprobarSolicitud();
        persona.VerificarCorreo();
        persona.VerificarCelular();
        return persona;
    }

    /// <summary>El contexto de petición de un usuario ya resuelto, como lo haría la API.</summary>
    public static ContextoPeticion ContextoDe(Guid personaId, Rol rol, Guid facultadId, Guid programaId, string correlacion) =>
        new(new AmbitoUsuario(personaId, rol, facultadId, programaId), correlacion);
}
