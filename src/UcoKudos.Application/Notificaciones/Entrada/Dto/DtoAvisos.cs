namespace UcoKudos.Application.Notificaciones.Entrada.Dto;

/// <summary>
/// Un aviso tal como lo ve quien lo recibió (RF-16).
///
/// El texto llega **ya compuesto**. En la base solo se guarda la clave del mensaje y sus variables,
/// no la frase: así corregir una redacción no obliga a reescribir avisos ya guardados, y el mismo
/// aviso puede decirse distinto en el correo, en la notificación del navegador y aquí.
/// </summary>
/// <param name="Id">Identificador del aviso, para marcarlo como leído.</param>
/// <param name="Clave">Clave del mensaje, por si el cliente quiere tratar unos distinto de otros.</param>
/// <param name="Titulo">Título corto.</param>
/// <param name="Cuerpo">Texto completo, con sus variables ya puestas.</param>
/// <param name="Leido">Si la persona ya lo vio.</param>
/// <param name="GeneradoEn">Cuándo ocurrió lo que lo originó.</param>
public sealed record AvisoDto(
    long Id,
    string Clave,
    string Titulo,
    string Cuerpo,
    bool Leido,
    DateTimeOffset GeneradoEn);

/// <summary>
/// Una página de avisos, con cuántos quedan sin leer.
///
/// El contador va aparte y cuenta **todos** los no leídos, no solo los de esta página: es lo que
/// necesita la interfaz para poner el número en la campana, y calcularlo en el cliente a partir de
/// la página daría un número equivocado en cuanto haya más de una.
/// </summary>
public sealed record PaginaAvisosDto(IReadOnlyList<AvisoDto> Elementos, int SinLeer);
