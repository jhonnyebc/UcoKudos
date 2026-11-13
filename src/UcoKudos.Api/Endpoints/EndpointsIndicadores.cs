using UcoKudos.Api.Plataforma;
using UcoKudos.Application.Indicadores;
using UcoKudos.Domain.Identidad.Repositorios;

namespace UcoKudos.Api.Endpoints;

/// <summary>Indicadores de gestión: reportes agregados del uso de la plataforma, por ámbito.</summary>
public static class EndpointsIndicadores
{
    public static void MapearIndicadores(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/api/indicadores").WithTags("Indicadores");

        grupo.MapGet("/", async (
            IIndicadoresPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            return Results.Ok(await puerto.ConsultarAsync(contexto, ct));
        })
        .WithName("Indicadores")
        .WithSummary("Indicadores del ámbito de quien consulta")
        .WithDescription(
            "Kudos publicados, personas activas, calificación promedio, reportes pendientes (solo para "
            + "quien modera), Kudos por categoría y por mes, y quiénes reconocen y son reconocidos. "
            + "Del Coordinador de Programa hacia arriba, cada uno sobre su ámbito. Lo vetado no cuenta.");
    }
}
