using UcoKudos.Application.Academico.Entrada.Dto;
using UcoKudos.Application.Comun;

namespace UcoKudos.Application.Academico.Entrada.Puertos;

/// <summary>
/// Puerto de entrada de la gestión académica (RF-05): los programas de cada persona y la
/// administración de facultades y programas.
/// </summary>
public interface IAcademicoPuertoEntrada
{
    /// <summary>Los programas de la persona. Ella misma, o quien manda sobre ella dentro de su ámbito.</summary>
    Task<ProgramasDePersonaDto> ProgramasDeAsync(ContextoPeticion contexto, Guid personaId, CancellationToken ct);

    Task AsignarVinculoAsync(ContextoPeticion contexto, Guid personaId, AsignarVinculoDto datos, CancellationToken ct);

    Task QuitarVinculoAsync(ContextoPeticion contexto, Guid personaId, Guid programaId, string tipo, CancellationToken ct);

    /// <summary>La estructura completa, con lo desactivado. Solo quien la administra.</summary>
    Task<IReadOnlyList<FacultadAdminDto>> EstructuraCompletaAsync(ContextoPeticion contexto, CancellationToken ct);

    Task<FacultadAdminDto> CrearFacultadAsync(ContextoPeticion contexto, DatosFacultadDto datos, CancellationToken ct);

    Task ActualizarFacultadAsync(ContextoPeticion contexto, Guid facultadId, DatosFacultadDto datos, CancellationToken ct);

    Task CambiarEstadoFacultadAsync(ContextoPeticion contexto, Guid facultadId, bool activa, CancellationToken ct);

    Task<ProgramaAdminDto> CrearProgramaAsync(ContextoPeticion contexto, Guid facultadId, DatosProgramaDto datos, CancellationToken ct);

    Task ActualizarProgramaAsync(ContextoPeticion contexto, Guid programaId, DatosProgramaDto datos, CancellationToken ct);

    Task CambiarEstadoProgramaAsync(ContextoPeticion contexto, Guid programaId, bool activo, CancellationToken ct);
}
