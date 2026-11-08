namespace UcoKudos.Domain.Transversal;

/// <summary>
/// Error de una regla de negocio: una invariante que no se cumple o una transición no permitida
/// (ADR-0001). La capa de interfaces la traduce a una respuesta 400 con el formato de problema
/// HTTP; el dominio no conoce códigos de estado.
/// </summary>
public class ExcepcionDominio(string mensaje) : Exception(mensaje);

/// <summary>
/// La operación compite con otra que ya se confirmó (concurrencia optimista, ADR-0005). Se traduce
/// a 409 para que el usuario pueda reintentar, en lugar de sobrescribir en silencio.
/// </summary>
public sealed class ExcepcionConflictoConcurrencia(string mensaje) : ExcepcionDominio(mensaje);

/// <summary>
/// El actor no tiene permiso sobre el recurso según la jerarquía institucional (ADR-0004).
/// Se traduce a 403 y el intento queda auditado (QA-SEG-01, ADR-0013).
/// </summary>
public sealed class ExcepcionAutorizacion(string mensaje) : ExcepcionDominio(mensaje);
