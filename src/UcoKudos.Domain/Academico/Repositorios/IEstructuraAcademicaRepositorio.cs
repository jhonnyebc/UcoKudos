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

    /// <summary>Los vínculos activos de una persona, de cualquier tipo.</summary>
    Task<IReadOnlyList<VinculoAcademico>> VinculosDeAsync(Guid personaId, CancellationToken ct);

    /// <summary>Un vínculo concreto, activo o no: si existe desactivado, se reactiva en lugar de duplicarse.</summary>
    Task<VinculoAcademico?> VinculoAsync(Guid personaId, Guid programaId, TipoVinculo tipo, CancellationToken ct);

    Task<IReadOnlyList<Programa>> ProgramasPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>¿Hay otra facultad con ese código? El código es único; la base lo garantiza y esto da el mensaje.</summary>
    Task<bool> ExisteCodigoDeFacultadAsync(string codigo, Guid? exceptoId, CancellationToken ct);

    Task<bool> ExisteCodigoDeProgramaAsync(string codigo, Guid? exceptoId, CancellationToken ct);
}
