using UcoKudos.Api.Plataforma;
using UcoKudos.Application.Identidad.Entrada.Dto;
using UcoKudos.Application.Identidad.Entrada.Puertos;
using UcoKudos.Domain.Identidad.Repositorios;

namespace UcoKudos.Api.Endpoints;

/// <summary>
/// Administración de usuarios. Quién administra a quién es la cadena del enunciado; la regla la
/// aplica el caso de uso, aquí solo se enruta.
/// </summary>
public static class EndpointsUsuarios
{
    public static void MapearUsuarios(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/api/usuarios").WithTags("Usuarios");

        grupo.MapGet("/", async (
            string? busqueda,
            IUsuariosPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            return Results.Ok(await puerto.AdministradosAsync(contexto, busqueda, ct));
        })
        .WithName("UsuariosAdministrados")
        .WithSummary("Las personas que quien consulta administra")
        .WithDescription(
            "Estudiantes y docentes de sus programas para un Coordinador de Programa; coordinadores de "
            + "programa de su facultad para uno de Facultad; y así hasta el Administrador, que "
            + "administra a administradores y rectores.");

        grupo.MapPost("/{personaId:guid}/desactivar", async (
            Guid personaId,
            MotivoDto datos,
            IUsuariosPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            await puerto.DesactivarAsync(contexto, personaId, datos.Motivo, ct);

            return Results.NoContent();
        })
        .WithName("DesactivarUsuario")
        .WithSummary("Desactiva una cuenta: pierde el acceso en la siguiente petición")
        .WithDescription("No borra nada: conserva su rol, sus Kudos y su historia. Reactivarla la deja como estaba.");

        grupo.MapPost("/{personaId:guid}/reactivar", async (
            Guid personaId,
            IUsuariosPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            await puerto.ReactivarAsync(contexto, personaId, ct);

            return Results.NoContent();
        })
        .WithName("ReactivarUsuario")
        .WithSummary("Reactiva una cuenta desactivada");

        grupo.MapPost("/{personaId:guid}/rol", async (
            Guid personaId,
            CambiarRolDto datos,
            IUsuariosPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            await puerto.CambiarRolAsync(contexto, personaId, datos, ct);

            return Results.NoContent();
        })
        .WithName("CambiarRolUsuario")
        .WithSummary("Cambia el rol de una persona")
        .WithDescription(
            "Quien lo cambia tiene que administrar a la persona en su rol actual y aprobar el rol "
            + "nuevo en el ámbito nuevo. El rol anterior se revoca y queda el rastro.");
    }
}
