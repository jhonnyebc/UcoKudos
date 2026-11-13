using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using UcoKudos.Api.Endpoints;
using UcoKudos.Api.Plataforma;
using UcoKudos.Infrastructure;
using UcoKudos.Infrastructure.Observabilidad;
using UcoKudos.Infrastructure.Persistencia;

var constructor = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------------------------
// Raíz de composición: el único lugar de la API que conoce la capa de infraestructura (ADR-0001).
// Fuera de aquí, los endpoints solo ven puertos de entrada y DTO, y la prueba de arquitectura
// La_api_no_usa_la_infraestructura_fuera_de_la_raiz_de_composicion lo vigila.
// ---------------------------------------------------------------------------------------------
constructor.Services.AgregarInfraestructura(constructor.Configuration);

// Las enumeraciones viajan como texto, no como número: el contrato se lee sin tabla de conversión
// y un valor nuevo no desplaza los existentes.
constructor.Services.ConfigureHttpJsonOptions(opciones =>
    opciones.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ---------------------------------------------------------------------------------------------
// Observabilidad: exporta trazas, métricas y registros por OTLP (ADR-0014).
//
// Los nombres de las fuentes se pasan desde aquí, que es la raíz de composición y el único sitio
// de la API al que se le permite conocer la capa de infraestructura.
// ---------------------------------------------------------------------------------------------
constructor.AgregarObservabilidad(
    Instrumentacion.NombreFuente,
    Instrumentacion.NombreMetricas);

// ---------------------------------------------------------------------------------------------
// Errores: el manejador traduce los errores de dominio a códigos de estado y añade el
// identificador de traza a toda respuesta de error (ADR-0014).
// ---------------------------------------------------------------------------------------------
constructor.Services.AddProblemDetails();
constructor.Services.AddExceptionHandler<ManejadorExcepciones>();

// ---------------------------------------------------------------------------------------------
// Autenticación delegada por OIDC (ADR-0004). La API es un validador de token: no custodia
// contraseñas. El rol NO viaja en el token; se resuelve en cada petición contra la base.
// ---------------------------------------------------------------------------------------------
var emisor = constructor.Configuration["UcoKudos:Identidad:Emisor"];
var audiencia = constructor.Configuration["UcoKudos:Identidad:Audiencia"];
var identidadConfigurada = !string.IsNullOrWhiteSpace(emisor) && !string.IsNullOrWhiteSpace(audiencia);

if (identidadConfigurada)
{
    constructor.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opciones =>
        {
            opciones.Authority = emisor;
            opciones.Audience = audiencia;
            opciones.TokenValidationParameters.ValidateIssuer = true;
            opciones.TokenValidationParameters.ValidateAudience = true;
            opciones.TokenValidationParameters.ValidateLifetime = true;
        });
}
else
{
    // Sin proveedor configurado la API arranca igual, con las rutas anónimas disponibles. Es
    // deliberado: permite levantar el entorno y ver el muro antes de configurar la identidad.
    constructor.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
}

constructor.Services.AddAuthorization();

// Contrato explícito del que se generan los tipos del cliente: una ruptura de contrato falla al
// compilar en el front en lugar de en producción (ADR-0015).
constructor.Services.AddOpenApi();

constructor.Services.AddHealthChecks();

var aplicacion = constructor.Build();

// ---------------------------------------------------------------------------------------------
// Modo migración: aplica el esquema y termina, sin atender tráfico (ADR-0003, ADR-0016).
//
// Existe para que el despliegue pueda migrar **con la misma imagen que va a desplegar**, de modo
// que el esquema lo aplica exactamente el código que después lo va a usar. La alternativa
// habitual —migrar al arrancar la aplicación— tiene dos problemas: con más de una instancia
// varias intentarían migrar a la vez, y en una migración lenta el servicio queda sin responder
// mientras tanto.
//
// Si falla, termina con código distinto de cero, que es lo que permite al guion de despliegue
// pararse antes de dirigir tráfico a una versión cuyo esquema no se aplicó.
// ---------------------------------------------------------------------------------------------
if (args.Contains("--migrar", StringComparer.Ordinal))
{
    await using var alcanceMigracion = aplicacion.Services.CreateAsyncScope();
    await alcanceMigracion.ServiceProvider.MigrarBaseDeDatosAsync(CancellationToken.None);
    return;
}

aplicacion.UseExceptionHandler();
aplicacion.UseAuthentication();
aplicacion.UseAuthorization();

if (aplicacion.Environment.IsDevelopment())
    aplicacion.MapOpenApi();

// ---------------------------------------------------------------------------------------------
// Rutas. El token se exige por GRUPO y no con una política global de reserva: una política global
// convierte las rutas inexistentes en 401 y esconde los errores de enrutamiento detrás de un
// problema de autenticación (ADR-0004).
// ---------------------------------------------------------------------------------------------
aplicacion.MapearSalud();

var api = aplicacion.MapGroup(string.Empty);

if (identidadConfigurada)
    api = aplicacion.MapGroup(string.Empty).RequireAuthorization();

api.MapearKudos();
api.MapearIdentidad();
api.MapearAcademico();
api.MapearIndicadores();
api.MapearCategorias();
api.MapearInteraccion();
api.MapearAuditoria();
api.MapearAvisos();

aplicacion.Run();

/// <summary>
/// Hace visible el punto de entrada a las pruebas de extremo a extremo.
///
/// Con sentencias de nivel superior el compilador genera <c>Program</c> como clase interna, y
/// <c>WebApplicationFactory&lt;Program&gt;</c> necesita poder verla para levantar la API completa.
/// Declararla parcial y pública no añade ningún miembro ni ninguna dependencia: la regla de
/// arquitectura que vigila a la API no la ve, porque no contiene código.
/// </summary>
public partial class Program;
