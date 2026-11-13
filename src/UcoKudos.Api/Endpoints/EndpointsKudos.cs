using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http.Features;
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
        .ExigirCuentaActiva()
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
        .ExigirCuentaActiva()
        .WithName("KudosRecibidos")
        .WithSummary("Kudos que recibió una persona");

        grupo.MapGet("/categoria/{categoriaId:guid}", async (
            Guid categoriaId,
            string? cursor,
            int? tamano,
            IKudosPuertoEntrada puerto,
            CancellationToken ct) => Results.Ok(await puerto.PorCategoriaAsync(categoriaId, cursor, tamano, ct)))
        .ExigirCuentaActiva()
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
        .ExigirCuentaActiva()
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
        .WithSummary("Reserva un adjunto y devuelve su identificador")
        .WithDescription(
            "Primer paso de la carga: valida lo que declara el cliente. Después se sube el contenido a "
            + "la propia API y se confirma.");

        grupo.MapPut("/{kudoId:guid}/adjuntos/{adjuntoId:guid}/contenido", async (
            Guid kudoId,
            Guid adjuntoId,
            IAdjuntosPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            // Segundo tope, el del servidor web, por si el caso de uso no llegara a leer: el máximo
            // de un adjunto son 5 MiB y esto deja un margen. Puede no ser modificable (en las
            // pruebas en memoria, por ejemplo), y en ese caso el caso de uso sigue acotando.
            var limite = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (limite is { IsReadOnly: false })
                limite.MaxRequestBodySize = 6 * 1024 * 1024;

            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            await puerto.GuardarContenidoAsync(contexto, kudoId, adjuntoId, http.Request.Body, ct);

            return Results.NoContent();
        })
        .WithName("SubirContenidoAdjunto")
        .WithSummary("Sube el contenido de un adjunto reservado")
        .WithDescription(
            "El cuerpo es el archivo tal cual. Solo el autor del Kudo. El tamaño se acota mientras se "
            + "lee. Todavía no lo da por bueno: eso lo hace la confirmación.");

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

        grupo.MapGet("/{kudoId:guid}/adjuntos/{adjuntoId:guid}/archivo", async (
            Guid kudoId,
            Guid adjuntoId,
            IAdjuntosPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            var archivo = await puerto.DescargarAsync(contexto, kudoId, adjuntoId, ct);

            // Contenido de un reconocimiento interno: no debe quedar en cachés compartidas.
            http.Response.Headers.CacheControl = "private, no-store";

            // El tipo es el detectado por el contenido, y `nosniff` impide que el navegador lo
            // reinterprete: un archivo que se declaró imagen no puede acabar ejecutándose como página.
            http.Response.Headers.XContentTypeOptions = "nosniff";

            http.Response.Headers.ContentDisposition =
                new ContentDispositionHeaderValue("inline") { FileNameStar = archivo.NombreOriginal }.ToString();

            return Results.File(archivo.Contenido, archivo.Tipo);
        })
        .WithName("DescargarAdjunto")
        .WithSummary("Entrega el archivo de un adjunto confirmado")
        .WithDescription(
            "La autorización se comprueba aquí y cada descarga queda auditada. El archivo no tiene "
            + "ninguna otra ruta de acceso.");

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
