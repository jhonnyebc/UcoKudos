using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UcoKudos.IntegrationTests.E2E;

/// <summary>
/// Sustituye al proveedor de identidad en las pruebas de extremo a extremo.
///
/// **Qué se cambia y qué no, que es lo que decide si la prueba vale algo.** Se sustituye *solo* la
/// validación del token: en lugar de comprobar una firma contra Auth0, esta identidad se lee de una
/// cabecera. Todo lo demás es el sistema real: el enrutamiento, la exigencia de autenticación por
/// grupo, la resolución del rol contra la base en cada petición (ADR-0004) y los casos de uso.
///
/// La alternativa —validar tokens reales— obligaría a que el pipeline tuviera credenciales de un
/// proveedor externo, y una prueba que depende de la disponibilidad de un tercero falla por razones
/// que no son suyas. La validación del token es responsabilidad de la biblioteca de JWT, no del
/// código propio.
///
/// El correo viaja en el mismo claim que pone el proveedor real (la Action de Post Login de Auth0),
/// así que la API no sabe que está hablando con un doble.
/// </summary>
internal sealed class EsquemaDePrueba(
    IOptionsMonitor<AuthenticationSchemeOptions> opciones,
    ILoggerFactory registro,
    UrlEncoder codificador)
    : AuthenticationHandler<AuthenticationSchemeOptions>(opciones, registro, codificador)
{
    public const string Nombre = "Prueba";

    /// <summary>Cabecera que dice quién hace la petición. Sin ella, la petición es anónima.</summary>
    public const string Cabecera = "X-Correo-De-Prueba";

    private const string ClaimCorreo = "https://ucokudos.co/email";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Cabecera, out var correo) || string.IsNullOrWhiteSpace(correo))
        {
            // Sin cabecera no hay identidad: lo mismo que ocurre con una petición sin token.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identidad = new ClaimsIdentity([new Claim(ClaimCorreo, correo.ToString())], Nombre);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identidad), Nombre);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
