namespace UcoKudos.Application.Comun.CasosUso;

/// <summary>
/// Caso de uso que devuelve un resultado (ADR-0001). Un caso de uso es una unidad de intención del
/// negocio: recibe sus datos de entrada, orquesta el dominio y los puertos de salida, y devuelve lo
/// que la capa de interfaces necesita. No conoce HTTP, ni la base de datos, ni los proveedores.
/// </summary>
public interface ICasoUso<in TEntrada, TSalida>
{
    Task<TSalida> EjecutarAsync(TEntrada entrada, CancellationToken ct);
}

/// <summary>Caso de uso sin resultado: ejecuta un cambio de estado y no devuelve datos.</summary>
public interface ICasoUsoSinRetorno<in TEntrada>
{
    Task EjecutarAsync(TEntrada entrada, CancellationToken ct);
}
