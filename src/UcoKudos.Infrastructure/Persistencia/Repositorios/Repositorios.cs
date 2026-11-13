using Microsoft.EntityFrameworkCore;
using UcoKudos.Domain.Academico;
using UcoKudos.Domain.Academico.Repositorios;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Auditoria.Repositorios;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Identidad.Repositorios;
using UcoKudos.Domain.Interaccion;
using UcoKudos.Domain.Interaccion.Repositorios;
using UcoKudos.Domain.Kudos;
using UcoKudos.Domain.Kudos.Repositorios;

namespace UcoKudos.Infrastructure.Persistencia.Repositorios;

/// <summary>
/// Repositorio de personas y de sus roles.
///
/// Ningún método guarda: la confirmación es de la unidad de trabajo (ADR-0005). Y los métodos
/// devuelven entidades o listas, nunca consultas componibles: un repositorio que devolviera una
/// consulta diferida filtraría el ORM al dominio y anularía el beneficio del patrón.
/// </summary>
public sealed class PersonaRepositorio(UcoKudosDbContext contexto) : IPersonaRepositorio
{
    public Task<Persona?> PorIdAsync(Guid id, CancellationToken ct) =>
        contexto.Personas.FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <summary>
    /// Resuelve la persona desde el correo del token. Corre en **cada** petición para comprobar que
    /// la cuenta sigue activa (ADR-0004), así que va por el índice único del correo.
    /// </summary>
    public Task<Persona?> PorCorreoAsync(string correoInstitucional, CancellationToken ct)
    {
        var correo = Persona.NormalizarCorreo(correoInstitucional);
        return contexto.Personas.FirstOrDefaultAsync(p => p.CorreoInstitucional == correo, ct);
    }

    public Task<bool> ExisteCorreoAsync(string correoInstitucional, CancellationToken ct)
    {
        var correo = Persona.NormalizarCorreo(correoInstitucional);
        return contexto.Personas.AnyAsync(p => p.CorreoInstitucional == correo, ct);
    }

    public void Agregar(Persona persona) => contexto.Personas.Add(persona);

    public Task<AsignacionRol?> RolActivoAsync(Guid personaId, CancellationToken ct) =>
        contexto.AsignacionesRol
            .Where(a => a.PersonaId == personaId)
            .OrderByDescending(a => a.Activa)
            .ThenByDescending(a => a.Desde)
            .FirstOrDefaultAsync(ct);

    public Task<bool> ExisteRolActivoAsync(short rolId, Guid? facultadId, Guid? programaId, CancellationToken ct)
    {
        var rol = Rol.Por(rolId);

        return rol.Ambito switch
        {
            AmbitoRol.Universidad when !rol.EsUnicoEnLaUniversidad => Task.FromResult(false),
            AmbitoRol.Universidad => contexto.AsignacionesRol.AnyAsync(a => a.Activa && a.RolId == rolId, ct),

            // Docente y Estudiante tienen ámbito de programa pero NO son únicos: puede haber cientos
            // en el mismo programa. Sin esta línea, aprobar al segundo estudiante de un programa
            // fallaba con «ya hay alguien ejerciendo el rol». Lo encontró la primera prueba de punta a
            // punta del flujo de aprobación: ninguna anterior aprobaba a un estudiante.
            AmbitoRol.Programa when rol.Id != Rol.CoordinadorPrograma.Id => Task.FromResult(false),
            AmbitoRol.Facultad => contexto.AsignacionesRol.AnyAsync(
                a => a.Activa && a.RolId == rolId && a.FacultadId == facultadId, ct),
            _ => contexto.AsignacionesRol.AnyAsync(
                a => a.Activa && a.RolId == rolId && a.ProgramaId == programaId, ct)
        };
    }

    public void AgregarRol(AsignacionRol asignacion) => contexto.AsignacionesRol.Add(asignacion);

    public Task<VerificacionContacto?> VerificacionVigenteAsync(Guid personaId, TipoContacto tipo, CancellationToken ct) =>
        contexto.VerificacionesContacto
            .Where(v => v.PersonaId == personaId && v.Tipo == tipo && !v.Consumida)
            .OrderByDescending(v => v.CreadaEn)
            .FirstOrDefaultAsync(ct);

    public void AgregarVerificacion(VerificacionContacto verificacion) => contexto.VerificacionesContacto.Add(verificacion);

    public async Task<IReadOnlyList<(Persona Persona, AsignacionRol Rol)>> AdministradasAsync(
        IReadOnlyCollection<short> rolesAdministrados, Guid? facultadId, Guid? programaId, string? texto, int tamano, CancellationToken ct)
    {
        var consulta = from persona in contexto.Personas.AsNoTracking()
                       join asignacion in contexto.AsignacionesRol.AsNoTracking() on persona.Id equals asignacion.PersonaId
                       where asignacion.Activa && rolesAdministrados.Contains(asignacion.RolId)
                       select new { persona, asignacion };

        if (programaId is not null)
            consulta = consulta.Where(x => x.asignacion.ProgramaId == programaId);
        else if (facultadId is not null)
            consulta = consulta.Where(x => x.asignacion.FacultadId == facultadId);

        if (texto is not null)
        {
            var minuscula = texto.ToLower();
            consulta = consulta.Where(x => x.persona.NombreCompleto.ToLower().Contains(minuscula));
        }

        var filas = await consulta.OrderBy(x => x.persona.NombreCompleto).Take(tamano).ToListAsync(ct);

        return [.. filas.Select(f => (f.persona, f.asignacion))];
    }

    public async Task<IReadOnlyList<Persona>> PendientesDeAprobacionAsync(
        Guid? facultadId, Guid? programaId, CancellationToken ct)
    {
        // Las personas con cuenta pendiente cuyo rol solicitado cae en el ámbito del aprobador. El
        // filtro final por jerarquía lo hace el caso de uso, que es donde está la regla.
        var consulta = from persona in contexto.Personas
                       join asignacion in contexto.AsignacionesRol on persona.Id equals asignacion.PersonaId
                       where persona.Estado == EstadoCuenta.Pendiente && !persona.SolicitudAprobada
                       select new { persona, asignacion };

        if (programaId is not null)
            consulta = consulta.Where(x => x.asignacion.ProgramaId == programaId);
        else if (facultadId is not null)
            consulta = consulta.Where(x => x.asignacion.FacultadId == facultadId);

        return await consulta.Select(x => x.persona).Distinct().ToListAsync(ct);
    }
}

public sealed class EstructuraAcademicaRepositorio(UcoKudosDbContext contexto) : IEstructuraAcademicaRepositorio
{
    public Task<Facultad?> FacultadPorIdAsync(Guid id, CancellationToken ct) =>
        contexto.Facultades.FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<Programa?> ProgramaPorIdAsync(Guid id, CancellationToken ct) =>
        contexto.Programas.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Facultad>> FacultadesAsync(CancellationToken ct) =>
        await contexto.Facultades.OrderBy(f => f.Nombre).ToListAsync(ct);

    public async Task<IReadOnlyList<Programa>> ProgramasDeFacultadAsync(Guid facultadId, CancellationToken ct) =>
        await contexto.Programas.Where(p => p.FacultadId == facultadId).OrderBy(p => p.Nombre).ToListAsync(ct);

    public void AgregarFacultad(Facultad facultad) => contexto.Facultades.Add(facultad);

    public void AgregarPrograma(Programa programa) => contexto.Programas.Add(programa);

    public async Task<IReadOnlyList<Guid>> ProgramasDeAsync(Guid personaId, TipoVinculo tipo, CancellationToken ct) =>
        await contexto.VinculosAcademicos
            .Where(v => v.PersonaId == personaId && v.Tipo == tipo && v.Activo)
            .Select(v => v.ProgramaId)
            .ToListAsync(ct);

    public void AgregarVinculo(VinculoAcademico vinculo) => contexto.VinculosAcademicos.Add(vinculo);

    public async Task<IReadOnlyList<VinculoAcademico>> VinculosDeAsync(Guid personaId, CancellationToken ct) =>
        await contexto.VinculosAcademicos.Where(v => v.PersonaId == personaId && v.Activo).ToListAsync(ct);

    public Task<VinculoAcademico?> VinculoAsync(Guid personaId, Guid programaId, TipoVinculo tipo, CancellationToken ct) =>
        contexto.VinculosAcademicos.FirstOrDefaultAsync(
            v => v.PersonaId == personaId && v.ProgramaId == programaId && v.Tipo == tipo, ct);

    public async Task<IReadOnlyList<Programa>> ProgramasPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        ids.Count == 0 ? [] : await contexto.Programas.Where(p => ids.Contains(p.Id)).ToListAsync(ct);

    public Task<bool> ExisteCodigoDeFacultadAsync(string codigo, Guid? exceptoId, CancellationToken ct) =>
        contexto.Facultades.AnyAsync(f => f.Codigo == codigo && f.Id != exceptoId, ct);

    public Task<bool> ExisteCodigoDeProgramaAsync(string codigo, Guid? exceptoId, CancellationToken ct) =>
        contexto.Programas.AnyAsync(p => p.Codigo == codigo && p.Id != exceptoId, ct);
}

public sealed class KudoRepositorio(UcoKudosDbContext contexto) : IKudoRepositorio
{
    /// <summary>
    /// Carga el Kudo **con sus adjuntos**: son parte del agregado, así que operar sobre uno sin los
    /// otros dejaría el agregado a medias.
    /// </summary>
    public Task<Kudo?> PorIdAsync(Guid id, CancellationToken ct) =>
        contexto.Kudos.Include(k => k.Adjuntos).FirstOrDefaultAsync(k => k.Id == id, ct);

    public void Agregar(Kudo kudo) => contexto.Kudos.Add(kudo);

    /// <summary>
    /// Cuenta las réplicas visibles desde la fuente. Es la consulta que verifica el contador
    /// materializado, y la que lo recalcula cuando cambia (ADR-0010).
    /// </summary>
    public Task<int> ContarReplicasAsync(Guid kudoId, CancellationToken ct) =>
        contexto.Replicas.CountAsync(r => r.KudoId == kudoId && !r.Vetado, ct);
}

public sealed class CategoriaRepositorio(UcoKudosDbContext contexto) : ICategoriaRepositorio
{
    public Task<Categoria?> PorIdAsync(Guid id, CancellationToken ct) =>
        contexto.Categorias.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Categoria>> ActivasAsync(CancellationToken ct) =>
        await contexto.Categorias.Where(c => c.Activa).OrderBy(c => c.Nombre).ToListAsync(ct);

    public async Task<IReadOnlyList<Categoria>> TodasAsync(CancellationToken ct) =>
        await contexto.Categorias.OrderBy(c => c.Nombre).ToListAsync(ct);

    public Task<bool> ExisteNombreAsync(string nombre, CancellationToken ct)
    {
        var limpio = (nombre ?? string.Empty).Trim();
        return contexto.Categorias.AnyAsync(c => c.Nombre == limpio, ct);
    }

    public void Agregar(Categoria categoria) => contexto.Categorias.Add(categoria);
}

public sealed class ReplicaRepositorio(UcoKudosDbContext contexto) : IReplicaRepositorio
{
    public Task<Replica?> PorIdAsync(Guid id, CancellationToken ct) =>
        contexto.Replicas.FirstOrDefaultAsync(r => r.Id == id, ct);

    public void Agregar(Replica replica) => contexto.Replicas.Add(replica);

    /// <summary>
    /// El hilo completo en **una** consulta, ordenada por ruta.
    ///
    /// Ese orden ya es el orden anidado de presentación (ADR-0011), así que el cliente lo pinta tal
    /// como lo recibe. Con solo la referencia al padre habría que recorrer el árbol nivel por nivel.
    /// </summary>
    public async Task<IReadOnlyList<Replica>> HiloDeAsync(Guid kudoId, CancellationToken ct) =>
        await contexto.Replicas
            .Where(r => r.KudoId == kudoId)
            .OrderBy(r => r.Ruta)
            .ToListAsync(ct);

    /// <summary>
    /// Veta la réplica y todos sus descendientes buscando por **prefijo de ruta**: una sola
    /// operación, por profundo que sea el subárbol (RF-12).
    ///
    /// Va en SQL escrito a mano porque el ORM no expresa la búsqueda por prefijo de forma que use el
    /// índice de patrón de texto. Lleva su prueba de integración, como manda ADR-0003.
    /// </summary>
    public async Task<int> VetarEnCascadaAsync(RutaReplica ruta, CancellationToken ct)
    {
        var prefijo = ruta.PrefijoDeDescendientes();

        return await contexto.Database.ExecuteSqlAsync(
            $"""
             UPDATE interaccion.replica
             SET vetado = true
             WHERE ruta LIKE {prefijo + "%"} AND NOT vetado
             """,
            ct);
    }
}

public sealed class CalificacionRepositorio(UcoKudosDbContext contexto) : ICalificacionRepositorio
{
    public Task<Calificacion?> ActivaDeAsync(Guid personaId, TipoContenido tipo, Guid contenidoId, CancellationToken ct) =>
        contexto.Calificaciones.FirstOrDefaultAsync(
            c => c.PersonaId == personaId && c.TipoContenido == tipo && c.ContenidoId == contenidoId && c.Activa, ct);

    public void Agregar(Calificacion calificacion) => contexto.Calificaciones.Add(calificacion);

    public async Task<ResumenCalificacion> ResumenAsync(TipoContenido tipo, Guid contenidoId, CancellationToken ct)
    {
        var activas = contexto.Calificaciones.Where(c => c.TipoContenido == tipo && c.ContenidoId == contenidoId && c.Activa);

        var total = await activas.CountAsync(ct);
        if (total == 0)
            return new ResumenCalificacion(0, null);

        var promedio = await activas.AverageAsync(c => (decimal)c.Estrellas, ct);
        return new ResumenCalificacion(total, Math.Round(promedio, 2));
    }
}

public sealed class ModeracionRepositorio(UcoKudosDbContext contexto) : IModeracionRepositorio
{
    public void AgregarReporte(Reporte reporte) => contexto.Reportes.Add(reporte);

    public void AgregarVeto(Veto veto) => contexto.Vetos.Add(veto);

    public async Task<IReadOnlyList<Reporte>> ReportesPendientesAsync(CancellationToken ct) =>
        await contexto.Reportes
            .Where(r => r.Estado == EstadoReporte.Pendiente)
            .OrderBy(r => r.PresentadoEn)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Reporte>> ReportesPendientesDeAsync(
        TipoContenido tipo, Guid contenidoId, CancellationToken ct) =>
        await contexto.Reportes
            .Where(r => r.Estado == EstadoReporte.Pendiente && r.TipoContenido == tipo && r.ContenidoId == contenidoId)
            .ToListAsync(ct);

    public Task<Reporte?> ReportePorIdAsync(Guid id, CancellationToken ct) =>
        contexto.Reportes.FirstOrDefaultAsync(r => r.Id == id, ct);
}

/// <summary>
/// Repositorio de la auditoría (ADR-0013). No tiene borrado ni actualización, y es deliberado: la
/// tabla es de solo inserción y lo garantiza un disparador en la base.
/// </summary>
public sealed class EventoAuditoriaRepositorio(UcoKudosDbContext contexto) : IEventoAuditoriaRepositorio
{
    public void Agregar(EventoAuditoria evento) => contexto.EventosAuditoria.Add(evento);

    /// <summary>
    /// Hash del último evento, para encadenar el siguiente.
    ///
    /// Mira primero lo que ya está acumulado en el contexto y todavía sin confirmar: dentro de una
    /// misma transacción se pueden registrar varios eventos, y si no se tuvieran en cuenta, el
    /// segundo apuntaría al mismo anterior que el primero y la cadena quedaría rota desde el inicio.
    /// </summary>
    public async Task<string?> UltimoHashAsync(CancellationToken ct)
    {
        var pendiente = contexto.ChangeTracker.Entries<EventoAuditoria>()
            .Where(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added)
            .Select(e => e.Entity)
            .LastOrDefault();

        if (pendiente is not null)
            return pendiente.Hash;

        return await contexto.EventosAuditoria
            .OrderByDescending(e => e.Id)
            .Select(e => e.Hash)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<EventoAuditoria>> EnOrdenAsync(int maximo, CancellationToken ct) =>
        await contexto.EventosAuditoria
            .OrderBy(e => e.Id)
            .Take(maximo)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<EventoAuditoria>> PorCorrelacionAsync(string correlacionId, CancellationToken ct) =>
        await contexto.EventosAuditoria
            .Where(e => e.CorrelacionId == correlacionId)
            .OrderBy(e => e.Id)
            .AsNoTracking()
            .ToListAsync(ct);
}
