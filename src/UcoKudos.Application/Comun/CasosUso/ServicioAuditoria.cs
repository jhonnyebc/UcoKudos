using System.Text.Json;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Auditoria.Repositorios;

namespace UcoKudos.Application.Comun.CasosUso;

/// <summary>
/// Registra eventos de auditoría encadenándolos (ADR-0013). Los casos de uso lo usan en lugar de
/// construir el evento a mano, por dos razones: hay que pedirle a la base el hash del último evento
/// para encadenar, y el JSON tiene que ser canónico.
///
/// Lo del JSON canónico no es un detalle estético: el hash se calcula sobre ese texto, y el motor
/// reordena las claves de un documento JSON al guardarlo. Si no se serializara de forma estable, la
/// verificación de la cadena fallaría al releer aunque nadie hubiera alterado nada.
/// </summary>
public sealed class ServicioAuditoria(IEventoAuditoriaRepositorio repositorio)
{
    private static readonly JsonSerializerOptions Canonico = new()
    {
        // Sin indentación y con las claves en orden de declaración: el mismo objeto da siempre el
        // mismo texto, que es la condición para que el hash sea reproducible.
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Encola un evento de auditoría en la unidad de trabajo. No confirma: lo hace el caso de uso
    /// junto con el cambio que audita.
    /// </summary>
    public async Task RegistrarAsync(
        ContextoPeticion contexto,
        string accion,
        string tipoEntidad,
        Guid entidadId,
        object? datos,
        DateTimeOffset ahora,
        CancellationToken ct)
    {
        var hashAnterior = await repositorio.UltimoHashAsync(ct);
        var json = datos is null ? "{}" : JsonSerializer.Serialize(datos, Canonico);

        repositorio.Agregar(new EventoAuditoria(
            contexto.PersonaId,
            accion,
            tipoEntidad,
            entidadId,
            json,
            contexto.CorrelacionId,
            hashAnterior,
            ahora));
    }

    /// <summary>
    /// Registra un evento del sistema, sin actor. Lo usa el proceso de notificaciones, que no actúa
    /// en nombre de nadie.
    /// </summary>
    public async Task RegistrarDelSistemaAsync(
        string accion,
        string tipoEntidad,
        Guid entidadId,
        object? datos,
        string? correlacionId,
        DateTimeOffset ahora,
        CancellationToken ct)
    {
        var hashAnterior = await repositorio.UltimoHashAsync(ct);
        var json = datos is null ? "{}" : JsonSerializer.Serialize(datos, Canonico);

        repositorio.Agregar(new EventoAuditoria(
            null, accion, tipoEntidad, entidadId, json, correlacionId, hashAnterior, ahora));
    }

    /// <summary>
    /// Registra un acceso rechazado por falta de permiso (QA-SEG-01). Se audita a propósito: un
    /// intento fallido suele informar más que uno exitoso.
    /// </summary>
    public Task RegistrarAccesoRechazadoAsync(
        ContextoPeticion contexto,
        string queIntento,
        Guid entidadId,
        DateTimeOffset ahora,
        CancellationToken ct) =>
        RegistrarAsync(
            contexto,
            AccionesAuditoria.AccesoRechazado,
            "Autorizacion",
            entidadId,
            new { intento = queIntento, rol = contexto.Rol.Nombre },
            ahora,
            ct);
}
