using UcoKudos.Api.Plataforma;
using UcoKudos.Application.Kudos.Entrada.Dto;
using UcoKudos.Application.Kudos.Entrada.Puertos;
using UcoKudos.Domain.Identidad.Repositorios;

namespace UcoKudos.Api.Endpoints;

/// <summary>
/// Endpoints de Kudos (RF-07, RF-08, RF-13).
///
/// Los endpoints solo conocen **puertos de entrada y DTO** (ADR-0001, regla 4): traducen HTTP a una
/// llamada de caso de uso y nada más. Ninguna regla de negocio vive aquí, porque la segunda ruta que
/// hiciera lo mismo la duplicaría.
/// </summary>
public static class EndpointsKudos
{
    public static void MapearKudos(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/api/kudos").WithTags("Kudos");

        // --- Publicar: la funcionalidad obligatoria (RF-07) ---
        grupo.MapPost("/", async (
            PublicarKudoDto datos,
            IKudosPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            var resultado = await puerto.PublicarAsync(contexto, datos, ct);

            return Results.Created("/api/kudos/" + resultado.Id, resultado);
        })
        .WithName("PublicarKudo")
        .WithSummary("Publica un Kudo y dispara sus cinco efectos")
        .WithDescription(
            "Persiste el Kudo y su auditoría en una transacción, y deposita el evento que produce el "
            + "correo, el WhatsApp y la notificación en tiempo real. Responde sin esperar a los "
            + "proveedores externos.");

        // --- Muro (RF-13, DRV-002) ---
        grupo.MapGet("/", async (
            string? cursor,
            int? tamano,
            IKudosPuertoEntrada puerto,
            CancellationToken ct) => Results.Ok(await puerto.MuroAsync(cursor, tamano, ct)))
        .WithName("ConsultarMuro")
        .WithSummary("Muro de Kudos, paginado por cursor")
        .WithDescription(
            "Paginación por cursor y no por número de página: la última página cuesta lo mismo que la "
            + "primera, y no se repiten ni se saltan Kudos recién publicados.");

        grupo.MapGet("/recibidos/{personaId:guid}", async (
            Guid personaId,
            string? cursor,
            int? tamano,
            IKudosPuertoEntrada puerto,
            CancellationToken ct) => Results.Ok(await puerto.RecibidosAsync(personaId, cursor, tamano, ct)))
        .WithName("KudosRecibidos")
        .WithSummary("Kudos que recibió una persona");

        grupo.MapGet("/categoria/{categoriaId:guid}", async (
            Guid categoriaId,
            string? cursor,
            int? tamano,
            IKudosPuertoEntrada puerto,
            CancellationToken ct) => Results.Ok(await puerto.PorCategoriaAsync(categoriaId, cursor, tamano, ct)))
        .WithName("KudosPorCategoria")
        .WithSummary("Muro filtrado por categoría");

        grupo.MapGet("/{kudoId:guid}", async (
            Guid kudoId,
            IKudosPuertoEntrada puerto,
            CancellationToken ct) =>
        {
            var detalle = await puerto.DetalleAsync(kudoId, ct);
            return detalle is null ? Results.NotFound() : Results.Ok(detalle);
        })
        .WithName("DetalleKudo")
        .WithSummary("Detalle de un Kudo con sus adjuntos");

        MapearAdjuntos(grupo);
    }

    /// <summary>
    /// Adjuntos en dos pasos (RF-08, ADR-0012): la API prepara una URL prefirmada, el cliente sube
    /// directo al almacenamiento y luego confirma. Los megabytes no atraviesan la aplicación.
    /// </summary>
    private static void MapearAdjuntos(IEndpointRouteBuilder grupo)
    {
        grupo.MapPost("/{kudoId:guid}/adjuntos/intencion", async (
            Guid kudoId,
            IntencionAdjuntoDto datos,
            IAdjuntosPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            return Results.Ok(await puerto.PrepararSubidaAsync(contexto, kudoId, datos, ct));
        })
        .WithName("PrepararAdjunto")
        .WithSummary("Pide una URL prefirmada para subir un adjunto")
        .WithDescription("El cliente sube el archivo directo al almacenamiento, sin pasar por la API.");

        grupo.MapPost("/{kudoId:guid}/adjuntos/{adjuntoId:guid}/confirmar", async (
            Guid kudoId,
            Guid adjuntoId,
            IAdjuntosPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            await puerto.ConfirmarSubidaAsync(contexto, kudoId, adjuntoId, ct);

            return Results.NoContent();
        })
        .WithName("ConfirmarAdjunto")
        .WithSummary("Confirma la carga tras verificar el contenido real del archivo")
        .WithDescription(
            "El tipo se determina por los primeros bytes del archivo, no por la extensión ni por lo "
            + "que declaró el cliente: las dos las controla quien sube el archivo.");

        grupo.MapGet("/{kudoId:guid}/adjuntos/{adjuntoId:guid}/descarga", async (
            Guid kudoId,
            Guid adjuntoId,
            IAdjuntosPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            var url = await puerto.UrlDeDescargaAsync(contexto, kudoId, adjuntoId, ct);

            return Results.Ok(new { url });
        })
        .WithName("DescargarAdjunto")
        .WithSummary("Entrega una URL de descarga de vida corta")
        .WithDescription("La autorización se comprueba antes de firmar. El enlace vence en minutos.");

        grupo.MapDelete("/{kudoId:guid}/adjuntos/{adjuntoId:guid}", async (
            Guid kudoId,
            Guid adjuntoId,
            IAdjuntosPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            await puerto.EliminarAsync(contexto, kudoId, adjuntoId, ct);

            return Results.NoContent();
        })
        .WithName("EliminarAdjunto")
        .WithSummary("Marca un adjunto como eliminado")
        .WithDescription("Marca en lugar de borrar, así un borrado accidental es reversible.");
    }
}
