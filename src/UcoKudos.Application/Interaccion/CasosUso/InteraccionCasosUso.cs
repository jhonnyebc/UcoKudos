using System.Text.Json;
using UcoKudos.Application.Comun;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Application.Interaccion.Entrada.Dto;
using UcoKudos.Application.Notificaciones.CasosUso;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Identidad.Repositorios;
using UcoKudos.Domain.Interaccion;
using UcoKudos.Domain.Interaccion.Repositorios;
using UcoKudos.Domain.Kudos.Repositorios;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.Application.Interaccion.CasosUso;

/// <summary>
/// Responder a un Kudo o a una réplica (RF-09): sin límite de profundidad, cantidad ni longitud.
///
/// Es donde el árbol materializado de ADR-0011 se construye. La ruta la calcula la propia entidad, en
/// un solo lugar, porque es el único modo de que no se desincronice con la referencia al padre.
/// </summary>
public sealed class ResponderCasoUso(
    IKudoRepositorio kudos,
    IReplicaRepositorio replicas,
    IPersonaRepositorio personas,
    IUnidadTrabajo unidadTrabajo,
    IBandejaSalida bandeja,
    ICacheConsultas cache,
    ServicioAuditoria auditoria,
    TimeProvider reloj)
{
    public async Task<ReplicaDto> EjecutarAsync(ContextoPeticion contexto, Guid kudoId, ResponderDto datos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(datos);

        var ahora = reloj.GetUtcNow();
        var kudo = await kudos.PorIdAsync(kudoId, ct)
                   ?? throw new ExcepcionDominio("El Kudo indicado no existe.");

        if (kudo.Vetado)
            throw new ExcepcionDominio("No se puede responder a un contenido retirado por moderación.");

        Replica replica;
        if (datos.ReplicaPadreId is null)
        {
            replica = Replica.SobreKudo(kudoId, contexto.PersonaId, datos.Contenido, ahora);
        }
        else
        {
            var padre = await replicas.PorIdAsync(datos.ReplicaPadreId.Value, ct)
                        ?? throw new ExcepcionDominio("La réplica a la que responde no existe.");

            if (padre.KudoId != kudoId)
                throw new ExcepcionDominio("Esa réplica pertenece a otro Kudo.");

            // Lo vetado ya no se muestra: una respuesta a ello quedaría colgando de la nada.
            if (padre.Vetado)
                throw new ExcepcionDominio("No se puede responder a un contenido retirado por moderación.");

            replica = Replica.SobreReplica(padre, contexto.PersonaId, datos.Contenido, ahora);
        }

        replicas.Agregar(replica);

        // Contador materializado al día, en la misma transacción que la réplica: es lo que evita que
        // se desfase (ADR-0010).
        kudo.FijarTotalReplicas(await kudos.ContarReplicasAsync(kudoId, ct) + 1);

        await auditoria.RegistrarAsync(
            contexto, AccionesAuditoria.ReplicaPublicar, nameof(Replica), replica.Id,
            new { kudoId, padreId = replica.ReplicaPadreId, profundidad = replica.Profundidad }, ahora, ct);

        var autor = await personas.PorIdAsync(contexto.PersonaId, ct);

        // Se avisa al destinatario del Kudo, salvo que sea quien responde.
        if (kudo.DestinatarioId != contexto.PersonaId)
        {
            bandeja.Programar(new EventoPendiente(
                EventoId: replica.Id,
                TipoEvento: TiposEvento.ReplicaPublicada,
                Carga: JsonSerializer.Serialize(new CargaReplicaPublicada(
                    replica.Id, kudoId, contexto.PersonaId, autor?.NombreCompleto ?? "Alguien", kudo.DestinatarioId)),
                OcurridoEn: ahora,
                ContextoTraza: contexto.CorrelacionId));
        }

        await unidadTrabajo.ConfirmarAsync(ct);
        await cache.InvalidarGrupoAsync(GruposCache.Muro, ct);

        return Convertir(replica, autor?.NombreCompleto ?? "Alguien");
    }

    /// <summary>
    /// El hilo completo, en el orden en que se debe pintar.
    ///
    /// Llega ordenado por ruta desde la base, y ese orden **ya es** el orden anidado de presentación
    /// (ADR-0011): el cliente no tiene que reconstruir el árbol, solo usar la profundidad para la
    /// sangría. Menos trabajo en el celular, que es donde importa.
    /// </summary>
    public async Task<IReadOnlyList<ReplicaDto>> HiloAsync(Guid kudoId, CancellationToken ct)
    {
        var visibles = (await replicas.HiloDeAsync(kudoId, ct)).Where(r => !r.Vetado).ToList();

        // El nombre de cada autor, una vez por persona aunque haya respondido varias veces.
        var nombres = new Dictionary<Guid, string>();
        foreach (var autorId in visibles.Select(r => r.AutorId).Distinct())
        {
            var persona = await personas.PorIdAsync(autorId, ct);
            nombres[autorId] = persona?.NombreCompleto ?? "Alguien";
        }

        return [.. visibles.Select(r => Convertir(r, nombres[r.AutorId]))];
    }

    private static ReplicaDto Convertir(Replica replica, string autorNombre) =>
        new(replica.Id, replica.KudoId, replica.ReplicaPadreId, replica.AutorId, autorNombre,
            replica.Contenido, replica.Profundidad, replica.TotalReplicas, replica.PromedioCalificacion,
            replica.TotalCalificaciones, replica.PublicadaEn);
}

/// <summary>
/// Calificar un Kudo o una réplica, de 1 a 5 estrellas (RF-10).
///
/// Una calificación activa por usuario y contenido: cambiar de opinión **desactiva** la anterior en
/// lugar de editarla, así queda el histórico. La garantía de unicidad es un índice único parcial en
/// la base.
/// </summary>
public sealed class CalificarCasoUso(
    IKudoRepositorio kudos,
    IReplicaRepositorio replicas,
    ICalificacionRepositorio calificaciones,
    IUnidadTrabajo unidadTrabajo,
    ICacheConsultas cache,
    ServicioAuditoria auditoria,
    TimeProvider reloj)
{
    public async Task EjecutarAsync(ContextoPeticion contexto, CalificarDto datos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(datos);

        var ahora = reloj.GetUtcNow();
        var tipo = Enum.Parse<TipoContenido>(datos.TipoContenido, ignoreCase: true);

        await ExigirContenidoExistenteAsync(tipo, datos.ContenidoId, ct);

        var anterior = await calificaciones.ActivaDeAsync(contexto.PersonaId, tipo, datos.ContenidoId, ct);
        anterior?.Reemplazar();

        calificaciones.Agregar(new Calificacion(contexto.PersonaId, tipo, datos.ContenidoId, datos.Estrellas, ahora));

        // El resumen se recalcula contando la nueva, porque el repositorio todavía no la ha guardado.
        var resumen = await calificaciones.ResumenAsync(tipo, datos.ContenidoId, ct);
        var total = resumen.Total + (anterior is null ? 1 : 0);
        var suma = ((resumen.Promedio ?? 0) * resumen.Total) - (anterior?.Estrellas ?? 0) + datos.Estrellas;
        var promedio = total == 0 ? (decimal?)null : Math.Round(suma / total, 2);

        await ActualizarContadoresAsync(tipo, datos.ContenidoId, total, promedio, ct);

        await auditoria.RegistrarAsync(
            contexto, AccionesAuditoria.ContenidoCalificar, tipo.ToString(), datos.ContenidoId,
            new { estrellas = datos.Estrellas, reemplazo = anterior is not null }, ahora, ct);

        await unidadTrabajo.ConfirmarAsync(ct);
        await cache.InvalidarGrupoAsync(GruposCache.Muro, ct);
    }

    private async Task ExigirContenidoExistenteAsync(TipoContenido tipo, Guid contenidoId, CancellationToken ct)
    {
        bool? vetado = tipo == TipoContenido.Kudo
            ? (await kudos.PorIdAsync(contenidoId, ct))?.Vetado
            : (await replicas.PorIdAsync(contenidoId, ct))?.Vetado;

        if (vetado is null)
            throw new ExcepcionDominio("El contenido que intenta calificar no existe.");

        // Lo vetado ya no se muestra: calificarlo movería promedios de algo que nadie ve.
        if (vetado.Value)
            throw new ExcepcionDominio("Este contenido fue retirado por moderación y no se puede calificar.");
    }

    private async Task ActualizarContadoresAsync(
        TipoContenido tipo, Guid contenidoId, int total, decimal? promedio, CancellationToken ct)
    {
        if (tipo == TipoContenido.Kudo)
        {
            var kudo = await kudos.PorIdAsync(contenidoId, ct);
            kudo?.FijarCalificacion(total, promedio);
        }
        else
        {
            var replica = await replicas.PorIdAsync(contenidoId, ct);
            replica?.FijarCalificacion(total, promedio);
        }
    }
}
