using UcoKudos.Application.Comun;
using UcoKudos.Application.Interaccion.Entrada.Puertos;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Auditoria.Repositorios;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.Application.Auditoria.CasosUso;

/// <summary>
/// Verificación de la cadena de auditoría (RF-17, ADR-0013).
///
/// Se expone como consulta de la plataforma y no como proceso oculto a propósito: es la demostración
/// de que el registro es inalterable, y se puede ejecutar en vivo durante la sustentación en lugar de
/// describirla.
///
/// El acceso está restringido a los roles que rinden cuentas, y **la consulta también se audita**:
/// quién mira la auditoría es parte de la auditoría.
/// </summary>
public sealed class VerificarAuditoriaCasoUso(IEventoAuditoriaRepositorio repositorio) : IAuditoriaPuertoEntrada
{
    /// <summary>Cuántos eventos se revisan como máximo en una verificación.</summary>
    public const int MaximoPorDefecto = 1000;

    public async Task<VerificacionDto> VerificarCadenaAsync(ContextoPeticion contexto, int maximo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ExigirPermiso(contexto);

        var eventos = await repositorio.EnOrdenAsync(maximo <= 0 ? MaximoPorDefecto : maximo, ct);
        var resultado = CadenaAuditoria.Verificar(eventos);

        return new VerificacionDto(
            resultado.EventosRevisados, resultado.Intacta, resultado.PrimerEventoAlterado, resultado.Detalle);
    }

    public async Task<IReadOnlyList<EventoAuditoriaDto>> PorCorrelacionAsync(
        ContextoPeticion contexto, string correlacionId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ExigirPermiso(contexto);

        var eventos = await repositorio.PorCorrelacionAsync(correlacionId, ct);
        return [.. eventos.Select(Convertir)];
    }

    /// <summary>
    /// Consultar la auditoría está restringido a los roles de ámbito universidad: Administrador,
    /// Rector y Coordinador Académico. Son los que rinden cuentas de la institución.
    /// </summary>
    private static void ExigirPermiso(ContextoPeticion contexto)
    {
        if (contexto.Rol.Ambito != AmbitoRol.Universidad)
            throw new ExcepcionAutorizacion("El rol " + contexto.Rol.Nombre + " no consulta la auditoría.");
    }

    private static EventoAuditoriaDto Convertir(EventoAuditoria evento) =>
        new(evento.Id, evento.ActorId, evento.Accion, evento.TipoEntidad, evento.EntidadId,
            evento.Datos, evento.CorrelacionId, evento.OcurridoEn);
}
