using UcoKudos.Api.Plataforma;
using UcoKudos.Application.Academico.Entrada.Dto;
using UcoKudos.Application.Academico.Entrada.Puertos;
using UcoKudos.Domain.Identidad.Repositorios;

namespace UcoKudos.Api.Endpoints;

/// <summary>
/// Gestión académica (RF-05): qué programas tiene cada persona y la administración de facultades y
/// programas. Las reglas de quién puede qué las aplica el caso de uso; aquí solo se enruta.
/// </summary>
public static class EndpointsAcademico
{
    public static void MapearAcademico(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/api/academico").WithTags("Gestión académica");

        grupo.MapGet("/mis-programas", async (
            IAcademicoPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            return Results.Ok(await puerto.ProgramasDeAsync(contexto, contexto.PersonaId, ct));
        })
        .WithName("MisProgramas")
        .WithSummary("Los programas de quien consulta");

        grupo.MapGet("/personas/{personaId:guid}/programas", async (
            Guid personaId,
            IAcademicoPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            return Results.Ok(await puerto.ProgramasDeAsync(contexto, personaId, ct));
        })
        .WithName("ProgramasDePersona")
        .WithSummary("Los programas de una persona: lo que estudia, enseña o acompaña")
        .WithDescription(
            "Solo la propia persona, o quien manda sobre ella dentro de su ámbito: un Coordinador de "
            + "Programa ve a los de sus programas, no a los de otros.");

        grupo.MapPost("/personas/{personaId:guid}/vinculos", async (
            Guid personaId,
            AsignarVinculoDto datos,
            IAcademicoPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            await puerto.AsignarVinculoAsync(contexto, personaId, datos, ct);

            return Results.NoContent();
        })
        .WithName("AsignarVinculo")
        .WithSummary("Vincula a un estudiante o a un docente con un programa")
        .WithDescription("Quien vincula tiene que mandar sobre la persona y alcanzar el programa.");

        grupo.MapDelete("/personas/{personaId:guid}/vinculos/{programaId:guid}/{tipo}", async (
            Guid personaId,
            Guid programaId,
            string tipo,
            IAcademicoPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            await puerto.QuitarVinculoAsync(contexto, personaId, programaId, tipo, ct);

            return Results.NoContent();
        })
        .WithName("QuitarVinculo")
        .WithSummary("Quita un vínculo con un programa");

        grupo.MapGet("/estructura", async (
            IAcademicoPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            return Results.Ok(await puerto.EstructuraCompletaAsync(contexto, ct));
        })
        .WithName("EstructuraCompleta")
        .WithSummary("Facultades y programas, también los desactivados")
        .WithDescription("Solo Administrador, Rector y Coordinador Académico.");

        grupo.MapPost("/facultades", async (
            DatosFacultadDto datos,
            IAcademicoPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            var creada = await puerto.CrearFacultadAsync(contexto, datos, ct);

            return Results.Created("/api/academico/facultades/" + creada.Id, creada);
        })
        .WithName("CrearFacultad")
        .WithSummary("Crea una facultad");

        grupo.MapPut("/facultades/{facultadId:guid}", async (
            Guid facultadId,
            DatosFacultadDto datos,
            IAcademicoPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            await puerto.ActualizarFacultadAsync(contexto, facultadId, datos, ct);

            return Results.NoContent();
        })
        .WithName("ActualizarFacultad")
        .WithSummary("Cambia el nombre y el código de una facultad");

        grupo.MapPost("/facultades/{facultadId:guid}/estado", async (
            Guid facultadId,
            bool activa,
            IAcademicoPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            await puerto.CambiarEstadoFacultadAsync(contexto, facultadId, activa, ct);

            return Results.NoContent();
        })
        .WithName("CambiarEstadoFacultad")
        .WithSummary("Activa o desactiva una facultad")
        .WithDescription(
            "Se desactiva en lugar de borrarse: sus personas, roles y Kudos siguen donde están. "
            + "Solo deja de ofrecerse al registrarse.");

        grupo.MapPost("/facultades/{facultadId:guid}/programas", async (
            Guid facultadId,
            DatosProgramaDto datos,
            IAcademicoPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            var creado = await puerto.CrearProgramaAsync(contexto, facultadId, datos, ct);

            return Results.Created("/api/academico/programas/" + creado.Id, creado);
        })
        .WithName("CrearPrograma")
        .WithSummary("Crea un programa dentro de una facultad");

        grupo.MapPut("/programas/{programaId:guid}", async (
            Guid programaId,
            DatosProgramaDto datos,
            IAcademicoPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            await puerto.ActualizarProgramaAsync(contexto, programaId, datos, ct);

            return Results.NoContent();
        })
        .WithName("ActualizarPrograma")
        .WithSummary("Cambia el nombre y el código de un programa");

        grupo.MapPost("/programas/{programaId:guid}/estado", async (
            Guid programaId,
            bool activo,
            IAcademicoPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            await puerto.CambiarEstadoProgramaAsync(contexto, programaId, activo, ct);

            return Results.NoContent();
        })
        .WithName("CambiarEstadoPrograma")
        .WithSummary("Activa o desactiva un programa")
        .WithDescription(
            "Se desactiva en lugar de borrarse: sus personas, roles y Kudos siguen donde están. "
            + "Deja de ofrecerse al registrarse y no admite vínculos nuevos.");
    }
}
