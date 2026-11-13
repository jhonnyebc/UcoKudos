using UcoKudos.Application.Comun;
using UcoKudos.Application.Identidad.Entrada.Dto;

namespace UcoKudos.Application.Identidad.Entrada.Puertos;

/// <summary>Puerto de entrada del módulo de identidad (RF-01 a RF-05).</summary>
public interface IIdentidadPuertoEntrada
{
    /// <summary>Registro de una cuenta nueva. Queda pendiente de aprobación (RF-01, RF-02).</summary>
    Task<PersonaDto> RegistrarAsync(RegistrarCuentaDto datos, string correlacionId, CancellationToken ct);

    /// <summary>Comprueba un código de verificación del correo o del celular (RF-02). Sin sesión.</summary>
    Task<EstadoRegistroDto> VerificarContactoAsync(VerificarContactoDto datos, string correlacionId, CancellationToken ct);

    /// <summary>Emite códigos nuevos para una cuenta pendiente. No revela si la cuenta existe.</summary>
    Task ReenviarCodigosAsync(string correoInstitucional, string correlacionId, CancellationToken ct);

    /// <summary>El perfil del usuario autenticado.</summary>
    Task<PersonaDto?> MiPerfilAsync(Guid personaId, CancellationToken ct);

    /// <summary>Las solicitudes que este usuario tiene que resolver, limitadas a su ámbito (RF-03).</summary>
    Task<IReadOnlyList<PersonaDto>> PendientesAsync(ContextoPeticion contexto, CancellationToken ct);

    Task AprobarAsync(ContextoPeticion contexto, Guid personaId, CancellationToken ct);

    Task RechazarAsync(ContextoPeticion contexto, Guid personaId, string motivo, CancellationToken ct);

    /// <summary>Facultades con sus programas, para elegir al registrarse (RF-05).</summary>
    /// <summary>
    /// Personas activas cuyo nombre contiene el texto, para elegir destinatario (apoya RF-07).
    /// Excluye a quien busca: el dominio prohíbe reconocerse a uno mismo.
    /// </summary>
    Task<IReadOnlyList<PersonaBuscadaDto>> BuscarPersonasAsync(
        ContextoPeticion contexto, string? texto, int? tamano, CancellationToken ct);

    Task<IReadOnlyList<FacultadDto>> EstructuraAsync(CancellationToken ct);

    /// <summary>El catálogo de los siete roles del enunciado.</summary>
    IReadOnlyList<RolDto> Roles();
}
