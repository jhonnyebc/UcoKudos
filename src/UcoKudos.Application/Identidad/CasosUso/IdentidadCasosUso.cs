using System.Security.Cryptography;
using System.Text.Json;
using UcoKudos.Application.Comun;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Application.Identidad.Entrada.Dto;
using UcoKudos.Application.Notificaciones.CasosUso;
using UcoKudos.Domain.Academico.Repositorios;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Identidad.Repositorios;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.Application.Identidad.CasosUso;

/// <summary>
/// Registro de una cuenta (RF-01) y su activación (RF-02).
///
/// La cuenta nace **pendiente**: solo se activa con las tres condiciones a la vez, y esa regla vive
/// en la entidad para que no haya forma de activar una saltándose una.
/// </summary>
public sealed class RegistrarCuentaCasoUso(
    IPersonaRepositorio personas,
    IEstructuraAcademicaRepositorio academico,
    IUnidadTrabajo unidadTrabajo,
    IBandejaSalida bandeja,
    ServicioAuditoria auditoria,
    TimeProvider reloj)
{
    public async Task<PersonaDto> EjecutarAsync(RegistrarCuentaDto datos, string correlacionId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(datos);

        var ahora = reloj.GetUtcNow();
        var correo = Persona.NormalizarCorreo(datos.CorreoInstitucional);

        if (await personas.ExisteCorreoAsync(correo, ct))
            throw new ExcepcionDominio("Ya existe una cuenta con el correo " + correo + ".");

        var rol = Rol.Por(datos.RolSolicitado);
        await ExigirAmbitoExistenteAsync(rol, datos.FacultadId, datos.ProgramaId, ct);

        var persona = new Persona(datos.CorreoInstitucional, datos.Celular, datos.NombreCompleto, ahora);
        personas.Agregar(persona);

        // El rol se asigna ya, pero inactivo hasta que se apruebe: así el aprobador ve qué pidió.
        var asignacion = new AsignacionRol(persona.Id, rol, datos.FacultadId, datos.ProgramaId, ahora);
        asignacion.Revocar(ahora);
        personas.AgregarRol(asignacion);

        // El contexto de una persona que todavía no tiene cuenta: se audita con ella misma como actor.
        var contexto = new ContextoPeticion(new AmbitoUsuario(persona.Id, rol, datos.FacultadId, datos.ProgramaId), correlacionId);

        await auditoria.RegistrarAsync(
            contexto, AccionesAuditoria.SolicitudPresentar, nameof(Persona), persona.Id,
            new { rol = rol.Nombre, facultadId = datos.FacultadId, programaId = datos.ProgramaId }, ahora, ct);

        // Código de verificación del celular por WhatsApp (RF-02), reutilizando el mismo adaptador
        // de salida que los avisos de Kudos (ADR-0004).
        var codigo = GenerarCodigo();
        bandeja.Programar(new EventoPendiente(
            EventoId: Guid.CreateVersion7(ahora),
            TipoEvento: TiposEvento.VerificacionCelularSolicitada,
            Carga: JsonSerializer.Serialize(new CargaVerificacion(persona.Id, codigo)),
            OcurridoEn: ahora,
            ContextoTraza: correlacionId));

        await unidadTrabajo.ConfirmarAsync(ct);

        return Convertir(persona, rol);
    }

    private async Task ExigirAmbitoExistenteAsync(Rol rol, Guid? facultadId, Guid? programaId, CancellationToken ct)
    {
        if (rol.Ambito == AmbitoRol.Facultad && await academico.FacultadPorIdAsync(facultadId!.Value, ct) is null)
            throw new ExcepcionDominio("La facultad indicada no existe.");

        if (rol.Ambito == AmbitoRol.Programa)
        {
            var programa = programaId is null ? null : await academico.ProgramaPorIdAsync(programaId.Value, ct);
            if (programa is null)
                throw new ExcepcionDominio("El programa académico indicado no existe.");
        }
    }

    /// <summary>
    /// Código de un solo uso, generado con un generador criptográfico. Seis dígitos es lo habitual,
    /// y lo que lo hace seguro no es la longitud sino el límite de intentos y la expiración.
    /// </summary>
    private static string GenerarCodigo() =>
        RandomNumberGenerator.GetInt32(100000, 1000000).ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal static PersonaDto Convertir(Persona persona, Rol rol) =>
        new(persona.Id, persona.CorreoInstitucional, persona.NombreCompleto, rol.Nombre,
            persona.Estado.ToString(), persona.CorreoVerificado, persona.CelularVerificado);
}

/// <summary>
/// Aprobar o rechazar una solicitud de cuenta (RF-03).
///
/// Es el caso de uso donde se ve el servicio de la jerarquía trabajando: comprueba que el aprobador
/// sea el nivel que corresponde **y** que el solicitante esté dentro de su ámbito. Si no, el intento
/// queda auditado antes de rechazarlo (QA-SEG-01).
/// </summary>
public sealed class ResolverSolicitudCasoUso(
    IPersonaRepositorio personas,
    IUnidadTrabajo unidadTrabajo,
    IBandejaSalida bandeja,
    ServicioAuditoria auditoria,
    TimeProvider reloj)
{
    public async Task AprobarAsync(ContextoPeticion contexto, Guid personaId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var ahora = reloj.GetUtcNow();
        var (persona, asignacion) = await CargarAsync(personaId, ct);

        await ExigirPuedeAprobarAsync(contexto, asignacion, "aprobar una solicitud", personaId, ct);

        // Unicidad (RF-04): el dominio lo comprueba para dar un mensaje claro; la garantía real es el
        // índice único parcial en la base, porque esta comprobación no resiste dos peticiones a la vez.
        if (await personas.ExisteRolActivoAsync(asignacion.RolId, asignacion.FacultadId, asignacion.ProgramaId, ct))
        {
            throw new ExcepcionDominio(
                "Ya hay alguien ejerciendo el rol " + asignacion.Rol.Nombre + " en ese ámbito.");
        }

        persona.AprobarSolicitud();

        var activa = new AsignacionRol(persona.Id, asignacion.Rol, asignacion.FacultadId, asignacion.ProgramaId, ahora);
        personas.AgregarRol(activa);

        await auditoria.RegistrarAsync(
            contexto, AccionesAuditoria.SolicitudAprobar, nameof(Persona), persona.Id,
            new { rol = asignacion.Rol.Nombre }, ahora, ct);

        bandeja.Programar(new EventoPendiente(
            EventoId: Guid.CreateVersion7(ahora),
            TipoEvento: TiposEvento.SolicitudAprobada,
            Carga: JsonSerializer.Serialize(new CargaSolicitud(persona.Id, asignacion.Rol.Nombre, null)),
            OcurridoEn: ahora,
            ContextoTraza: contexto.CorrelacionId));

        await unidadTrabajo.ConfirmarAsync(ct);
    }

    public async Task RechazarAsync(ContextoPeticion contexto, Guid personaId, string motivo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var ahora = reloj.GetUtcNow();
        var (persona, asignacion) = await CargarAsync(personaId, ct);

        await ExigirPuedeAprobarAsync(contexto, asignacion, "rechazar una solicitud", personaId, ct);

        persona.RechazarSolicitud();

        await auditoria.RegistrarAsync(
            contexto, AccionesAuditoria.SolicitudRechazar, nameof(Persona), persona.Id,
            new { rol = asignacion.Rol.Nombre, motivo }, ahora, ct);

        bandeja.Programar(new EventoPendiente(
            EventoId: Guid.CreateVersion7(ahora),
            TipoEvento: TiposEvento.SolicitudRechazada,
            Carga: JsonSerializer.Serialize(new CargaSolicitud(persona.Id, asignacion.Rol.Nombre, motivo)),
            OcurridoEn: ahora,
            ContextoTraza: contexto.CorrelacionId));

        await unidadTrabajo.ConfirmarAsync(ct);
    }

    private async Task<(Persona Persona, AsignacionRol Asignacion)> CargarAsync(Guid personaId, CancellationToken ct)
    {
        var persona = await personas.PorIdAsync(personaId, ct)
                      ?? throw new ExcepcionDominio("La persona indicada no existe.");

        var asignacion = await personas.RolActivoAsync(personaId, ct)
                         ?? throw new ExcepcionDominio("Esta persona no tiene una solicitud de rol registrada.");

        return (persona, asignacion);
    }

    private async Task ExigirPuedeAprobarAsync(
        ContextoPeticion contexto, AsignacionRol solicitud, string queIntento, Guid entidadId, CancellationToken ct)
    {
        if (JerarquiaInstitucional.PuedeAprobar(contexto.Ambito, solicitud.Rol, solicitud.FacultadId, solicitud.ProgramaId))
            return;

        var ahora = reloj.GetUtcNow();
        await auditoria.RegistrarAccesoRechazadoAsync(contexto, queIntento, entidadId, ahora, ct);
        await unidadTrabajo.ConfirmarAsync(ct);

        throw new ExcepcionAutorizacion(
            "El rol " + contexto.Rol.Nombre + " no aprueba solicitudes de " + solicitud.Rol.Nombre + " en ese ámbito.");
    }
}

/// <summary>Consultas de identidad: quién soy y qué solicitudes me toca aprobar.</summary>
public sealed class ConsultarIdentidadCasoUso(
    IPersonaRepositorio personas,
    IEstructuraAcademicaRepositorio academico,
    ICacheConsultas cache)
{
    public async Task<PersonaDto?> MiPerfilAsync(Guid personaId, CancellationToken ct)
    {
        var persona = await personas.PorIdAsync(personaId, ct);
        if (persona is null)
            return null;

        var asignacion = await personas.RolActivoAsync(personaId, ct);
        return RegistrarCuentaCasoUso.Convertir(persona, asignacion?.Rol ?? Rol.Estudiante);
    }

    /// <summary>
    /// Las solicitudes que este usuario tiene que resolver, limitadas a **su** ámbito. Es el caso que
    /// el enunciado detalla: un coordinador ve las de sus programas, no todas.
    /// </summary>
    public async Task<IReadOnlyList<PersonaDto>> PendientesAsync(ContextoPeticion contexto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var pendientes = await personas.PendientesDeAprobacionAsync(
            contexto.Ambito.FacultadId, contexto.Ambito.ProgramaId, ct);

        var resultado = new List<PersonaDto>();
        foreach (var persona in pendientes)
        {
            var asignacion = await personas.RolActivoAsync(persona.Id, ct);
            if (asignacion is null)
                continue;

            if (JerarquiaInstitucional.PuedeAprobar(contexto.Ambito, asignacion.Rol, asignacion.FacultadId, asignacion.ProgramaId))
                resultado.Add(RegistrarCuentaCasoUso.Convertir(persona, asignacion.Rol));
        }

        return resultado;
    }

    /// <summary>
    /// La estructura académica para elegir al registrarse. Va por caché: cambia muy poco y se
    /// consulta en cada registro (ADR-0010).
    /// </summary>
    public async Task<IReadOnlyList<FacultadDto>> EstructuraAsync(CancellationToken ct)
    {
        const string clave = GruposCache.EstructuraAcademica + ":arbol";

        var enCache = await cache.ObtenerAsync<List<FacultadDto>>(clave, ct);
        if (enCache is not null)
            return enCache;

        var facultades = await academico.FacultadesAsync(ct);
        var arbol = new List<FacultadDto>();

        foreach (var facultad in facultades.Where(f => f.Activa))
        {
            var programas = await academico.ProgramasDeFacultadAsync(facultad.Id, ct);
            arbol.Add(new FacultadDto(
                facultad.Id,
                facultad.Nombre,
                facultad.Codigo,
                [.. programas.Where(p => p.Activo).Select(p => new ProgramaDto(p.Id, p.Nombre, p.Codigo))]));
        }

        await cache.GuardarAsync(clave, arbol, ct);
        return arbol;
    }
}
