using UcoKudos.Api.Plataforma;
using UcoKudos.Application.Identidad.Entrada.Dto;
using UcoKudos.Application.Identidad.Entrada.Puertos;
using UcoKudos.Application.Interaccion.Entrada.Dto;
using UcoKudos.Application.Interaccion.Entrada.Puertos;
using UcoKudos.Application.Kudos.Entrada.Dto;
using UcoKudos.Application.Kudos.Entrada.Puertos;
using UcoKudos.Domain.Identidad.Repositorios;

namespace UcoKudos.Api.Endpoints;

/// <summary>Endpoints de identidad y estructura académica (RF-01 a RF-05).</summary>
public static class EndpointsIdentidad
{
    public static void MapearIdentidad(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/api/identidad").WithTags("Identidad");

        // El registro es el único endpoint que no exige token: todavía no hay cuenta.
        grupo.MapPost("/registro", async (
            RegistrarCuentaDto datos,
            IIdentidadPuertoEntrada puerto,
            HttpContext http,
            CancellationToken ct) =>
        {
            var persona = await puerto.RegistrarAsync(datos, ContextoUsuario.Correlacion(http), ct);
            return Results.Created("/api/identidad/personas/" + persona.Id, persona);
        })
        .AllowAnonymous()
        .WithName("RegistrarCuenta")
        .WithSummary("Registra una cuenta, que queda pendiente de aprobación")
        .WithDescription(
            "La cuenta se activa solo con las tres condiciones de RF-02: solicitud aprobada, correo "
            + "verificado y celular verificado.");

        grupo.MapGet("/yo", async (
            IIdentidadPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            var perfil = await puerto.MiPerfilAsync(contexto.PersonaId, ct);

            return perfil is null ? Results.NotFound() : Results.Ok(perfil);
        })
        .WithName("MiPerfil")
        .WithSummary("El perfil del usuario autenticado");

        grupo.MapGet("/solicitudes/pendientes", async (
            IIdentidadPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            return Results.Ok(await puerto.PendientesAsync(contexto, ct));
        })
        .WithName("SolicitudesPendientes")
        .WithSummary("Las solicitudes que este usuario debe resolver")
        .WithDescription(
            "Limitadas a su ámbito: un Coordinador de Programa ve las de sus programas, no todas.");

        grupo.MapPost("/solicitudes/{personaId:guid}/aprobar", async (
            Guid personaId,
            IIdentidadPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            await puerto.AprobarAsync(contexto, personaId, ct);

            return Results.NoContent();
        })
        .WithName("AprobarSolicitud")
        .WithSummary("Aprueba una solicitud según el flujo de la jerarquía (RF-03)");

        grupo.MapPost("/solicitudes/{personaId:guid}/rechazar", async (
            Guid personaId,
            RechazarSolicitudDto datos,
            IIdentidadPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            await puerto.RechazarAsync(contexto, personaId, datos.Motivo, ct);

            return Results.NoContent();
        })
        .WithName("RechazarSolicitud")
        .WithSummary("Rechaza una solicitud con su motivo");

        grupo.MapGet("/personas", async (
            string? busqueda,
            int? tamano,
            IIdentidadPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            return Results.Ok(await puerto.BuscarPersonasAsync(contexto, busqueda, tamano, ct));
        })
        .WithName("BuscarPersonas")
        .WithSummary("Personas activas cuyo nombre contiene el texto, para elegir destinatario")
        .WithDescription(
            "Devuelve solo identificador y nombre: el correo y el celular no salen de aquí. " +
            "Excluye a quien busca, porque el dominio prohíbe reconocerse a uno mismo. " +
            "Con menos de dos caracteres devuelve una lista vacía en lugar de un error.");

        grupo.MapGet("/estructura", async (
            IIdentidadPuertoEntrada puerto,
            CancellationToken ct) => Results.Ok(await puerto.EstructuraAsync(ct)))
        .AllowAnonymous()
        .WithName("EstructuraAcademica")
        .WithSummary("Facultades con sus programas, para elegir al registrarse");

        grupo.MapGet("/roles", (IIdentidadPuertoEntrada puerto) => Results.Ok(puerto.Roles()))
            .AllowAnonymous()
            .WithName("Roles")
            .WithSummary("Los siete roles institucionales del enunciado");
    }
}

/// <summary>Endpoints de categorías (RF-06).</summary>
public static class EndpointsCategorias
{
    public static void MapearCategorias(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/api/categorias").WithTags("Categorías");

        grupo.MapGet("/", async (
            ICategoriasPuertoEntrada puerto,
            CancellationToken ct) => Results.Ok(await puerto.ActivasAsync(ct)))
        .ExigirCuentaActiva()
        .WithName("CategoriasActivas")
        .WithSummary("Las categorías disponibles para publicar");

        grupo.MapGet("/todas", async (
            ICategoriasPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            return Results.Ok(await puerto.TodasAsync(contexto, ct));
        })
        .WithName("TodasLasCategorias")
        .WithSummary("Todas, incluidas las inactivas. Solo para quien administra");

        grupo.MapPost("/", async (
            GuardarCategoriaDto datos,
            ICategoriasPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            var categoria = await puerto.CrearAsync(contexto, datos, ct);

            return Results.Created("/api/categorias/" + categoria.Id, categoria);
        })
        .WithName("CrearCategoria")
        .WithSummary("Crea una categoría")
        .WithDescription("Solo Administrador, Rector y Coordinador Académico (RF-06).");

        grupo.MapPut("/{id:guid}", async (
            Guid id,
            GuardarCategoriaDto datos,
            ICategoriasPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            await puerto.ActualizarAsync(contexto, id, datos, ct);

            return Results.NoContent();
        })
        .WithName("ActualizarCategoria")
        .WithSummary("Renombra una categoría o cambia su descripción");

        grupo.MapPost("/{id:guid}/estado", async (
            Guid id,
            bool activa,
            ICategoriasPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            await puerto.CambiarEstadoAsync(contexto, id, activa, ct);

            return Results.NoContent();
        })
        .WithName("CambiarEstadoCategoria")
        .WithSummary("Activa o desactiva una categoría")
        .WithDescription("Se desactiva en lugar de borrarse: los Kudos publicados conservan su categoría.");
    }
}

/// <summary>Endpoints de interacción y moderación (RF-09 a RF-12).</summary>
public static class EndpointsInteraccion
{
    public static void MapearInteraccion(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/api/kudos/{kudoId:guid}").WithTags("Interacción");

        grupo.MapPost("/replicas", async (
            Guid kudoId,
            ResponderDto datos,
            IInteraccionPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            var replica = await puerto.ResponderAsync(contexto, kudoId, datos, ct);

            return Results.Created("/api/kudos/" + kudoId + "/replicas/" + replica.Id, replica);
        })
        .WithName("Responder")
        .WithSummary("Responde a un Kudo o a otra réplica")
        .WithDescription("Sin límite de profundidad, de cantidad ni de longitud (RF-09).");

        grupo.MapGet("/replicas", async (
            Guid kudoId,
            IInteraccionPuertoEntrada puerto,
            CancellationToken ct) => Results.Ok(await puerto.HiloAsync(kudoId, ct)))
        .ExigirCuentaActiva()
        .WithName("Hilo")
        .WithSummary("El hilo completo, ya en el orden en que se debe pintar")
        .WithDescription(
            "Llega ordenado por ruta, que es el orden anidado de presentación: el cliente solo usa la "
            + "profundidad para la sangría.");

        var interaccion = rutas.MapGroup("/api/interaccion").WithTags("Interacción");

        interaccion.MapPost("/calificaciones", async (
            CalificarDto datos,
            IInteraccionPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            await puerto.CalificarAsync(contexto, datos, ct);

            return Results.NoContent();
        })
        .WithName("Calificar")
        .WithSummary("Califica un Kudo o una réplica de 1 a 5 estrellas")
        .WithDescription("Una calificación activa por usuario y contenido; cambiarla desactiva la anterior.");

        var moderacion = rutas.MapGroup("/api/moderacion").WithTags("Moderación");

        moderacion.MapPost("/reportes", async (
            ReportarDto datos,
            IModeracionPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            await puerto.ReportarAsync(contexto, datos, ct);

            return Results.Accepted();
        })
        .WithName("Reportar")
        .WithSummary("Reporta contenido inapropiado")
        .WithDescription("Lo puede hacer cualquier usuario (RF-11).");

        moderacion.MapPost("/vetos", async (
            VetarDto datos,
            IModeracionPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            return Results.Ok(await puerto.VetarAsync(contexto, datos, ct));
        })
        .WithName("Vetar")
        .WithSummary("Veta contenido, en cascada si es una réplica")
        .WithDescription(
            "Solo Rector y Coordinador Académico (RF-12). El Administrador del Sistema no veta, "
            + "aunque sea el rol más alto: moderar es una atribución académica, no técnica.");
    }
}

/// <summary>
/// Endpoints de auditoría (RF-17).
///
/// La verificación de la cadena se expone como consulta y no como proceso oculto a propósito: es lo
/// que se ejecuta en vivo para demostrar que el registro es inalterable (ADR-0013).
/// </summary>
public static class EndpointsAuditoria
{
    public static void MapearAuditoria(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/api/auditoria").WithTags("Auditoría");

        grupo.MapGet("/verificacion", async (
            int? maximo,
            IAuditoriaPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            return Results.Ok(await puerto.VerificarCadenaAsync(contexto, maximo ?? 1000, ct));
        })
        .WithName("VerificarAuditoria")
        .WithSummary("Verifica la cadena de hashes de la auditoría")
        .WithDescription(
            "Recorre la cadena comprobando que el hash de cada evento corresponda a sus datos y que "
            + "apunte al anterior. Detecta tanto una edición como una eliminación.");

        grupo.MapGet("/correlacion/{correlacionId}", async (
            string correlacionId,
            IAuditoriaPuertoEntrada puerto,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            return Results.Ok(await puerto.PorCorrelacionAsync(contexto, correlacionId, ct));
        })
        .WithName("AuditoriaPorCorrelacion")
        .WithSummary("Los eventos de una misma petición")
        .WithDescription("Es cómo se reconstruye una publicación concreta de punta a punta (QA-OBS-01).");
    }
}
