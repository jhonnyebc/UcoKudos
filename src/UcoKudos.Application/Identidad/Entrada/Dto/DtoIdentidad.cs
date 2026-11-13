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

/// <summary>Un código que alguien recibió, para comprobar su correo o su celular.</summary>
/// <param name="CorreoInstitucional">El correo con el que se registró.</param>
/// <param name="Tipo">«correo» o «celular».</param>
/// <param name="Codigo">Los seis dígitos recibidos.</param>
public sealed record VerificarContactoDto(string CorreoInstitucional, string Tipo, string Codigo);

/// <summary>Pedir códigos nuevos para una cuenta pendiente.</summary>
public sealed record ReenviarCodigosDto(string CorreoInstitucional);

/// <summary>En qué punto está el registro de una cuenta: qué falta para que quede activa.</summary>
public sealed record EstadoRegistroDto(
    bool CorreoVerificado,
    bool CelularVerificado,
    bool SolicitudAprobada,
    string EstadoCuenta);

/// <summary>Motivo del rechazo de una solicitud. Va al aviso que recibe el solicitante.</summary>
public sealed record RechazarSolicitudDto(string Motivo);

/// <summary>Un programa académico, para elegir al registrarse.</summary>
public sealed record ProgramaDto(Guid Id, string Nombre, string Codigo);

/// <summary>Una facultad con sus programas activos.</summary>
public sealed record FacultadDto(Guid Id, string Nombre, string Codigo, IReadOnlyList<ProgramaDto> Programas);

/// <summary>Un rol del catálogo, para que el cliente muestre las opciones del registro.</summary>
public sealed record RolDto(short Id, string Nombre, string Ambito);

/// <summary>
/// Una persona tal y como la ve un buscador, para elegir a quién reconocer.
///
/// **Lleva lo mínimo, y eso es la decisión importante de este tipo.** <see cref="PersonaDto"/>
/// incluye el correo institucional, el estado de la cuenta y qué tiene verificado; devolver eso en
/// un buscador abierto a cualquier usuario autenticado convertiría una caja de texto en un volcado
/// del directorio de la universidad: escribiendo «a» se obtendría el correo de media institución.
/// Para elegir destinatario basta el nombre, así que solo va el nombre.
/// </summary>
public sealed record PersonaBuscadaDto(Guid Id, string NombreCompleto);
