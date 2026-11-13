using UcoKudos.Application.Academico.Entrada.Dto;
using UcoKudos.Application.Comun;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Domain.Academico;
using UcoKudos.Domain.Academico.Repositorios;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Identidad.Repositorios;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.Application.Academico.CasosUso;

/// <summary>
/// Los programas de una persona (RF-05): lo que el enunciado pide poder conocer de cada rol en
/// Gestión Académica.
///
/// Reúne **dos fuentes**: el programa de su rol —el que acompaña un Coordinador de Programa, o aquel
/// donde está un docente o un estudiante— y los vínculos adicionales, porque una persona puede
/// estudiar o enseñar en varios programas.
///
/// Ver los programas de otra persona exige mandar sobre ella dentro del ámbito: un Coordinador de
/// Programa ve a los de sus programas y no a los de otros. Cada uno ve los suyos.
/// </summary>
public sealed class ConsultarProgramasDePersonaCasoUso(
    IPersonaRepositorio personas,
    IEstructuraAcademicaRepositorio academico,
    IUnidadTrabajo unidadTrabajo,
    ServicioAuditoria auditoria,
    TimeProvider reloj)
{
    public async Task<ProgramasDePersonaDto> EjecutarAsync(ContextoPeticion contexto, Guid personaId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var persona = await personas.PorIdAsync(personaId, ct)
                      ?? throw new ExcepcionDominio("La persona indicada no existe.");

        var asignacion = await personas.RolActivoAsync(personaId, ct);
        var rol = asignacion?.Rol ?? Rol.Estudiante;
        var vinculos = await academico.VinculosDeAsync(personaId, ct);

        var ids = new List<Guid>();
        if (asignacion?.ProgramaId is { } principal)
            ids.Add(principal);
        ids.AddRange(vinculos.Select(v => v.ProgramaId));

        var programas = (await academico.ProgramasPorIdsAsync([.. ids.Distinct()], ct)).ToDictionary(p => p.Id);

        await ExigirPuedeVerAsync(contexto, persona, rol, programas.Values, ct);

        var facultades = new Dictionary<Guid, string>();
        async Task<string> NombreDeFacultadAsync(Guid facultadId)
        {
            if (!facultades.TryGetValue(facultadId, out var nombre))
            {
                nombre = (await academico.FacultadPorIdAsync(facultadId, ct))?.Nombre ?? "Facultad desconocida";
                facultades[facultadId] = nombre;
            }

            return nombre;
        }

        var resultado = new List<ProgramaDePersonaDto>();

        if (asignacion?.ProgramaId is { } programaPrincipal && programas.TryGetValue(programaPrincipal, out var p))
        {
            resultado.Add(new ProgramaDePersonaDto(
                p.Id, p.Nombre, p.Codigo, p.FacultadId, await NombreDeFacultadAsync(p.FacultadId), "Principal"));
        }

        foreach (var vinculo in vinculos)
        {
            if (!programas.TryGetValue(vinculo.ProgramaId, out var programa))
                continue;

            // El vínculo que repite lo que ya dice el rol no se enseña dos veces.
            var repiteElRol = programa.Id == asignacion?.ProgramaId
                              && ((rol == Rol.Estudiante && vinculo.Tipo == TipoVinculo.Matricula)
                                  || (rol == Rol.Docente && vinculo.Tipo == TipoVinculo.Docencia));
            if (repiteElRol)
                continue;

            resultado.Add(new ProgramaDePersonaDto(
                programa.Id, programa.Nombre, programa.Codigo, programa.FacultadId,
                await NombreDeFacultadAsync(programa.FacultadId),
                vinculo.Tipo == TipoVinculo.Matricula ? "Matrícula" : "Docencia"));
        }

        string? facultadAcompanada = null;
        if (rol == Rol.CoordinadorFacultad && asignacion?.FacultadId is { } facultadId)
            facultadAcompanada = await NombreDeFacultadAsync(facultadId);

        return new ProgramasDePersonaDto(persona.Id, persona.NombreCompleto, rol.Nombre, facultadAcompanada, resultado);
    }

    private async Task ExigirPuedeVerAsync(
        ContextoPeticion contexto, Persona persona, Rol rol, IEnumerable<Programa> programas, CancellationToken ct)
    {
        var alcanzables = programas.Select(p => (p.Id, p.FacultadId));

        if (JerarquiaInstitucional.PuedeVerProgramasDe(contexto.Ambito, persona.Id, rol, alcanzables))
            return;

        await auditoria.RegistrarAccesoRechazadoAsync(contexto, "ver los programas de una persona", persona.Id, reloj.GetUtcNow(), ct);
        await unidadTrabajo.ConfirmarAsync(ct);

        throw new ExcepcionAutorizacion("El rol " + contexto.Rol.Nombre + " no puede ver los programas de esa persona.");
    }
}

/// <summary>
/// Vincular a una persona con un programa, o quitarle el vínculo (RF-05): es la forma de que un
/// estudiante curse varios programas y un docente enseñe en varios.
///
/// Los Coordinadores administran a los estudiantes y docentes **de sus programas**: quien vincula
/// tiene que mandar sobre la persona y alcanzar el programa del vínculo (un Coordinador de Programa
/// solo el suyo; uno de Facultad, los de su facultad; los de ámbito universidad, cualquiera).
/// </summary>
public sealed class GestionarVinculosCasoUso(
    IPersonaRepositorio personas,
    IEstructuraAcademicaRepositorio academico,
    IUnidadTrabajo unidadTrabajo,
    ServicioAuditoria auditoria,
    TimeProvider reloj)
{
    public async Task AsignarAsync(ContextoPeticion contexto, Guid personaId, AsignarVinculoDto datos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(datos);

        var tipo = ParsearTipo(datos.Tipo);
        var (persona, rol) = await CargarPersonaAsync(personaId, ct);

        // Un estudiante se matricula y un docente enseña: mezclarlos no tiene sentido académico.
        if (tipo == TipoVinculo.Matricula && rol != Rol.Estudiante)
            throw new ExcepcionDominio("Solo un estudiante se puede matricular en un programa.");
        if (tipo == TipoVinculo.Docencia && rol != Rol.Docente)
            throw new ExcepcionDominio("Solo un docente puede enseñar en un programa.");

        var programa = await academico.ProgramaPorIdAsync(datos.ProgramaId, ct)
                       ?? throw new ExcepcionDominio("El programa indicado no existe.");

        await ExigirPermisoAsync(contexto, persona, rol, programa, "vincular a una persona con un programa", ct);

        if (!programa.Activo)
            throw new ExcepcionDominio("El programa " + programa.Nombre + " está desactivado y no admite vínculos nuevos.");

        var existente = await academico.VinculoAsync(personaId, programa.Id, tipo, ct);
        if (existente is { Activo: true })
            throw new ExcepcionDominio("Esa persona ya está vinculada a ese programa.");

        if (existente is not null)
            existente.Activar();
        else
            academico.AgregarVinculo(new VinculoAcademico(personaId, programa.Id, tipo));

        await auditoria.RegistrarAsync(
            contexto, AccionesAuditoria.VinculoAsignar, nameof(Persona), personaId,
            new { programaId = programa.Id, tipo = tipo.ToString() }, reloj.GetUtcNow(), ct);

        await unidadTrabajo.ConfirmarAsync(ct);
    }

    public async Task QuitarAsync(
        ContextoPeticion contexto, Guid personaId, Guid programaId, string tipoTexto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var tipo = ParsearTipo(tipoTexto);
        var (persona, rol) = await CargarPersonaAsync(personaId, ct);

        var programa = await academico.ProgramaPorIdAsync(programaId, ct)
                       ?? throw new ExcepcionDominio("El programa indicado no existe.");

        await ExigirPermisoAsync(contexto, persona, rol, programa, "quitar un vínculo con un programa", ct);

        var vinculo = await academico.VinculoAsync(personaId, programaId, tipo, ct);
        if (vinculo is not { Activo: true })
            throw new ExcepcionDominio("Esa persona no tiene ese vínculo.");

        vinculo.Desactivar();

        await auditoria.RegistrarAsync(
            contexto, AccionesAuditoria.VinculoQuitar, nameof(Persona), personaId,
            new { programaId, tipo = tipo.ToString() }, reloj.GetUtcNow(), ct);

        await unidadTrabajo.ConfirmarAsync(ct);
    }

    private static TipoVinculo ParsearTipo(string texto) =>
        (texto ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "MATRICULA" or "MATRÍCULA" => TipoVinculo.Matricula,
            "DOCENCIA" => TipoVinculo.Docencia,
            _ => throw new ExcepcionDominio("El tipo de vínculo debe ser «Matricula» o «Docencia».")
        };

    private async Task<(Persona Persona, Rol Rol)> CargarPersonaAsync(Guid personaId, CancellationToken ct)
    {
        var persona = await personas.PorIdAsync(personaId, ct)
                      ?? throw new ExcepcionDominio("La persona indicada no existe.");
        var asignacion = await personas.RolActivoAsync(personaId, ct);

        return (persona, asignacion?.Rol ?? Rol.Estudiante);
    }

    private async Task ExigirPermisoAsync(
        ContextoPeticion contexto, Persona persona, Rol rol, Programa programa, string queIntento, CancellationToken ct)
    {
        if (JerarquiaInstitucional.PuedeGestionarVinculos(contexto.Ambito, persona.Id, rol, programa.Id, programa.FacultadId))
            return;

        await auditoria.RegistrarAccesoRechazadoAsync(contexto, queIntento, persona.Id, reloj.GetUtcNow(), ct);
        await unidadTrabajo.ConfirmarAsync(ct);

        throw new ExcepcionAutorizacion(
            "El rol " + contexto.Rol.Nombre + " no puede " + queIntento + " en ese ámbito.");
    }
}

/// <summary>
/// Administrar facultades y programas: crearlos, editarlos y activarlos o desactivarlos.
///
/// **Quién puede**: el Administrador, el Rector y el Coordinador Académico, los mismos que
/// administran las categorías. El enunciado no lo fija; es una decisión (ver ADR-0004) por ser la
/// estructura de toda la universidad.
///
/// **Se desactiva, no se borra**, como las categorías: los Kudos, las cuentas y los roles ya
/// existentes conservan su programa. Desactivar un programa o una facultad solo cierra **lo nuevo**:
/// deja de ofrecerse al registrarse y no admite vínculos nuevos, y sus personas siguen donde están.
/// </summary>
public sealed class AdministrarEstructuraCasoUso(
    IEstructuraAcademicaRepositorio academico,
    IUnidadTrabajo unidadTrabajo,
    ICacheConsultas cache,
    ServicioAuditoria auditoria,
    TimeProvider reloj)
{
    public async Task<IReadOnlyList<FacultadAdminDto>> CompletaAsync(ContextoPeticion contexto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        await ExigirPermisoAsync(contexto, "ver la estructura completa", Guid.Empty, ct);

        var resultado = new List<FacultadAdminDto>();
        foreach (var facultad in await academico.FacultadesAsync(ct))
        {
            var programas = await academico.ProgramasDeFacultadAsync(facultad.Id, ct);
            resultado.Add(Convertir(facultad, programas));
        }

        return resultado;
    }

    public async Task<FacultadAdminDto> CrearFacultadAsync(ContextoPeticion contexto, DatosFacultadDto datos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(datos);
        await ExigirPermisoAsync(contexto, "crear una facultad", Guid.Empty, ct);

        var facultad = new Facultad(datos.Nombre, datos.Codigo, reloj.GetUtcNow());
        await ExigirCodigoLibreDeFacultadAsync(facultad.Codigo, null, ct);

        academico.AgregarFacultad(facultad);
        await TerminarAsync(contexto, AccionesAuditoria.EstructuraCrear, nameof(Facultad), facultad.Id, new { facultad.Nombre, facultad.Codigo }, ct);

        return Convertir(facultad, []);
    }

    public async Task ActualizarFacultadAsync(ContextoPeticion contexto, Guid facultadId, DatosFacultadDto datos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(datos);
        await ExigirPermisoAsync(contexto, "editar una facultad", facultadId, ct);

        var facultad = await academico.FacultadPorIdAsync(facultadId, ct)
                       ?? throw new ExcepcionDominio("La facultad indicada no existe.");

        facultad.Actualizar(datos.Nombre, datos.Codigo);
        await ExigirCodigoLibreDeFacultadAsync(facultad.Codigo, facultad.Id, ct);

        await TerminarAsync(contexto, AccionesAuditoria.EstructuraActualizar, nameof(Facultad), facultad.Id, new { facultad.Nombre, facultad.Codigo }, ct);
    }

    public async Task CambiarEstadoFacultadAsync(ContextoPeticion contexto, Guid facultadId, bool activa, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        await ExigirPermisoAsync(contexto, "cambiar el estado de una facultad", facultadId, ct);

        var facultad = await academico.FacultadPorIdAsync(facultadId, ct)
                       ?? throw new ExcepcionDominio("La facultad indicada no existe.");

        if (activa)
            facultad.Activar();
        else
            facultad.Desactivar();

        await TerminarAsync(contexto, AccionesAuditoria.EstructuraCambiarEstado, nameof(Facultad), facultad.Id, new { activa }, ct);
    }

    public async Task<ProgramaAdminDto> CrearProgramaAsync(
        ContextoPeticion contexto, Guid facultadId, DatosProgramaDto datos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(datos);
        await ExigirPermisoAsync(contexto, "crear un programa", facultadId, ct);

        _ = await academico.FacultadPorIdAsync(facultadId, ct)
            ?? throw new ExcepcionDominio("La facultad indicada no existe.");

        var programa = new Programa(facultadId, datos.Nombre, datos.Codigo, reloj.GetUtcNow());
        await ExigirCodigoLibreDeProgramaAsync(programa.Codigo, null, ct);

        academico.AgregarPrograma(programa);
        await TerminarAsync(contexto, AccionesAuditoria.EstructuraCrear, nameof(Programa), programa.Id, new { programa.Nombre, programa.Codigo, facultadId }, ct);

        return Convertir(programa);
    }

    public async Task ActualizarProgramaAsync(ContextoPeticion contexto, Guid programaId, DatosProgramaDto datos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(datos);
        await ExigirPermisoAsync(contexto, "editar un programa", programaId, ct);

        var programa = await academico.ProgramaPorIdAsync(programaId, ct)
                       ?? throw new ExcepcionDominio("El programa indicado no existe.");

        programa.Actualizar(datos.Nombre, datos.Codigo);
        await ExigirCodigoLibreDeProgramaAsync(programa.Codigo, programa.Id, ct);

        await TerminarAsync(contexto, AccionesAuditoria.EstructuraActualizar, nameof(Programa), programa.Id, new { programa.Nombre, programa.Codigo }, ct);
    }

    public async Task CambiarEstadoProgramaAsync(ContextoPeticion contexto, Guid programaId, bool activo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        await ExigirPermisoAsync(contexto, "cambiar el estado de un programa", programaId, ct);

        var programa = await academico.ProgramaPorIdAsync(programaId, ct)
                       ?? throw new ExcepcionDominio("El programa indicado no existe.");

        if (activo)
            programa.Activar();
        else
            programa.Desactivar();

        await TerminarAsync(contexto, AccionesAuditoria.EstructuraCambiarEstado, nameof(Programa), programa.Id, new { activo }, ct);
    }

    private async Task ExigirCodigoLibreDeFacultadAsync(string codigo, Guid? excepto, CancellationToken ct)
    {
        if (await academico.ExisteCodigoDeFacultadAsync(codigo, excepto, ct))
            throw new ExcepcionDominio("Ya existe una facultad con el código " + codigo + ".");
    }

    private async Task ExigirCodigoLibreDeProgramaAsync(string codigo, Guid? excepto, CancellationToken ct)
    {
        if (await academico.ExisteCodigoDeProgramaAsync(codigo, excepto, ct))
            throw new ExcepcionDominio("Ya existe un programa con el código " + codigo + ".");
    }

    /// <summary>
    /// Cierra una escritura: auditoría, confirmación y caché. La caché de la estructura se invalida
    /// porque el desplegable del registro sale de ella, y tiene que reflejar el cambio de inmediato.
    /// </summary>
    private async Task TerminarAsync(
        ContextoPeticion contexto, string accion, string tipoEntidad, Guid entidadId, object datos, CancellationToken ct)
    {
        await auditoria.RegistrarAsync(contexto, accion, tipoEntidad, entidadId, datos, reloj.GetUtcNow(), ct);
        await unidadTrabajo.ConfirmarAsync(ct);
        await cache.InvalidarGrupoAsync(GruposCache.EstructuraAcademica, ct);
    }

    private async Task ExigirPermisoAsync(ContextoPeticion contexto, string queIntento, Guid entidadId, CancellationToken ct)
    {
        if (JerarquiaInstitucional.PuedeAdministrarEstructura(contexto.Ambito))
            return;

        await auditoria.RegistrarAccesoRechazadoAsync(contexto, queIntento, entidadId, reloj.GetUtcNow(), ct);
        await unidadTrabajo.ConfirmarAsync(ct);

        throw new ExcepcionAutorizacion("El rol " + contexto.Rol.Nombre + " no administra la estructura académica.");
    }

    private static FacultadAdminDto Convertir(Facultad facultad, IReadOnlyList<Programa> programas) =>
        new(facultad.Id, facultad.Nombre, facultad.Codigo, facultad.Activa, [.. programas.Select(Convertir)]);

    private static ProgramaAdminDto Convertir(Programa programa) =>
        new(programa.Id, programa.Nombre, programa.Codigo, programa.Activo);
}
