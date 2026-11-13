namespace UcoKudos.Application.Academico.Entrada.Dto;

/// <summary>
/// Un programa en el que una persona está, y por qué.
/// </summary>
/// <param name="Relacion">
/// «Principal» si es el programa de su rol (el que acompaña un Coordinador de Programa, o aquel donde
/// está un docente o un estudiante al aprobarse su solicitud); «Matrícula» o «Docencia» si es un
/// vínculo adicional (RF-05).
/// </param>
public sealed record ProgramaDePersonaDto(
    Guid ProgramaId,
    string ProgramaNombre,
    string ProgramaCodigo,
    Guid FacultadId,
    string FacultadNombre,
    string Relacion);

/// <summary>
/// Lo que el enunciado pide poder conocer de cada persona (Gestión Académica): qué programas estudia
/// un estudiante, en cuáles enseña un docente, qué programa acompaña un Coordinador de Programa y qué
/// facultad acompaña un Coordinador de Facultad.
/// </summary>
/// <param name="FacultadAcompanada">Solo para un Coordinador de Facultad: la facultad que acompaña.</param>
public sealed record ProgramasDePersonaDto(
    Guid PersonaId,
    string NombreCompleto,
    string Rol,
    string? FacultadAcompanada,
    IReadOnlyList<ProgramaDePersonaDto> Programas);

/// <summary>Vincular a una persona con un programa. El tipo es «Matricula» o «Docencia».</summary>
public sealed record AsignarVinculoDto(Guid ProgramaId, string Tipo);

/// <summary>Un programa tal como lo ve quien administra la estructura, activo o no.</summary>
public sealed record ProgramaAdminDto(Guid Id, string Nombre, string Codigo, bool Activo);

/// <summary>Una facultad con todos sus programas, también los desactivados.</summary>
public sealed record FacultadAdminDto(
    Guid Id, string Nombre, string Codigo, bool Activa, IReadOnlyList<ProgramaAdminDto> Programas);

public sealed record DatosFacultadDto(string Nombre, string Codigo);

public sealed record DatosProgramaDto(string Nombre, string Codigo);
