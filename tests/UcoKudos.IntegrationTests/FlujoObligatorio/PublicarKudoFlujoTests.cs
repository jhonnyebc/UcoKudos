using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UcoKudos.Application.Kudos.Entrada.Dto;
using UcoKudos.Application.Kudos.Entrada.Puertos;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Identidad;
using UcoKudos.IntegrationTests.Infraestructura;

namespace UcoKudos.IntegrationTests.FlujoObligatorio;

/// <summary>
/// **La funcionalidad obligatoria del enunciado (RF-07), de punta a punta.**
///
/// Hasta ahora las piezas estaban probadas por separado: las invariantes, la bandeja, la cadena de
/// auditoría. Esta clase prueba que **el flujo entero funciona junto**, que es lo que el enunciado
/// evalúa:
///
///   Paso 1  El Kudo se almacena correctamente.
///   Paso 2  El destinatario recibe un correo.
///   Paso 3  El destinatario recibe un WhatsApp.
///   Paso 4  Recibe notificación en tiempo real.
///   Paso 5  Todo el proceso queda auditado.
///
/// Contra PostgreSQL real y con los adaptadores simulados de los tres canales, así que no toca la
/// red ni gasta cuota de ningún proveedor.
///
/// Lo que estas pruebas demuestran, y que es el argumento central de la arquitectura: los cinco
/// pasos **no ocurren en la misma transacción**. Los pasos 1 y 5 se confirman juntos; los pasos 2, 3
/// y 4 los produce el proceso de la bandeja después (ADR-0005).
/// </summary>
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Category", "Integration")]
public sealed class PublicarKudoFlujoTests(BaseDeDatosFixture baseDatos) : IAsyncLifetime
{
    private PlataformaFixture _plataforma = null!;
    private DatosSembrados _datos = null!;
    private Guid _facultadId;
    private Guid _programaId;

    public async Task InitializeAsync()
    {
        await baseDatos.LimpiarAsync();

        _plataforma = new PlataformaFixture(baseDatos);

        await using var contexto = baseDatos.CrearContexto();
        _datos = await Semillas.SembrarAsync(contexto, PlataformaFixture.Ahora);

        var asignacion = await contexto.AsignacionesRol.FirstAsync(a => a.PersonaId == _datos.AutorId);
        _facultadId = asignacion.FacultadId!.Value;
        _programaId = asignacion.ProgramaId!.Value;
    }

    public Task DisposeAsync()
    {
        _plataforma.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Paso 1 y paso 5, que son los que ocurren **dentro** de la transacción.
    ///
    /// Y la observación que importa: al terminar la publicación, el Kudo y su auditoría ya están, y
    /// el evento está **pendiente**. Todavía no se envió nada. Eso es lo que hace que publicar
    /// responda rápido y que un proveedor caído no impida publicar.
    /// </summary>
    [Fact]
    public async Task Publicar_guarda_el_kudo_lo_audita_y_deja_el_evento_pendiente()
    {
        var kudoId = await PublicarAsync("Gracias por sostener al grupo toda la semana.");

        await using var contexto = baseDatos.CrearContexto();

        // Paso 1: persistencia.
        var kudo = await contexto.Kudos.FirstOrDefaultAsync(k => k.Id == kudoId);
        Assert.NotNull(kudo);
        Assert.Equal(_datos.AutorId, kudo.AutorId);
        Assert.Equal(_datos.DestinatarioId, kudo.DestinatarioId);
        Assert.False(kudo.Vetado);

        // Paso 5: auditoría, en la misma transacción.
        var auditoria = await contexto.EventosAuditoria
            .Where(e => e.EntidadId == kudoId && e.Accion == AccionesAuditoria.KudoPublicar)
            .ToListAsync();
        Assert.Single(auditoria);
        Assert.Equal(_datos.AutorId, auditoria[0].ActorId);

        // El evento quedó pendiente: los avisos todavía no salieron.
        var pendientes = await contexto.MensajesOutbox.Where(m => m.ProcesadoEn == null).ToListAsync();
        Assert.Single(pendientes);
        Assert.Equal(kudoId, pendientes[0].EventoId);

        Assert.Empty(_plataforma.Correo.Enviados);
        Assert.Empty(_plataforma.WhatsApp.Enviados);
    }

    /// <summary>
    /// **La prueba central: los cinco pasos completos.**
    ///
    /// Publica y después ejecuta una vuelta del proceso de la bandeja, que es lo que en producción
    /// ocurre un par de segundos más tarde.
    /// </summary>
    [Fact]
    public async Task El_flujo_completo_produce_los_cinco_pasos()
    {
        var kudoId = await PublicarAsync("Reconocimiento por el trabajo con el grupo.");

        var atendidos = await _plataforma.Proceso.ProcesarVueltaAsync(CancellationToken.None);
        Assert.Equal(1, atendidos);

        // Paso 2: correo al destinatario, a su dirección institucional.
        var correo = Assert.Single(_plataforma.Correo.Enviados);
        Assert.Equal(_datos.CorreoDestinatario, correo.Destino);
        Assert.Contains("Kudo", correo.Asunto, StringComparison.OrdinalIgnoreCase);
        // El nombre del autor y la categoría viajan en el cuerpo, que sale del catálogo de mensajes.
        Assert.Contains("Ana Gómez", correo.Texto, StringComparison.Ordinal);
        Assert.Contains("Rendimiento Académico", correo.Texto, StringComparison.Ordinal);

        // Paso 3: WhatsApp, y SIEMPRE por plantilla aprobada: el destinatario nunca escribió
        // primero, así que la plataforma está siempre fuera de la ventana de 24 horas (ADR-0007).
        var whatsApp = Assert.Single(_plataforma.WhatsApp.Enviados);
        Assert.Equal(_datos.CelularDestinatario, whatsApp.Telefono);
        Assert.Equal("kudo_recibido", whatsApp.Plantilla);
        Assert.Equal(["Ana Gómez", "Rendimiento Académico"], whatsApp.Parametros);

        await using var contexto = baseDatos.CrearContexto();

        // Paso 4: tiempo real. Nadie está conectado por SSE y no hay suscripción de Web Push, así
        // que el aviso queda PERSISTIDO y el destinatario lo verá al entrar. No es un fallo: es la
        // degradación elegante de ADR-0009.
        var aviso = Assert.Single(await contexto.Avisos.Where(a => a.PersonaId == _datos.DestinatarioId).ToListAsync());
        Assert.Equal("kudo.recibido", aviso.ClaveMensaje);
        Assert.False(aviso.Leido);

        // Los tres canales quedaron registrados con su resultado.
        var registros = await contexto.RegistrosNotificacion.Where(n => n.EventoId == kudoId).ToListAsync();
        Assert.Equal(3, registros.Count);
        Assert.All(registros, r => Assert.Equal("Enviado", r.Estado));
        Assert.Equal(["Correo", "TiempoReal", "WhatsApp"], registros.Select(r => r.Canal).Order());

        // El evento quedó procesado.
        Assert.Empty(await contexto.MensajesOutbox.Where(m => m.ProcesadoEn == null).ToListAsync());

        // Paso 5 completo: la publicación y los tres envíos, todos auditados.
        var acciones = await contexto.EventosAuditoria.Select(e => e.Accion).ToListAsync();
        Assert.Contains(AccionesAuditoria.KudoPublicar, acciones);
        Assert.Equal(3, acciones.Count(a => a == AccionesAuditoria.NotificacionEnviar));
    }

    /// <summary>
    /// **QA-CONF-02: un reproceso no duplica.**
    ///
    /// Es una de las tres pruebas no negociables de la pirámide. Se fuerza una segunda vuelta sobre
    /// el mismo evento y el destinatario tiene que seguir teniendo **un** correo y **un** WhatsApp.
    /// </summary>
    [Fact]
    public async Task Reprocesar_el_mismo_evento_no_duplica_los_avisos()
    {
        var kudoId = await PublicarAsync("Primera publicación.");

        await _plataforma.Proceso.ProcesarVueltaAsync(CancellationToken.None);

        // Se devuelve el evento a la cola a mano, como si un fallo lo hubiera reintentado.
        await using (var contexto = baseDatos.CrearContexto())
        {
            await contexto.Database.ExecuteSqlRawAsync(
                "UPDATE notif.outbox_mensaje SET procesado_en = NULL WHERE evento_id = {0}", kudoId);
        }

        await _plataforma.Proceso.ProcesarVueltaAsync(CancellationToken.None);

        // La deduplicación por evento y destinatario es lo que lo impide.
        Assert.Single(_plataforma.Correo.Enviados);
        Assert.Single(_plataforma.WhatsApp.Enviados);
    }

    /// <summary>
    /// **QA-DISP-01: WhatsApp caído no impide publicar, y no arrastra a los otros canales.**
    ///
    /// El adaptador simulado devuelve un error transitorio, como haría Meta con un 503. El Kudo sigue
    /// publicado y auditado, el correo y el tiempo real salen igual, y el evento vuelve a la cola.
    /// </summary>
    [Fact]
    public async Task Si_whatsapp_falla_el_kudo_queda_publicado_y_los_otros_canales_salen()
    {
        _plataforma.WhatsApp.FallarProximos.Enqueue(
            UcoKudos.Application.Notificaciones.Salida.Puertos.ResultadoEnvio.Transitorio("HTTP 503: servicio no disponible."));

        var kudoId = await PublicarAsync("Reconocimiento con el canal caído.");

        await _plataforma.Proceso.ProcesarVueltaAsync(CancellationToken.None);

        await using var contexto = baseDatos.CrearContexto();

        // El Kudo está publicado: el fallo del canal no alcanzó la transacción.
        Assert.NotNull(await contexto.Kudos.FirstOrDefaultAsync(k => k.Id == kudoId));

        // El correo salió igual: los canales son independientes.
        Assert.Single(_plataforma.Correo.Enviados);
        Assert.Empty(_plataforma.WhatsApp.Enviados);

        // El fallo quedó registrado y auditado.
        var registros = await contexto.RegistrosNotificacion.Where(n => n.EventoId == kudoId).ToListAsync();
        var fallido = Assert.Single(registros, r => r.Canal == "WhatsApp");
        Assert.Equal("Transitorio", fallido.Estado);
        Assert.Contains("503", fallido.Error ?? string.Empty, StringComparison.Ordinal);

        // Y el evento volvió a la cola con un intento más, para reintentar el canal que falló.
        var evento = await contexto.MensajesOutbox.FirstAsync(m => m.EventoId == kudoId);
        Assert.True(evento.Intentos > 0);
    }

    /// <summary>
    /// La traza cruza la frontera asíncrona (QA-OBS-01, ADR-0014): el identificador de correlación de
    /// la petición que publicó aparece también en la auditoría de los envíos, que ocurren después y
    /// en otro proceso.
    ///
    /// Es lo que permite reconstruir **una** publicación concreta de punta a punta con un solo valor.
    /// </summary>
    [Fact]
    public async Task La_correlacion_une_la_publicacion_con_la_auditoria_de_los_envios()
    {
        const string correlacion = "peticion-de-prueba-0001";

        var kudoId = await PublicarAsync("Publicación con correlación conocida.", correlacion);
        await _plataforma.Proceso.ProcesarVueltaAsync(CancellationToken.None);

        await using var contexto = baseDatos.CrearContexto();

        var eventos = await contexto.EventosAuditoria
            .Where(e => e.CorrelacionId == correlacion)
            .Select(e => e.Accion)
            .ToListAsync();

        // La publicación y los tres envíos, todos bajo el mismo identificador.
        Assert.Contains(AccionesAuditoria.KudoPublicar, eventos);
        Assert.Equal(3, eventos.Count(a => a == AccionesAuditoria.NotificacionEnviar));
    }

    /// <summary>
    /// La cadena de auditoría sigue intacta después del flujo completo.
    ///
    /// Importa porque el flujo escribe eventos desde **dos** procesos distintos: la petición que
    /// publica y el proceso de la bandeja. Si el encadenado fallara entre ellos, la cadena se
    /// rompería aquí y en ningún otro sitio.
    /// </summary>
    [Fact]
    public async Task La_cadena_de_auditoria_sigue_intacta_tras_el_flujo_completo()
    {
        await PublicarAsync("Primera.");
        await _plataforma.Proceso.ProcesarVueltaAsync(CancellationToken.None);

        await PublicarAsync("Segunda.");
        await _plataforma.Proceso.ProcesarVueltaAsync(CancellationToken.None);

        await using var contexto = baseDatos.CrearContexto();
        var eventos = await contexto.EventosAuditoria.OrderBy(e => e.Id).AsNoTracking().ToListAsync();

        var resultado = CadenaAuditoria.Verificar(eventos);

        Assert.True(resultado.Intacta, resultado.Detalle);
        Assert.True(resultado.EventosRevisados >= 8);
    }

    /// <summary>No se puede publicar un Kudo en una categoría desactivada.</summary>
    [Fact]
    public async Task No_se_publica_en_una_categoria_desactivada()
    {
        await using (var contexto = baseDatos.CrearContexto())
        {
            var categoria = await contexto.Categorias.FirstAsync(c => c.Id == _datos.CategoriaId);
            categoria.Desactivar();
            await contexto.SaveChangesAsync();
        }

        using var alcance = _plataforma.CrearAlcance();
        var puerto = alcance.ServiceProvider.GetRequiredService<IKudosPuertoEntrada>();

        await Assert.ThrowsAsync<UcoKudos.Domain.Transversal.ExcepcionDominio>(() =>
            puerto.PublicarAsync(ContextoDelAutor(), new PublicarKudoDto(_datos.DestinatarioId, _datos.CategoriaId, "Hola"), CancellationToken.None));
    }

    /// <summary>Publica un Kudo a través del puerto de entrada, como lo haría la API.</summary>
    private async Task<Guid> PublicarAsync(string contenido, string correlacion = "peticion-1")
    {
        using var alcance = _plataforma.CrearAlcance();
        var puerto = alcance.ServiceProvider.GetRequiredService<IKudosPuertoEntrada>();

        var resultado = await puerto.PublicarAsync(
            ContextoDelAutor(correlacion),
            new PublicarKudoDto(_datos.DestinatarioId, _datos.CategoriaId, contenido),
            CancellationToken.None);

        return resultado.Id;
    }

    private Application.Comun.ContextoPeticion ContextoDelAutor(string correlacion = "peticion-1") =>
        Semillas.ContextoDe(_datos.AutorId, Rol.Docente, _facultadId, _programaId, correlacion);
}
