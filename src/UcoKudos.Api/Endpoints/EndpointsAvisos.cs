using System.Globalization;
using UcoKudos.Api.Plataforma;
using UcoKudos.Application.Comun.Entrada.Puertos;
using UcoKudos.Domain.Identidad.Repositorios;

namespace UcoKudos.Api.Endpoints;

/// <summary>
/// Endpoints de avisos en tiempo real (RF-16, ADR-0009).
///
/// SSE y no WebSocket: lo que hace falta es enviar del servidor al cliente, y para eso SSE es la
/// herramienta del tamaño del problema. WebSocket daría comunicación bidireccional que ningún
/// requisito necesita, a cambio de un mantenimiento de conexión más complejo.
///
/// Todo pasa por <see cref="IAvisosPuertoEntrada"/>: la primera versión de este archivo usaba el
/// adaptador de SSE de la infraestructura y la prueba de arquitectura lo detectó como violación de
/// la regla 4. Era una violación real, y este puerto es la corrección.
/// </summary>
public static class EndpointsAvisos
{
    public static void MapearAvisos(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/api/avisos").WithTags("Avisos");

        grupo.MapGet("/en-vivo", async (
            IAvisosPuertoEntrada avisos,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);

            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";

            // Desactiva el almacenamiento intermedio del proxy: con él, los avisos llegarían en
            // bloques y dejarían de ser en tiempo real.
            http.Response.Headers["X-Accel-Buffering"] = "no";

            var (conexionId, lector) = avisos.Abrir(contexto.PersonaId);

            try
            {
                await http.Response.WriteAsync(": conectado\n\n", ct);
                await http.Response.Body.FlushAsync(ct);

                // Latido periódico: las redes móviles cortan las conexiones inactivas, y los
                // intermediarios también. Sin esto, la conexión se caería cada pocos minutos.
                using var latido = new PeriodicTimer(TimeSpan.FromSeconds(20));
                var esperaLatido = latido.WaitForNextTickAsync(ct).AsTask();
                var esperaAviso = lector.WaitToReadAsync(ct).AsTask();

                while (!ct.IsCancellationRequested)
                {
                    var completada = await Task.WhenAny(esperaAviso, esperaLatido);

                    if (completada == esperaAviso)
                    {
                        if (!await esperaAviso)
                            break;

                        while (lector.TryRead(out var cuerpo))
                        {
                            await http.Response.WriteAsync("data: " + cuerpo + "\n\n", ct);
                            await http.Response.Body.FlushAsync(ct);
                        }

                        esperaAviso = lector.WaitToReadAsync(ct).AsTask();
                    }
                    else
                    {
                        await esperaLatido;
                        await http.Response.WriteAsync(": latido\n\n", ct);
                        await http.Response.Body.FlushAsync(ct);
                        esperaLatido = latido.WaitForNextTickAsync(ct).AsTask();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // El cliente cerró la pestaña. No es un error.
            }
            finally
            {
                avisos.Cerrar(contexto.PersonaId, conexionId);
            }

            return Results.Empty;
        })
        .WithName("AvisosEnVivo")
        .WithSummary("Conexión de avisos en tiempo real")
        .WithDescription(
            "Mientras esta conexión esté abierta, los avisos llegan por aquí y no se envía Web Push, "
            + "para no notificar dos veces lo mismo.");


        // --- La bandeja de avisos (RF-16) ---
        //
        // Es lo que hace cierta la promesa de ADR-0009. El canal en vivo es efímero y Web Push
        // puede no llegar nunca —navegador cerrado, suscripción caducada, permiso denegado—, así
        // que el aviso se guarda siempre y esta es la pantalla donde aparece.
        //
        // No recibe ningún identificador de persona: quién pregunta lo dice el token.
        grupo.MapGet("/", async (
            int? tamano,
            IAvisosPuertoEntrada avisos,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            return Results.Ok(await avisos.MisAvisosAsync(contexto, tamano, ct));
        })
        .WithName("MisAvisos")
        .WithSummary("Los avisos de quien pregunta, del más reciente al más antiguo")
        .WithDescription(
            "Incluye cuántos quedan sin leer en total, no solo en esta página: es lo que la interfaz "
            + "necesita para el contador, y calcularlo desde la página daría un número equivocado.");

        grupo.MapPost("/{avisoId:long}/leido", async (
            long avisoId,
            IAvisosPuertoEntrada avisos,
            IPersonaRepositorio personas,
            HttpContext http,
            CancellationToken ct) =>
        {
            var contexto = await ContextoUsuario.ResolverAsync(http, personas, ct);
            await avisos.MarcarLeidoAsync(contexto, avisoId, ct);
            return Results.NoContent();
        })
        .WithName("MarcarAvisoLeido")
        .WithSummary("Marca un aviso propio como leído")
        .WithDescription(
            "Un aviso de otra persona responde igual que uno inexistente: distinguirlos permitiría "
            + "averiguar qué avisos tienen los demás probando identificadores.");
        // La clave pública VAPID NO es secreta: el cliente la necesita para suscribirse. La privada
        // sí, y nunca sale de la configuración del servidor (ADR-0009, ADR-0018).
        grupo.MapGet("/clave-publica", (IAvisosPuertoEntrada avisos) =>
            avisos.ClavePublicaPush() is { } clave
                ? Results.Ok(new { clavePublica = clave })
                : Results.NotFound(new { detalle = "El canal de Web Push no está configurado." }))
        .AllowAnonymous()
        .WithName("ClavePublicaPush")
        .WithSummary("Clave pública VAPID para suscribirse a Web Push");
    }
}

/// <summary>
/// Puntos de salud (ADR-0014). Separados a propósito:
///
///   /salud/vivo  → ¿el proceso está en pie? No comprueba dependencias, así que un fallo de la base
///                  no hace que la infraestructura reinicie el contenedor sin motivo.
///   /salud/listo → ¿puede atender tráfico? Comprueba sus dependencias.
///
/// Ninguno exige autenticación, porque los consulta la infraestructura, y ninguno revela por qué
/// falla: responden si el sistema está listo, no qué tiene roto.
/// </summary>
public static class EndpointsSalud
{
    public static void MapearSalud(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        rutas.MapGet("/salud/vivo", () => Results.Ok(new { estado = "vivo" }))
            .AllowAnonymous()
            .WithTags("Salud")
            .WithName("SaludVivo");

        rutas.MapGet("/salud/listo", async (
            ISaludPuertoEntrada salud,
            CancellationToken ct) => await salud.EstaListoAsync(ct)
                ? Results.Ok(new { estado = "listo" })
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
        .AllowAnonymous()
        .WithTags("Salud")
        .WithName("SaludListo");

        // Información de la versión desplegada. Sirve para comprobar de un vistazo que el despliegue
        // puso la imagen que se esperaba (ADR-0016).
        rutas.MapGet("/version", () =>
        {
            var ensamblado = typeof(EndpointsSalud).Assembly;
            var version = ensamblado.GetName().Version?.ToString(3) ?? "desconocida";

            return Results.Ok(new
            {
                aplicacion = "UCO Kudos",
                version,
                instante = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            });
        })
        .AllowAnonymous()
        .WithTags("Salud")
        .WithName("Version");
    }
}
