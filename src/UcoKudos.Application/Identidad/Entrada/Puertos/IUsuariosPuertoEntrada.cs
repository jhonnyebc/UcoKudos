using UcoKudos.Application.Comun;
using UcoKudos.Application.Identidad.Entrada.Dto;

namespace UcoKudos.Application.Identidad.Entrada.Puertos;

/// <summary>Puerto de entrada de la administración de usuarios: desactivar, reactivar y cambiar el rol.</summary>
public interface IUsuariosPuertoEntrada
{
    /// <summary>Las personas que quien consulta administra, según la cadena del enunciado.</summary>
    Task<IReadOnlyList<UsuarioAdministradoDto>> AdministradosAsync(ContextoPeticion contexto, string? busqueda, CancellationToken ct);

    Task DesactivarAsync(ContextoPeticion contexto, Guid personaId, string motivo, CancellationToken ct);

    Task ReactivarAsync(ContextoPeticion contexto, Guid personaId, CancellationToken ct);

    Task CambiarRolAsync(ContextoPeticion contexto, Guid personaId, CambiarRolDto datos, CancellationToken ct);
}
