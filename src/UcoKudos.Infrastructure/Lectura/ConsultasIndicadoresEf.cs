using Microsoft.EntityFrameworkCore;
using UcoKudos.Application.Indicadores;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Interaccion;
using UcoKudos.Infrastructure.Persistencia;

namespace UcoKudos.Infrastructure.Lectura;

/// <summary>
/// Indicadores de gestión. Son **agregaciones de solo lectura** y no pasan por las entidades de
/// dominio: cada cifra es una consulta que el motor resuelve con sus agrupaciones.
///
/// Un Kudo cuenta en el ámbito de **su destinatario**, según el rol activo de este. Lo vetado no
/// cuenta.
/// </summary>
public sealed class ConsultasIndicadoresEf(UcoKudosDbContext contexto) : IConsultasIndicadores
{
    private const int Tamano = 5;

    public async Task<ResumenCrudo> ResumenAsync(AlcanceDeIndicadores alcance, DateTimeOffset desde, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(alcance);

        var facultadId = alcance.FacultadId;
        var programaId = alcance.ProgramaId;

        // Los roles activos dentro del ámbito. Hay a lo sumo uno activo por persona (índice único).
        var rolesDelAmbito = contexto.AsignacionesRol.AsNoTracking()
            .Where(a => a.Activa
                        && (programaId == null || a.ProgramaId == programaId)
                        && (facultadId == null || a.FacultadId == facultadId));

        var kudos =
            from kudo in contexto.Kudos.AsNoTracking()
            where !kudo.Vetado
            join rol in rolesDelAmbito on kudo.DestinatarioId equals rol.PersonaId
            select kudo;

        var total = await kudos.CountAsync(ct);

        var personas = await (
            from persona in contexto.Personas.AsNoTracking()
            where persona.Estado == EstadoCuenta.Activa
            join rol in rolesDelAmbito on persona.Id equals rol.PersonaId
            select persona.Id).CountAsync(ct);

        var conCalificacion = kudos.Where(k => k.TotalCalificaciones > 0);
        var calificaciones = await conCalificacion.SumAsync(k => k.TotalCalificaciones, ct);
        var suma = await conCalificacion.SumAsync(k => (decimal?)(k.PromedioCalificacion * k.TotalCalificaciones), ct) ?? 0m;

        var reportes = await contexto.Reportes.AsNoTracking().CountAsync(r => r.Estado == EstadoReporte.Pendiente, ct);

        var porCategoria = await (
            from grupo in kudos.GroupBy(k => k.CategoriaId).Select(g => new { Id = g.Key, Total = g.Count() })
            join categoria in contexto.Categorias.AsNoTracking() on grupo.Id equals categoria.Id
            select new ConteoDto(categoria.Nombre, grupo.Total)).ToListAsync(ct);

        var porMes = await kudos
            .Where(k => k.PublicadoEn >= desde)
            .GroupBy(k => new { k.PublicadoEn.Year, k.PublicadoEn.Month })
            .Select(g => new ConteoMes(g.Key.Year, g.Key.Month, g.Count()))
            .ToListAsync(ct);

        var masReconocidos = await (
            from grupo in kudos.GroupBy(k => k.DestinatarioId).Select(g => new { Id = g.Key, Total = g.Count() })
            join persona in contexto.Personas.AsNoTracking() on grupo.Id equals persona.Id
            orderby grupo.Total descending, persona.NombreCompleto
            select new ConteoDto(persona.NombreCompleto, grupo.Total)).Take(Tamano).ToListAsync(ct);

        var masReconocen = await (
            from grupo in kudos.GroupBy(k => k.AutorId).Select(g => new { Id = g.Key, Total = g.Count() })
            join persona in contexto.Personas.AsNoTracking() on grupo.Id equals persona.Id
            orderby grupo.Total descending, persona.NombreCompleto
            select new ConteoDto(persona.NombreCompleto, grupo.Total)).Take(Tamano).ToListAsync(ct);

        return new ResumenCrudo(
            total, personas, suma, calificaciones, reportes, porCategoria, porMes, masReconocidos, masReconocen);
    }
}
