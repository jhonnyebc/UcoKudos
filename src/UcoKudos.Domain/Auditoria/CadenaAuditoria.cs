namespace UcoKudos.Domain.Auditoria;

/// <summary>Resultado de verificar la cadena de auditoría.</summary>
/// <param name="EventosRevisados">Cuántos eventos se recorrieron.</param>
/// <param name="Intacta">La cadena no presenta alteraciones.</param>
/// <param name="PrimerEventoAlterado">Identificador del primer evento donde se rompe, si se rompe.</param>
/// <param name="Detalle">Qué se encontró, en lenguaje llano.</param>
public sealed record ResultadoVerificacion(
    int EventosRevisados,
    bool Intacta,
    long? PrimerEventoAlterado,
    string Detalle)
{
    public static ResultadoVerificacion Vacia() =>
        new(0, true, null, "No hay eventos de auditoría todavía.");

    public static ResultadoVerificacion Correcta(int revisados) =>
        new(revisados, true, null, "La cadena está intacta: se verificaron " + revisados + " eventos.");

    public static ResultadoVerificacion Rota(int revisados, long eventoId, string motivo) =>
        new(revisados, false, eventoId, "La cadena se rompe en el evento " + eventoId + ": " + motivo);
}

/// <summary>
/// Verificación de la cadena de hashes de la auditoría (ADR-0013).
///
/// Es lo que convierte la inmutabilidad en algo demostrable en lugar de declarado, y está pensada
/// para ejecutarse **en vivo**: la plataforma la expone como una consulta, no como un proceso
/// oculto, para poder mostrarla durante la sustentación.
///
/// Comprueba dos cosas por cada evento: que su hash corresponda a sus datos (nadie editó un campo)
/// y que su hash anterior apunte al hash del evento previo (nadie eliminó ni insertó uno en medio).
/// </summary>
public static class CadenaAuditoria
{
    /// <summary>
    /// Verifica una secuencia de eventos en orden de inserción. Se detiene en la primera
    /// discrepancia, porque a partir de ahí todo lo demás es consecuencia.
    /// </summary>
    public static ResultadoVerificacion Verificar(IReadOnlyList<EventoAuditoria> eventosEnOrden)
    {
        ArgumentNullException.ThrowIfNull(eventosEnOrden);

        if (eventosEnOrden.Count == 0)
            return ResultadoVerificacion.Vacia();

        string? hashEsperadoDelAnterior = null;

        for (var i = 0; i < eventosEnOrden.Count; i++)
        {
            var evento = eventosEnOrden[i];

            // Primero el enlace: si el anterior no encaja, alguien eliminó o insertó un evento.
            if (i > 0 && evento.HashAnterior != hashEsperadoDelAnterior)
            {
                return ResultadoVerificacion.Rota(
                    i + 1,
                    evento.Id,
                    "su referencia al evento anterior no coincide, así que se eliminó o se insertó un evento.");
            }

            // Después el contenido: si el hash no corresponde a los datos, alguien editó un campo.
            if (evento.RecalcularHash() != evento.Hash)
            {
                return ResultadoVerificacion.Rota(
                    i + 1,
                    evento.Id,
                    "su hash no corresponde a sus datos, así que el registro fue modificado.");
            }

            hashEsperadoDelAnterior = evento.Hash;
        }

        return ResultadoVerificacion.Correcta(eventosEnOrden.Count);
    }
}
