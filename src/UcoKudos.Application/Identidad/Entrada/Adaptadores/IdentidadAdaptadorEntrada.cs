using UcoKudos.Application.Comun;
using UcoKudos.Application.Identidad.CasosUso;
using UcoKudos.Application.Identidad.Entrada.Dto;
using UcoKudos.Application.Identidad.Entrada.Puertos;
using UcoKudos.Domain.Identidad;

namespace UcoKudos.Application.Identidad.Entrada.Adaptadores;

/// <summary>Adaptador de entrada del módulo de identidad: compone los casos de uso tras el puerto.</summary>
public sealed class IdentidadAdaptadorEntrada(
    RegistrarCuentaCasoUso registrar,
    ResolverSolicitudCasoUso resolver,
    ConsultarIdentidadCasoUso consultar) : IIdentidadPuertoEntrada
{
    public Task<PersonaDto> RegistrarAsync(RegistrarCuentaDto datos, string correlacionId, CancellationToken ct) =>
        registrar.EjecutarAsync(datos, correlacionId, ct);

    public Task<PersonaDto?> MiPerfilAsync(Guid personaId, CancellationToken ct) =>
        consultar.MiPerfilAsync(personaId, ct);

    public Task<IReadOnlyList<PersonaDto>> PendientesAsync(ContextoPeticion contexto, CancellationToken ct) =>
        consultar.PendientesAsync(contexto, ct);

    public Task AprobarAsync(ContextoPeticion contexto, Guid personaId, CancellationToken ct) =>
        resolver.AprobarAsync(contexto, personaId, ct);

    public Task RechazarAsync(ContextoPeticion contexto, Guid personaId, string motivo, CancellationToken ct) =>
        resolver.RechazarAsync(contexto, personaId, motivo, ct);

    public Task<IReadOnlyList<FacultadDto>> EstructuraAsync(CancellationToken ct) =>
        consultar.EstructuraAsync(ct);

    /// <summary>
    /// El catálogo de roles sale del dominio y no de la base: son los siete del enunciado y no
    /// cambian en ejecución (ADR-0004).
    /// </summary>
    public IReadOnlyList<RolDto> Roles() =>
        [.. Rol.Todos.Select(r => new RolDto(r.Id, r.Nombre, r.Ambito.ToString()))];
}
