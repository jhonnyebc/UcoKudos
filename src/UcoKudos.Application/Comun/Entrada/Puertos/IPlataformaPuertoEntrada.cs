using System.Threading.Channels;
using UcoKudos.Application.Notificaciones.Entrada.Dto;

namespace UcoKudos.Application.Comun.Entrada.Puertos;

/// <summary>
/// Puerto de entrada de los avisos en tiempo real (RF-16, ADR-0009).
///
/// Existe porque la prueba de arquitectura lo exigió: la primera versión de los endpoints usaba
/// directamente el adaptador de SSE de la infraestructura, y la regla 4 de ADR-0001 falló. Era una
/// violación real, no un falso positivo, y este puerto es la corrección.
///
/// Es un buen ejemplo de para qué sirve esa prueba: el compilador lo permitía, porque la API
/// referencia la infraestructura para su raíz de composición, así que nada más lo habría detectado.
/// </summary>
public interface IAvisosPuertoEntrada
{
    /// <summary>
    /// Abre una conexión de avisos para una persona y devuelve la cola por la que llegarán.
    ///
    /// La cola es un tipo de la biblioteca base, no de infraestructura: la capa de interfaces se
    /// encarga de las mecánicas de HTTP y esta capa solo entrega los avisos.
    /// </summary>
    (Guid ConexionId, ChannelReader<string> Lector) Abrir(Guid personaId);

    /// <summary>Cierra la conexión cuando el cliente se va.</summary>
    void Cerrar(Guid personaId, Guid conexionId);

    /// <summary>
    /// Clave pública VAPID, para que el cliente se suscriba a Web Push. **No es secreta**: la clave
    /// privada sí, y nunca sale de la configuración del servidor (ADR-0009, ADR-0018).
    ///
    /// Null si el canal no está configurado, que no es un error: el aviso queda persistido y el
    /// usuario lo ve al entrar.
    /// </summary>
    string? ClavePublicaPush();

    /// <summary>
    /// La bandeja de avisos de quien pregunta (RF-16).
    ///
    /// Recibe el contexto de la petición, no un identificador de persona: **quién pregunta lo dice
    /// el token, no el cliente**. Aceptar un identificador por parámetro dejaría que cualquiera
    /// pidiera los avisos de otro con solo cambiarlo en la URL.
    /// </summary>
    Task<PaginaAvisosDto> MisAvisosAsync(ContextoPeticion contexto, int? tamano, CancellationToken ct);

    /// <summary>Marca un aviso propio como leído. Falla si no es de quien pregunta.</summary>
    Task MarcarLeidoAsync(ContextoPeticion contexto, long avisoId, CancellationToken ct);
}

/// <summary>
/// Puerto de entrada de los puntos de salud (ADR-0014).
///
/// El de preparación tiene que comprobar la base de datos, y comprobarla sin conocer el contexto de
/// persistencia solo es posible detrás de un puerto. La alternativa habría sido que el endpoint
/// dependiera del contexto de EF Core, que es lo que la regla 4 prohíbe.
/// </summary>
public interface ISaludPuertoEntrada
{
    /// <summary>
    /// ¿Puede la instancia atender tráfico? Comprueba las dependencias que necesita para trabajar.
    ///
    /// Devuelve solo un sí o un no, sin detalle: un punto de salud dice si el sistema está listo, no
    /// qué tiene roto. Para eso están las trazas y las métricas.
    /// </summary>
    Task<bool> EstaListoAsync(CancellationToken ct);
}
