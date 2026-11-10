using System.Text.Json;
using UcoKudos.Application.Comun;
using UcoKudos.Application.Notificaciones.Catalogo;
using UcoKudos.Application.Notificaciones.Entrada.Dto;
using UcoKudos.Application.Notificaciones.Salida.Puertos;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.Application.Notificaciones.CasosUso;

/// <summary>
/// La bandeja de avisos (RF-16).
///
/// Es la otra mitad de ADR-0009, y la que sostiene la promesa del resto: el canal en vivo es
/// efímero y Web Push puede no llegar —el navegador cerrado, la suscripción caducada, el permiso
/// denegado—, así que **el aviso se guarda siempre** y esta consulta es la que lo hace visible. Sin
/// ella, un aviso que no se entregó en el momento se pierde para siempre y la plataforma miente al
/// decir que notifica.
///
/// El texto se compone **aquí** a partir de la clave y las variables guardadas, no se lee hecho de
/// la base. Esa decisión tiene una consecuencia práctica: corregir una redacción en el catálogo
/// arregla también los avisos ya guardados, en lugar de dejar los viejos con el texto antiguo.
/// </summary>
public sealed class ConsultarAvisosCasoUso(IConsultasAvisos consultas)
{
    private const int TamanoPorOmision = 30;
    private const int TamanoMaximo = 100;

    public async Task<PaginaAvisosDto> MisAvisosAsync(
        ContextoPeticion contexto, int? tamano, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        // El tamaño se acota aquí y no se confía al cliente: un `tamano=1000000` sería una forma
        // barata de cargar el servidor desde fuera.
        var cuantos = Math.Clamp(tamano ?? TamanoPorOmision, 1, TamanoMaximo);

        var (filas, sinLeer) = await consultas.DeLaPersonaAsync(contexto.Ambito.PersonaId, cuantos, ct);

        return new PaginaAvisosDto(filas.Select(Componer).ToList(), sinLeer);
    }

    public async Task MarcarLeidoAsync(ContextoPeticion contexto, long avisoId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var marcado = await consultas.MarcarLeidoAsync(contexto.Ambito.PersonaId, avisoId, ct);

        if (!marcado)
        {
            // Mismo error para «no existe» y para «es de otra persona», y es deliberado: distinguir
            // los dos casos permitiría averiguar qué avisos tienen los demás probando identificadores.
            throw new ExcepcionAutorizacion("Ese aviso no existe o no es tuyo.");
        }
    }

    /// <summary>
    /// Compone el texto del aviso.
    ///
    /// Si el catálogo ya no tiene esa clave, o las variables guardadas no cuadran con las que el
    /// mensaje espera, **no se rompe la bandeja entera**: ese aviso se muestra con un texto
    /// genérico y los demás se ven con normalidad. Un aviso viejo de una versión anterior no debe
    /// impedirle a nadie leer los de hoy.
    /// </summary>
    private static AvisoDto Componer(FilaAviso fila)
    {
        try
        {
            var mensaje = CatalogoMensajes.Por(fila.ClaveMensaje);
            var variables = JsonSerializer.Deserialize<string[]>(fila.VariablesJson) ?? [];

            return new AvisoDto(
                fila.Id,
                fila.ClaveMensaje,
                mensaje.Titulo,
                CatalogoMensajes.Formatear(mensaje, variables),
                fila.Leido,
                fila.GeneradoEn);
        }
        catch (Exception error) when (error is InvalidOperationException or JsonException or FormatException)
        {
            return new AvisoDto(
                fila.Id,
                fila.ClaveMensaje,
                "Aviso",
                "Este aviso no se puede mostrar completo.",
                fila.Leido,
                fila.GeneradoEn);
        }
    }
}
