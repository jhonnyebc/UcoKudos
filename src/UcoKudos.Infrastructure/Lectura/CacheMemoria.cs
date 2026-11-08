using System.Collections.Concurrent;
using System.Text.Json;
using UcoKudos.Application.Comun.Salida.Puertos;

namespace UcoKudos.Infrastructure.Lectura;

/// <summary>
/// Caché del lado de lectura, en memoria del proceso (ADR-0010).
///
/// Dos decisiones que conviene poder defender:
///
///  - **En memoria y no distribuida.** Hoy hay una sola instancia, así que una caché distribuida
///    añadiría un salto de red sin ningún beneficio. Está detrás del puerto
///    <see cref="ICacheConsultas"/>, así que el cambio, cuando haya más de una instancia, es de
///    adaptador y no de diseño.
///
///  - **Invalidación por evento, no por expiración.** Una expiración por tiempo obliga a elegir entre
///    mostrar datos viejos y que la caché sirva de poco. Por evento no hay ese compromiso: publicar
///    un Kudo invalida la primera página del muro y el Kudo aparece de inmediato.
///
/// Las entradas se agrupan por prefijo para poder invalidar un grupo completo. Los grupos están
/// declarados en <see cref="GruposCache"/>, en un solo lugar: una invalidación dispersa por el código
/// es una fuente garantizada de datos viejos.
/// </summary>
public sealed class CacheMemoria : ICacheConsultas
{
    private readonly ConcurrentDictionary<string, string> _entradas = new(StringComparer.Ordinal);

    public Task<T?> ObtenerAsync<T>(string clave, CancellationToken ct) where T : class
    {
        if (!_entradas.TryGetValue(clave, out var json))
            return Task.FromResult<T?>(null);

        try
        {
            return Task.FromResult(JsonSerializer.Deserialize<T>(json));
        }
        catch (JsonException)
        {
            // Una entrada que no se puede leer se descarta: es caché, no fuente de verdad.
            _entradas.TryRemove(clave, out _);
            return Task.FromResult<T?>(null);
        }
    }

    /// <summary>
    /// Guarda el valor ya serializado, no la entidad. Así una lectura posterior no vuelve a
    /// transformarlo y nadie puede mutar por accidente lo que está en la caché.
    /// </summary>
    public Task GuardarAsync<T>(string clave, T valor, CancellationToken ct) where T : class
    {
        _entradas[clave] = JsonSerializer.Serialize(valor);
        return Task.CompletedTask;
    }

    public Task InvalidarGrupoAsync(string grupo, CancellationToken ct)
    {
        var prefijo = grupo + ":";

        foreach (var clave in _entradas.Keys.Where(k => k.StartsWith(prefijo, StringComparison.Ordinal)).ToList())
            _entradas.TryRemove(clave, out _);

        return Task.CompletedTask;
    }

    /// <summary>Cuántas entradas hay. Se expone para las pruebas y como métrica.</summary>
    public int Entradas => _entradas.Count;
}
