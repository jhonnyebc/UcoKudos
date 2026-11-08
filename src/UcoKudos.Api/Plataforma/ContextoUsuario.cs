using System.Diagnostics;
using System.Security.Claims;
using UcoKudos.Application.Comun;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Identidad.Repositorios;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.Api.Plataforma;

/// <summary>
/// Resuelve el contexto de la petición a partir del token (ADR-0004).
///
/// Es el lugar donde se materializa la decisión más característica de la autorización de esta
/// plataforma: **el rol no viaja en el token**. El token aporta la identidad, el correo
/// institucional; el rol, el ámbito y el estado de la cuenta se resuelven contra la base **en cada
/// petición**.
///
/// El costo es una consulta de una fila con índice. El beneficio es doble: desactivar una cuenta
/// surte efecto de inmediato en lugar de cuando expire el token, y el ámbito por facultad y programa
/// no tendría forma mantenible de caber en un claim.
/// </summary>
public static class ContextoUsuario
{
    /// <summary>
    /// Claim del que sale el correo institucional. Se intentan varios porque los proveedores no
    /// coinciden en cuál usan.
    /// </summary>
    private static readonly string[] ClaimsDeCorreo =
    [
        ClaimTypes.Email,
        "email",
        "preferred_username",
        ClaimTypes.NameIdentifier
    ];

    public static async Task<ContextoPeticion> ResolverAsync(
        HttpContext http, IPersonaRepositorio personas, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(personas);

        var correo = ClaimsDeCorreo
            .Select(claim => http.User.FindFirst(claim)?.Value)
            .FirstOrDefault(valor => !string.IsNullOrWhiteSpace(valor))
            ?? throw new ExcepcionAutorizacion("El token no identifica a ninguna persona.");

        var persona = await personas.PorCorreoAsync(correo, ct)
                      ?? throw new ExcepcionAutorizacion("No existe una cuenta para " + correo + ".");

        // El estado se comprueba aquí, en cada petición y sin caché: una cuenta desactivada pierde el
        // acceso de inmediato.
        if (!persona.PuedeAcceder)
            throw new ExcepcionAutorizacion("La cuenta no está activa.");

        var asignacion = await personas.RolActivoAsync(persona.Id, ct)
                         ?? throw new ExcepcionAutorizacion("La cuenta no tiene un rol activo.");

        if (!asignacion.Activa)
            throw new ExcepcionAutorizacion("El rol de la cuenta fue revocado.");

        var ambito = AmbitoUsuario.De(asignacion);

        return new ContextoPeticion(ambito, Correlacion(http));
    }

    /// <summary>
    /// Identificador de correlación de la petición. Es el de la traza actual, de modo que la
    /// auditoría y las trazas se pueden unir con un solo valor (QA-OBS-01, ADR-0014).
    /// </summary>
    public static string Correlacion(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return Activity.Current?.Id ?? http.TraceIdentifier;
    }
}
