using UcoKudos.Application.Comun;
using UcoKudos.Application.Identidad.Entrada.Dto;

namespace UcoKudos.Application.Identidad.Entrada.Puertos;

/// <summary>Puerto de entrada del módulo de identidad (RF-01 a RF-05).</summary>
public interface IIdentidadPuertoEntrada
{
    /// <summary>Registro de una cuenta nueva. Queda pendiente de aprobación (RF-01, RF-02).</summary>
    Task<PersonaDto> RegistrarAsync(RegistrarCuentaDto datos, string correlacionId, CancellationToken ct);

    /// <summary>El perfil del usuario autenticado.</summary>
    Task<PersonaDto?> MiPerfilAsync(Guid personaId, CancellationToken ct);

    /// <summary>Las solicitudes que este usuario tiene que resolver, limitadas a su ámbito (RF-03).</summary>
    Task<IReadOnlyList<PersonaDto>> PendientesAsync(ContextoPeticion contexto, CancellationToken ct);

    Task AprobarAsync(ContextoPeticion contexto, Guid personaId, CancellationToken ct);

    Task RechazarAsync(ContextoPeticion contexto, Guid personaId, string motivo, CancellationToken ct);

    /// <summary>Facultades con sus programas, para elegir al registrarse (RF-05).</summary>
    Task<IReadOnlyList<FacultadDto>> EstructuraAsync(CancellationToken ct);

    /// <summary>El catálogo de los siete roles del enunciado.</summary>
    IReadOnlyList<RolDto> Roles();
}
