using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace UcoKudos.Infrastructure.Notificaciones;

/// <summary>
/// Canal de avisos en vivo por eventos enviados por el servidor (ADR-0009).
///
/// Mantiene una cola por conexión abierta. Es **estado en memoria del proceso**, y está declarado
/// como tal en la matriz Twelve-Factor (factor VI): con varias instancias, cada una conocería solo
/// sus conexiones, así que la política de «si está conectado, no enviar Web Push» habría que
/// coordinarla. Hoy hay una sola instancia y queda anotado como punto de revisión.
///
/// No es estado de negocio: perderlo al reiniciar solo obliga al navegador a reconectar, y el aviso
/// está persistido de todos modos.
/// </summary>
public sealed class AvisosEnVivoSse : Application.Notificaciones.Salida.Puertos.IAvisosEnVivo
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<string>>> _conexiones = new();

    public bool EstaConectado(Guid personaId) =>
        _conexiones.TryGetValue(personaId, out var canales) && !canales.IsEmpty;

    public async Task<bool> EntregarAsync(Guid personaId, string clave, object datos, CancellationToken ct)
    {
        if (!_conexiones.TryGetValue(personaId, out var canales) || canales.IsEmpty)
            return false;

        var cuerpo = JsonSerializer.Serialize(new { clave, datos });
        var entregado = false;

        foreach (var canal in canales.Values)
        {
            // Escritura sin esperar: si una conexión está saturada se descarta ese aviso en lugar de
            // bloquear el proceso de notificaciones. El aviso sigue persistido.
            if (canal.Writer.TryWrite(cuerpo))
                entregado = true;
        }

        await Task.CompletedTask;
        return entregado;
    }

    /// <summary>
    /// Registra una conexión nueva y devuelve su cola. La capa de interfaces la consume y la libera
    /// con <see cref="Cerrar"/> cuando el cliente se va.
    /// </summary>
    public (Guid ConexionId, ChannelReader<string> Lector) Abrir(Guid personaId)
    {
        // Cola acotada y descartando lo más viejo: si un cliente deja de leer, no crece sin límite.
        var canal = Channel.CreateBounded<string>(new BoundedChannelOptions(50)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

        var conexionId = Guid.NewGuid();
        var canales = _conexiones.GetOrAdd(personaId, _ => new ConcurrentDictionary<Guid, Channel<string>>());
        canales[conexionId] = canal;

        return (conexionId, canal.Reader);
    }

    public void Cerrar(Guid personaId, Guid conexionId)
    {
        if (!_conexiones.TryGetValue(personaId, out var canales))
            return;

        if (canales.TryRemove(conexionId, out var canal))
            canal.Writer.TryComplete();

        if (canales.IsEmpty)
            _conexiones.TryRemove(personaId, out _);
    }

    /// <summary>Cuántas conexiones hay abiertas. Se expone como métrica.</summary>
    public int ConexionesAbiertas => _conexiones.Values.Sum(c => c.Count);
}
