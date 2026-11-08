namespace UcoKudos.Application.Identidad.Entrada.Dto;

/// <summary>
/// Datos del registro (RF-01). El rol se **solicita**, no se asigna: queda pendiente del aprobador
/// que corresponda según la jerarquía (RF-03).
/// </summary>
public sealed record RegistrarCuentaDto(
    string CorreoInstitucional,
    string Celular,
    string NombreCompleto,
    short RolSolicitado,
    Guid? FacultadId,
    Guid? ProgramaId);

/// <summary>
/// Una persona vista desde la API. No incluye el celular: es un dato de contacto que la plataforma
/// usa para avisar, no algo que haga falta mostrar.
/// </summary>
public sealed record PersonaDto(
    Guid Id,
    string CorreoInstitucional,
    string NombreCompleto,
    string Rol,
    string EstadoCuenta,
    bool CorreoVerificado,
    bool CelularVerificado);

/// <summary>Motivo del rechazo de una solicitud. Va al aviso que recibe el solicitante.</summary>
public sealed record RechazarSolicitudDto(string Motivo);

/// <summary>Un programa académico, para elegir al registrarse.</summary>
public sealed record ProgramaDto(Guid Id, string Nombre, string Codigo);

/// <summary>Una facultad con sus programas activos.</summary>
public sealed record FacultadDto(Guid Id, string Nombre, string Codigo, IReadOnlyList<ProgramaDto> Programas);

/// <summary>Un rol del catálogo, para que el cliente muestre las opciones del registro.</summary>
public sealed record RolDto(short Id, string Nombre, string Ambito);
