using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.Api.Plataforma;

/// <summary>
/// Traduce los errores de negocio a respuestas HTTP (ADR-0001). El dominio no conoce códigos de
/// estado, y esta es la única pieza que hace la conversión.
///
/// La tabla es deliberada:
///
///   ExcepcionAutorizacion           → 403, porque el usuario está identificado pero no le toca.
///   ExcepcionConflictoConcurrencia  → 409, para que pueda reintentar en lugar de sobrescribir algo.
///   ExcepcionDominio                → 400, una invariante o una transición no permitida.
///   Cualquier otra                  → 500, sin filtrar nada de dentro.
///
/// **Toda** respuesta de error lleva el identificador de traza. Es lo que permite que un usuario
/// reporte un error con un identificador y se encuentre la traza exacta (QA-OBS-01), y lo que hace
/// innecesario devolver una traza de pila, que es lo que no se debe filtrar nunca.
/// </summary>
public sealed class ManejadorExcepciones(IProblemDetailsService problemas) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext contexto, Exception excepcion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(excepcion);

        var (estado, titulo, detalle) = Traducir(excepcion);

        contexto.Response.StatusCode = estado;

        return await problemas.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = contexto,
            Exception = excepcion,
            ProblemDetails = new ProblemDetails
            {
                Status = estado,
                Title = titulo,
                Detail = detalle,
                Extensions =
                {
                    // El identificador de traza, que es lo que une esta respuesta con los logs, las
                    // trazas y los eventos de auditoría de la misma petición.
                    ["traceId"] = Activity.Current?.Id ?? contexto.TraceIdentifier
                }
            }
        });
    }

    private static (int Estado, string Titulo, string Detalle) Traducir(Exception excepcion) => excepcion switch
    {
        ExcepcionAutorizacion error =>
            (StatusCodes.Status403Forbidden, "No autorizado", error.Message),

        ExcepcionConflictoConcurrencia error =>
            (StatusCodes.Status409Conflict, "Conflicto de concurrencia", error.Message),

        ExcepcionDominio error =>
            (StatusCodes.Status400BadRequest, "Solicitud no válida", error.Message),

        // Un error inesperado no cuenta nada de dentro: ni el tipo, ni el mensaje, ni la traza de
        // pila. Quien tenga que diagnosticarlo usa el identificador de traza.
        _ => (StatusCodes.Status500InternalServerError,
              "Error interno",
              "Ocurrió un error inesperado. Use el identificador de traza para reportarlo.")
    };
}
