using UcoKudos.Application.Comun;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Application.Identidad.Entrada.Dto;
using UcoKudos.Application.Identidad.Entrada.Puertos;
using UcoKudos.Domain.Academico.Repositorios;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Identidad.Repositorios;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.Application.Identidad.CasosUso;

/// <summary>
/// Administración de usuarios: desactivar y reactivar una cuenta, y cambiar su rol.
///
/// **Quién administra a quién es la cadena del enunciado, literal**, la misma que aprueba las
/// solicitudes: un Coordinador de Programa administra a los estudiantes y docentes de **sus**
/// programas; uno de Facultad, a los coordinadores de programa de su facultad; el Coordinador
/// Académico, a los de facultad; el Rector, a los coordinadores académicos; y el Administrador, a los
/// administradores y a los rectores. Nadie se administra a sí mismo: evita que una cuenta se deje a
/// sí misma sin acceso, y que el último administrador se bloquee.
///
/// Desactivar **no borra ni cambia nada más**: la cuenta pierde el acceso en la siguiente petición
/// —el estado se comprueba en cada una, sin caché— y conserva su rol, sus Kudos y su historia, de modo
/// que reactivarla la deja exactamente como estaba.
///
/// Cambiar el rol revoca el actual y crea el nuevo, así que queda el rastro de lo que fue. Quien lo
/// cambia tiene que poder administrar a la persona en su rol **actual** y ser quien aprobaría el rol
/// **nuevo** en el ámbito nuevo: no se puede ascender a alguien más allá de lo que uno mismo aprueba.
/// </summary>
public sealed class AdministrarUsuariosCasoUso(
    IPersonaRepositorio personas,
    IEstructuraAcademicaRepositorio academico,
    IUnidadTrabajo unidadTrabajo,
    ServicioAuditoria auditoria,
    TimeProvider reloj) : IUsuariosPuertoEntrada
{
    public const int TamanoMaximoDeLista = 200;

    public async Task<IReadOnlyList<UsuarioAdministradoDto>> AdministradosAsync(
        ContextoPeticion contexto, string? busqueda, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var roles = JerarquiaInstitucional.RolesQueAdministra(contexto.Rol);
        if (roles.Count == 0)
            return [];

        var filas = await personas.AdministradasAsync(
            [.. roles.Select(r => r.Id)],
            contexto.Rol.Ambito == AmbitoRol.Facultad ? contexto.Ambito.FacultadId : null,
            contexto.Rol.Ambito == AmbitoRol.Programa ? contexto.Ambito.ProgramaId : null,
            string.IsNullOrWhiteSpace(busqueda) ? null : busqueda.Trim(),
            TamanoMaximoDeLista,
            ct);

        // Quien no tiene a quién administrar recibe una lista vacía, no un error: «no hay» es una
        // respuesta. La autorización de verdad está en cada acción.
        return
        [
            .. filas
                .Where(f => f.Persona.Id != contexto.PersonaId)
                .Select(f => new UsuarioAdministradoDto(
                    f.Persona.Id, f.Persona.NombreCompleto, f.Persona.CorreoInstitucional,
                    f.Rol.Rol.Nombre, f.Persona.Estado.ToString()))
        ];
    }

    public async Task DesactivarAsync(ContextoPeticion contexto, Guid personaId, string motivo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var texto = ExigirMotivo(motivo);
        var (persona, asignacion) = await CargarAsync(personaId, ct);

        await ExigirPuedeAdministrarAsync(contexto, persona, asignacion, "desactivar una cuenta", ct);

        if (persona.Estado != EstadoCuenta.Activa)
            throw new ExcepcionDominio("Solo se puede desactivar una cuenta activa.");

        persona.Desactivar();

        await auditoria.RegistrarAsync(
            contexto, AccionesAuditoria.UsuarioDesactivar, nameof(Persona), persona.Id,
            new { rol = asignacion.Rol.Nombre, motivo = texto }, reloj.GetUtcNow(), ct);

        await unidadTrabajo.ConfirmarAsync(ct);
    }

    public async Task ReactivarAsync(ContextoPeticion contexto, Guid personaId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var (persona, asignacion) = await CargarAsync(personaId, ct);

        await ExigirPuedeAdministrarAsync(contexto, persona, asignacion, "reactivar una cuenta", ct);

        // El dominio exige que esté inactiva y recalcula el estado: la reactivación no puede saltarse
        // las condiciones de RF-02.
        persona.Reactivar();

        await auditoria.RegistrarAsync(
            contexto, AccionesAuditoria.UsuarioReactivar, nameof(Persona), persona.Id,
            new { rol = asignacion.Rol.Nombre }, reloj.GetUtcNow(), ct);

        await unidadTrabajo.ConfirmarAsync(ct);
    }

    public async Task CambiarRolAsync(ContextoPeticion contexto, Guid personaId, CambiarRolDto datos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(datos);

        var texto = ExigirMotivo(datos.Motivo);
        var ahora = reloj.GetUtcNow();
        var (persona, actual) = await CargarAsync(personaId, ct);

        // 1. Poder administrarla en su rol actual.
        await ExigirPuedeAdministrarAsync(contexto, persona, actual, "cambiar el rol de una persona", ct);

        var nuevoRol = Rol.Por(datos.RolId);
        var (facultadNueva, programaNuevo) = await ResolverAmbitoAsync(nuevoRol, datos.FacultadId, datos.ProgramaId, ct);

        if (nuevoRol == actual.Rol && facultadNueva == actual.FacultadId && programaNuevo == actual.ProgramaId)
            throw new ExcepcionDominio("Esa persona ya tiene ese rol en ese ámbito.");

        // 2. Ser quien aprobaría el rol nuevo en el ámbito nuevo: no se asciende más allá de lo que
        //    uno mismo aprueba.
        if (!JerarquiaInstitucional.PuedeAprobar(contexto.Ambito, nuevoRol, facultadNueva, programaNuevo))
        {
            await auditoria.RegistrarAccesoRechazadoAsync(contexto, "asignar el rol " + nuevoRol.Nombre, persona.Id, ahora, ct);
            await unidadTrabajo.ConfirmarAsync(ct);

            throw new ExcepcionAutorizacion(
                "El rol " + contexto.Rol.Nombre + " no asigna el rol " + nuevoRol.Nombre + " en ese ámbito.");
        }

        // La unicidad de RF-04: la base la garantiza con índices; esto da el mensaje claro.
        if (await personas.ExisteRolActivoAsync(nuevoRol.Id, facultadNueva, programaNuevo, ct))
            throw new ExcepcionDominio("Ya hay alguien ejerciendo el rol " + nuevoRol.Nombre + " en ese ámbito.");

        var asignacion = new AsignacionRol(persona.Id, nuevoRol, facultadNueva, programaNuevo, ahora);

        actual.Revocar(ahora);
        personas.AgregarRol(asignacion);

        await auditoria.RegistrarAsync(
            contexto, AccionesAuditoria.UsuarioCambiarRol, nameof(Persona), persona.Id,
            new { de = actual.Rol.Nombre, a = nuevoRol.Nombre, motivo = texto }, ahora, ct);

        await unidadTrabajo.ConfirmarAsync(ct);
    }

    private static string ExigirMotivo(string? motivo)
    {
        var texto = (motivo ?? string.Empty).Trim();
        return texto.Length == 0 ? throw new ExcepcionDominio("El motivo es obligatorio.") : texto;
    }

    private async Task<(Persona Persona, AsignacionRol Asignacion)> CargarAsync(Guid personaId, CancellationToken ct)
    {
        var persona = await personas.PorIdAsync(personaId, ct)
                      ?? throw new ExcepcionDominio("La persona indicada no existe.");

        var asignacion = await personas.RolActivoAsync(personaId, ct);
        if (asignacion is not { Activa: true })
            throw new ExcepcionDominio("Esta persona no tiene un rol activo: su solicitud todavía se resuelve en «Cuentas».");

        return (persona, asignacion);
    }

    private async Task ExigirPuedeAdministrarAsync(
        ContextoPeticion contexto, Persona persona, AsignacionRol asignacion, string queIntento, CancellationToken ct)
    {
        if (JerarquiaInstitucional.PuedeAdministrar(
                contexto.Ambito, persona.Id, asignacion.Rol, asignacion.FacultadId, asignacion.ProgramaId))
            return;

        await auditoria.RegistrarAccesoRechazadoAsync(contexto, queIntento, persona.Id, reloj.GetUtcNow(), ct);
        await unidadTrabajo.ConfirmarAsync(ct);

        throw new ExcepcionAutorizacion(
            "El rol " + contexto.Rol.Nombre + " no puede " + queIntento + " de esa persona.");
    }

    /// <summary>
    /// El ámbito del rol nuevo: tiene que existir y estar activo, y el de programa toma la facultad
    /// **del programa**, no la que diga quien llama, porque de ella depende quién aprueba.
    /// </summary>
    private async Task<(Guid? FacultadId, Guid? ProgramaId)> ResolverAmbitoAsync(
        Rol rol, Guid? facultadId, Guid? programaId, CancellationToken ct)
    {
        switch (rol.Ambito)
        {
            case AmbitoRol.Facultad:
                {
                    var facultad = facultadId is null ? null : await academico.FacultadPorIdAsync(facultadId.Value, ct);
                    if (facultad is null)
                        throw new ExcepcionDominio("La facultad indicada no existe.");
                    if (!facultad.Activa)
                        throw new ExcepcionDominio("La facultad " + facultad.Nombre + " está desactivada.");

                    return (facultad.Id, null);
                }

            case AmbitoRol.Programa:
                {
                    var programa = programaId is null ? null : await academico.ProgramaPorIdAsync(programaId.Value, ct);
                    if (programa is null)
                        throw new ExcepcionDominio("El programa académico indicado no existe.");
                    if (!programa.Activo)
                        throw new ExcepcionDominio("El programa " + programa.Nombre + " está desactivado.");

                    return (programa.FacultadId, programa.Id);
                }

            default:
                return (null, null);
        }
    }
}
