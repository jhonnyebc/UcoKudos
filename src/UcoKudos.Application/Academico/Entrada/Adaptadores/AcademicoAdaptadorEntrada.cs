using UcoKudos.Application.Academico.CasosUso;
using UcoKudos.Application.Academico.Entrada.Dto;
using UcoKudos.Application.Academico.Entrada.Puertos;
using UcoKudos.Application.Comun;

namespace UcoKudos.Application.Academico.Entrada.Adaptadores;

/// <summary>Adaptador de entrada de la gestión académica: compone los casos de uso tras el puerto.</summary>
public sealed class AcademicoAdaptadorEntrada(
    ConsultarProgramasDePersonaCasoUso consultar,
    GestionarVinculosCasoUso vinculos,
    AdministrarEstructuraCasoUso estructura) : IAcademicoPuertoEntrada
{
    public Task<ProgramasDePersonaDto> ProgramasDeAsync(ContextoPeticion contexto, Guid personaId, CancellationToken ct) =>
        consultar.EjecutarAsync(contexto, personaId, ct);

    public Task AsignarVinculoAsync(ContextoPeticion contexto, Guid personaId, AsignarVinculoDto datos, CancellationToken ct) =>
        vinculos.AsignarAsync(contexto, personaId, datos, ct);

    public Task QuitarVinculoAsync(ContextoPeticion contexto, Guid personaId, Guid programaId, string tipo, CancellationToken ct) =>
        vinculos.QuitarAsync(contexto, personaId, programaId, tipo, ct);

    public Task<IReadOnlyList<FacultadAdminDto>> EstructuraCompletaAsync(ContextoPeticion contexto, CancellationToken ct) =>
        estructura.CompletaAsync(contexto, ct);

    public Task<FacultadAdminDto> CrearFacultadAsync(ContextoPeticion contexto, DatosFacultadDto datos, CancellationToken ct) =>
        estructura.CrearFacultadAsync(contexto, datos, ct);

    public Task ActualizarFacultadAsync(ContextoPeticion contexto, Guid facultadId, DatosFacultadDto datos, CancellationToken ct) =>
        estructura.ActualizarFacultadAsync(contexto, facultadId, datos, ct);

    public Task CambiarEstadoFacultadAsync(ContextoPeticion contexto, Guid facultadId, bool activa, CancellationToken ct) =>
        estructura.CambiarEstadoFacultadAsync(contexto, facultadId, activa, ct);

    public Task<ProgramaAdminDto> CrearProgramaAsync(ContextoPeticion contexto, Guid facultadId, DatosProgramaDto datos, CancellationToken ct) =>
        estructura.CrearProgramaAsync(contexto, facultadId, datos, ct);

    public Task ActualizarProgramaAsync(ContextoPeticion contexto, Guid programaId, DatosProgramaDto datos, CancellationToken ct) =>
        estructura.ActualizarProgramaAsync(contexto, programaId, datos, ct);

    public Task CambiarEstadoProgramaAsync(ContextoPeticion contexto, Guid programaId, bool activo, CancellationToken ct) =>
        estructura.CambiarEstadoProgramaAsync(contexto, programaId, activo, ct);
}
