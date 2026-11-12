using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using UcoKudos.Infrastructure.Canal;
using UcoKudos.Infrastructure.Notificaciones;
using UcoKudos.Infrastructure.Objetos;

namespace UcoKudos.IntegrationTests.E2E;

/// <summary>
/// La API completa, levantada en memoria y atendiendo HTTP de verdad, contra PostgreSQL real.
///
/// Es lo que distingue una prueba de extremo a extremo de una de integración: la de integración
/// invoca casos de uso con un contexto ya construido; esta **llama a los endpoints** y atraviesa
/// todo el camino que recorre una petición real —enrutamiento, autenticación, autorización,
/// serialización, manejo de errores— antes de llegar al dominio.
///
/// Los tres canales siguen siendo los simulados, y es deliberado: lo que se comprueba es que el
/// sistema *entrega* el mensaje a cada canal, no que Meta o un servidor SMTP respondan. Eso ya está
/// verificado contra los proveedores reales y documentado en <c>docs/evidencias</c>.
/// </summary>
internal sealed class PlataformaWebFactory(string cadenaConexion) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("ConnectionStrings:Postgres", cadenaConexion);

        // Con proveedor de identidad configurado, los grupos de rutas exigen autenticación. Sin
        // esto la API arranca en el modo permisivo de desarrollo y una petición anónima no daría 401,
        // con lo que la prueba de que se exige token no probaría nada.
        builder.UseSetting("UcoKudos:Identidad:Emisor", "https://emisor.de.prueba");
        builder.UseSetting("UcoKudos:Identidad:Audiencia", "https://api.de.prueba");

        builder.UseSetting(OpcionesWhatsApp.Seccion + ":Proveedor", OpcionesWhatsApp.ProveedorSimulado);
        builder.UseSetting(OpcionesCorreo.Seccion + ":Proveedor", OpcionesCorreo.ProveedorSimulado);
        builder.UseSetting(OpcionesObjetos.Seccion + ":Proveedor", OpcionesObjetos.ProveedorSimulado);

        // El despachador no corre por su cuenta: la prueba invoca sus vueltas. Esperar a un
        // temporizador volvería la suite lenta y, peor, dependiente del reloj.
        builder.UseSetting(OpcionesProcesoNotificaciones.Seccion + ":Habilitado", "false");

        builder.ConfigureTestServices(servicios =>
            servicios.AddAuthentication(opciones =>
                {
                    opciones.DefaultAuthenticateScheme = EsquemaDePrueba.Nombre;
                    opciones.DefaultChallengeScheme = EsquemaDePrueba.Nombre;
                })
                .AddScheme<AuthenticationSchemeOptions, EsquemaDePrueba>(EsquemaDePrueba.Nombre, _ => { }));
    }
}
