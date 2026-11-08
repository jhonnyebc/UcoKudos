namespace UcoKudos.Domain.Academico.Repositorios;

/// <summary>
/// Puerto de la estructura académica. Importa porque es el **ámbito** contra el que se comprueba la
/// autorización: saber en qué programas está una persona es lo que permite decidir si su coordinador
/// manda sobre ella (ADR-0004).
/// </summary>
public interface IEstructuraAcademicaRepositorio
{
    Task<Facultad?> FacultadPorIdAsync(Guid id, CancellationToken ct);

    Task<Programa?> ProgramaPorIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<Facultad>> FacultadesAsync(CancellationToken ct);

    Task<IReadOnlyList<Programa>> ProgramasDeFacultadAsync(Guid facultadId, CancellationToken ct);

    void AgregarFacultad(Facultad facultad);

    void AgregarPrograma(Programa programa);

    /// <summary>Programas en los que la persona estudia o enseña (RF-05).</summary>
    Task<IReadOnlyList<Guid>> ProgramasDeAsync(Guid personaId, TipoVinculo tipo, CancellationToken ct);

    void AgregarVinculo(VinculoAcademico vinculo);
}
