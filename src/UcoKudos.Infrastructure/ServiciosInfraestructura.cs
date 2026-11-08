using Microsoft.Extensions.DependencyInjection;

namespace UcoKudos.Infrastructure;

/// <summary>
/// Registro de los adaptadores de salida (ADR-0001): persistencia, correo, WhatsApp, notificación en
/// tiempo real y almacenamiento de objetos.
///
/// Esta clase es el único punto por el que la capa de interfaces toca la infraestructura: la API la
/// llama desde su raíz de composición y, fuera de ahí, no conoce ningún tipo de esta capa. La prueba
/// de arquitectura <c>La_api_no_usa_la_infraestructura_fuera_de_la_raiz_de_composicion</c> lo vigila.
/// </summary>
public static class ServiciosInfraestructura
{
    public static IServiceCollection AgregarInfraestructura(this IServiceCollection servicios)
    {
        // Los adaptadores se registran aquí a medida que se implementan.
        return servicios;
    }
}
