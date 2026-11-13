using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UcoKudos.Application.Auditoria.CasosUso;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Application.Identidad.CasosUso;
using UcoKudos.Application.Identidad.Entrada.Adaptadores;
using UcoKudos.Application.Identidad.Entrada.Puertos;
using UcoKudos.Application.Identidad.Salida.Puertos;
using UcoKudos.Application.Interaccion.CasosUso;
using UcoKudos.Application.Interaccion.Entrada.Adaptadores;
using UcoKudos.Application.Interaccion.Entrada.Puertos;
using UcoKudos.Application.Kudos.CasosUso;
using UcoKudos.Application.Kudos.Entrada.Adaptadores;
using UcoKudos.Application.Kudos.Entrada.Puertos;
using UcoKudos.Application.Moderacion.CasosUso;
using UcoKudos.Application.Notificaciones.CasosUso;
using UcoKudos.Application.Notificaciones.Entrada.Puertos;
using UcoKudos.Application.Notificaciones.Salida.Puertos;
using UcoKudos.Domain.Academico.Repositorios;
using UcoKudos.Domain.Auditoria.Repositorios;
using UcoKudos.Domain.Identidad.Repositorios;
using UcoKudos.Domain.Interaccion.Repositorios;
using UcoKudos.Domain.Kudos.Repositorios;
using UcoKudos.Infrastructure.Canal;
using UcoKudos.Infrastructure.Lectura;
using UcoKudos.Infrastructure.Notificaciones;
using UcoKudos.Infrastructure.Objetos;
using UcoKudos.Infrastructure.Persistencia;
using UcoKudos.Infrastructure.Persistencia.Repositorios;

namespace UcoKudos.Infrastructure;

/// <summary>
/// Registro de los adaptadores de salida y de los casos de uso (ADR-0001).
///
/// Esta clase es el único punto por el que la capa de interfaces toca la infraestructura: la API la
/// llama desde su raíz de composición y, fuera de ahí, no conoce ningún tipo de esta capa. La prueba
/// de arquitectura <c>La_api_no_usa_la_infraestructura_fuera_de_la_raiz_de_composicion</c> lo vigila,
/// y es la única de las cinco reglas que el compilador no puede garantizar.
///
/// Los adaptadores se eligen por configuración: cada canal arranca en modo simulado y pasa al
/// proveedor real cuando se le dan sus credenciales. Así la plataforma funciona desde el primer
/// arranque, sin configurar nada, y un canal sin credenciales degrada el servicio en lugar de
/// impedir que arranque.
/// </summary>
public static class ServiciosInfraestructura
{
    public static IServiceCollection AgregarInfraestructura(
        this IServiceCollection servicios, IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(configuracion);

        servicios.TryAddSingletonReloj();

        AgregarPersistencia(servicios, configuracion);
        AgregarLectura(servicios);
        AgregarCanales(servicios, configuracion);
        AgregarCasosUso(servicios);
        AgregarProcesoDeNotificaciones(servicios, configuracion);

        return servicios;
    }

    private static void AgregarPersistencia(IServiceCollection servicios, IConfiguration configuracion)
    {
        var cadena = configuracion.GetConnectionString("Postgres");

        servicios.AddDbContext<UcoKudosDbContext>(opciones =>
        {
            if (!string.IsNullOrWhiteSpace(cadena))
                opciones.UseNpgsql(cadena, npgsql => npgsql.MigrationsHistoryTable("migracion", UcoKudosDbContext.EsquemaNucleo));
        });

        // La unidad de trabajo y la bandeja son la MISMA instancia: es lo que hace que el cambio, su
        // auditoría y su evento viajen en la misma transacción (ADR-0005).
        servicios.AddScoped<UnidadTrabajoEf>();
        servicios.AddScoped<IUnidadTrabajo>(sp => sp.GetRequiredService<UnidadTrabajoEf>());
        servicios.AddScoped<IBandejaSalida>(sp => sp.GetRequiredService<UnidadTrabajoEf>());

        servicios.AddScoped<IPersonaRepositorio, PersonaRepositorio>();
        servicios.AddScoped<IEstructuraAcademicaRepositorio, EstructuraAcademicaRepositorio>();
        servicios.AddScoped<IKudoRepositorio, KudoRepositorio>();
        servicios.AddScoped<ICategoriaRepositorio, CategoriaRepositorio>();
        servicios.AddScoped<IReplicaRepositorio, ReplicaRepositorio>();
        servicios.AddScoped<ICalificacionRepositorio, CalificacionRepositorio>();
        servicios.AddScoped<IModeracionRepositorio, ModeracionRepositorio>();
        servicios.AddScoped<IEventoAuditoriaRepositorio, EventoAuditoriaRepositorio>();
        servicios.AddScoped<IRepositorioAvisos, RepositorioAvisosEf>();
        servicios.AddScoped<IRegistroNotificaciones, RegistroNotificacionesEf>();
    }

    private static void AgregarLectura(IServiceCollection servicios)
    {
        servicios.AddScoped<IConsultasMuro, ConsultasMuroEf>();
        servicios.AddScoped<IConsultasAvisos, ConsultasAvisosEf>();
        servicios.AddScoped<IConsultasPersonas, ConsultasPersonasEf>();

        // La caché es singleton: compartirla entre peticiones es justamente su razón de existir.
        servicios.AddSingleton<CacheMemoria>();
        servicios.AddScoped<ISuscripcionesPush, SuscripcionesPushEf>();
        servicios.AddSingleton<ICacheConsultas>(sp => sp.GetRequiredService<CacheMemoria>());
    }

    private static void AgregarCanales(IServiceCollection servicios, IConfiguration configuracion)
    {
        // --- WhatsApp (ADR-0007) ---
        var opcionesWhatsApp = Leer<OpcionesWhatsApp>(configuracion, OpcionesWhatsApp.Seccion);
        servicios.AddSingleton(opcionesWhatsApp);

        if (opcionesWhatsApp.EsSimulado)
        {
            servicios.AddSingleton<WhatsAppSimuladoAdapter>();
            servicios.AddSingleton<IWhatsAppGateway>(sp => sp.GetRequiredService<WhatsAppSimuladoAdapter>());
        }
        else
        {
            servicios.AddHttpClient<IWhatsAppGateway, WhatsAppMetaAdapter>()
                .ConfigureHttpClient(cliente => cliente.Timeout = TimeSpan.FromSeconds(15));
        }

        // --- Correo (ADR-0008) ---
        var opcionesCorreo = Leer<OpcionesCorreo>(configuracion, OpcionesCorreo.Seccion);
        servicios.AddSingleton(opcionesCorreo);

        if (opcionesCorreo.EsSimulado)
        {
            servicios.AddSingleton<EnviadorCorreoSimulado>();
            servicios.AddSingleton<IEnviadorCorreo>(sp => sp.GetRequiredService<EnviadorCorreoSimulado>());
        }
        else
        {
            servicios.AddSingleton<IEnviadorCorreo, EnviadorCorreoSmtp>();
        }

        // --- Tiempo real (ADR-0009) ---
        // El canal de SSE es singleton porque mantiene las conexiones abiertas en memoria.
        servicios.AddSingleton<AvisosEnVivoSse>();
        servicios.AddSingleton<IAvisosEnVivo>(sp => sp.GetRequiredService<AvisosEnVivoSse>());

        // Web Push cae al simulado mientras no haya claves VAPID. No es un fallo: sin este canal el
        // aviso queda persistido y el usuario lo ve al entrar (ADR-0009).
        var opcionesPush = Leer<OpcionesPush>(configuracion, OpcionesPush.Seccion);
        servicios.AddSingleton(opcionesPush);

        if (opcionesPush.EstaConfigurado)
        {
            servicios.AddHttpClient(nameof(EnviadorPushWebPush))
                .ConfigureHttpClient(cliente => cliente.Timeout = TimeSpan.FromSeconds(15));

            servicios.AddSingleton(sp => FabricaPushServiceClient.Crear(
                opcionesPush,
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(EnviadorPushWebPush))));

            servicios.AddSingleton<IEnviadorPush, EnviadorPushWebPush>();
        }
        else
        {
            servicios.AddSingleton<EnviadorPushSimulado>();
            servicios.AddSingleton<IEnviadorPush>(sp => sp.GetRequiredService<EnviadorPushSimulado>());
        }

        // --- Adjuntos (ADR-0012) ---
        var opcionesObjetos = Leer<OpcionesObjetos>(configuracion, OpcionesObjetos.Seccion);
        servicios.AddSingleton(opcionesObjetos);

        if (opcionesObjetos.EsSimulado)
        {
            servicios.AddSingleton<AlmacenObjetosSimulado>();
            servicios.AddSingleton<IAlmacenObjetos>(sp => sp.GetRequiredService<AlmacenObjetosSimulado>());
        }
        else
        {
            // El cliente va con estilo de ruta y endpoint propio: es lo que permite que el mismo
            // adaptador hable con el almacenamiento autoalojado de hoy y con un servicio gestionado
            // el día que haya presupuesto, cambiando solo configuración (ADR-0012).
            servicios.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
                new Amazon.Runtime.BasicAWSCredentials(opcionesObjetos.Usuario, opcionesObjetos.Password),
                new AmazonS3Config
                {
                    ServiceURL = opcionesObjetos.Endpoint,
                    ForcePathStyle = true,
                    AuthenticationRegion = "us-east-1",

                    // El esquema sale del endpoint configurado, no de un valor por omisión. El SDK
                    // firma las URL con https salvo que se le diga lo contrario, así que contra un
                    // almacenamiento local en http las URL prefirmadas se generaban con https y el
                    // cliente fallaba en el saludo TLS. En el servidor el endpoint será https y esto
                    // quedará en false solo, que es lo que debe pasar.
                    UseHttp = opcionesObjetos.Endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                }));

            servicios.AddSingleton<AlmacenObjetosCompatibleS3>();
            servicios.AddSingleton<IAlmacenObjetos>(sp => sp.GetRequiredService<AlmacenObjetosCompatibleS3>());
        }
    }

    private static void AgregarCasosUso(IServiceCollection servicios)
    {
        servicios.AddScoped<ConsultarAvisosCasoUso>();
        servicios.AddScoped<ServicioAuditoria>();

        servicios.AddScoped<PublicarKudoCasoUso>();
        servicios.AddScoped<ConsultarMuroCasoUso>();
        servicios.AddScoped<AdjuntosCasosUso>();
        servicios.AddScoped<CategoriasCasosUso>();
        servicios.AddScoped<RegistrarCuentaCasoUso>();
        servicios.AddScoped<VerificarContactoCasoUso>();
        servicios.AddScoped<ReenviarCodigosCasoUso>();
        servicios.AddScoped<ResolverSolicitudCasoUso>();
        servicios.AddScoped<ConsultarIdentidadCasoUso>();
        servicios.AddScoped<BuscarPersonasCasoUso>();
        servicios.AddScoped<ResponderCasoUso>();
        servicios.AddScoped<CalificarCasoUso>();
        servicios.AddScoped<ReportarContenidoCasoUso>();
        servicios.AddScoped<VetarContenidoCasoUso>();
        servicios.AddScoped<GestionarReportesCasoUso>();
        servicios.AddScoped<GatewayNotificaciones>();

        // Puertos de entrada: es lo único que la API puede pedir (ADR-0001, regla 4).
        servicios.AddScoped<IKudosPuertoEntrada, KudosAdaptadorEntrada>();
        servicios.AddScoped<IAdjuntosPuertoEntrada, AdjuntosAdaptadorEntrada>();
        servicios.AddScoped<ICategoriasPuertoEntrada, CategoriasAdaptadorEntrada>();
        servicios.AddScoped<IIdentidadPuertoEntrada, IdentidadAdaptadorEntrada>();
        servicios.AddScoped<IInteraccionPuertoEntrada, InteraccionAdaptadorEntrada>();
        servicios.AddScoped<IModeracionPuertoEntrada, ModeracionAdaptadorEntrada>();
        servicios.AddScoped<IAuditoriaPuertoEntrada, VerificarAuditoriaCasoUso>();
        servicios.AddScoped<INotificacionesPuertoEntrada, ProcesarEventoCasoUso>();

        // Puertos de plataforma: existen porque la prueba de arquitectura detectó que los endpoints
        // de avisos y de salud usaban tipos de infraestructura directamente (ADR-0001, regla 4).
        servicios.AddScoped<Application.Comun.Entrada.Puertos.IAvisosPuertoEntrada, Plataforma.AvisosAdaptadorEntrada>();
        servicios.AddScoped<Application.Comun.Entrada.Puertos.ISaludPuertoEntrada, Plataforma.SaludAdaptadorEntrada>();
    }

    private static void AgregarProcesoDeNotificaciones(IServiceCollection servicios, IConfiguration configuracion)
    {
        var opciones = Leer<OpcionesProcesoNotificaciones>(configuracion, OpcionesProcesoNotificaciones.Seccion);
        servicios.AddSingleton(opciones);
        servicios.AddHostedService<ProcesoNotificaciones>();
    }

    /// <summary>
    /// Lee una sección de configuración. Los valores por omisión están en las propias clases de
    /// opciones, salvo los secretos: esos no tienen valor por omisión, así que si faltan el canal se
    /// queda en modo simulado en lugar de fallar en silencio (ADR-0018).
    /// </summary>
    private static T Leer<T>(IConfiguration configuracion, string seccion) where T : new()
    {
        var opciones = new T();
        configuracion.GetSection(seccion).Bind(opciones);
        return opciones;
    }

    private static void TryAddSingletonReloj(this IServiceCollection servicios)
    {
        // El reloj se inyecta para que las pruebas puedan controlarlo: es la contraparte de que el
        // dominio reciba el tiempo como parámetro (ADR-0001).
        if (servicios.All(s => s.ServiceType != typeof(TimeProvider)))
            servicios.AddSingleton(TimeProvider.System);
    }
}
