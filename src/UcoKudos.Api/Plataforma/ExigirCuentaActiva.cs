using UcoKudos.Domain.Identidad.Repositorios;

namespace UcoKudos.Api.Plataforma;

/// <summary>
/// Exige que quien lee tenga una cuenta activa en la plataforma, no solo un token válido.
///
/// **Por qué existe, y es un hallazgo de las pruebas de extremo a extremo.** La autorización de esta
/// plataforma se apoya en una decisión explícita (ADR-0004): el token solo dice *quién es* quien
/// llama, y la cuenta, el estado y el rol se resuelven contra la base **en cada petición**, de modo
/// que desactivar a alguien surte efecto de inmediato. Todos los endpoints que escriben lo
/// cumplían, porque necesitan el contexto del usuario para actuar.
///
/// Los de **lectura** no, porque no lo necesitan para responder: el muro, los Kudos recibidos, el
/// detalle y las réplicas devolvían datos con solo presentar un token válido. Eso abría dos
/// huecos reales:
///
///  - Cualquiera que consiga autenticarse en el proveedor de identidad —con inicio de sesión
///    social, es cualquier persona con una cuenta de Google— podía leer todos los reconocimientos de
///    la universidad sin tener cuenta en la plataforma. Autenticarse no es tener cuenta.
///  - Una cuenta desactivada, pendiente o rechazada seguía leyendo, contra lo que el propio
///    código de <see cref="ContextoUsuario"/> afirma.
///
/// Resolver el contexto cuesta una consulta de una fila por índice, el mismo coste que ya pagan las
/// operaciones de escritura. Para DRV-002 (carga dominada por lectura) es un precio aceptado
/// a cambio de que la regla se cumpla en todas partes.
///
/// **Solo se aplica a quien ya está autenticado.** Una petición sin identidad la rechaza el grupo
/// con 401 antes de llegar aquí; y cuando la API arranca sin proveedor configurado —el modo de
/// desarrollo documentado en <c>Program.cs</c>— las rutas siguen siendo anónimas, que es el
/// comportamiento que ese modo promete.
/// </summary>
public static class ExigirCuentaActivaExtensiones
{
    public static TConstructor ExigirCuentaActiva<TConstructor>(this TConstructor constructor)
        where TConstructor : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(constructor);

        return constructor.AddEndpointFilter(async (contexto, siguiente) =>
        {
            var http = contexto.HttpContext;

            if (http.User.Identity?.IsAuthenticated == true)
            {
                // Lanza ExcepcionAutorizacion si no hay cuenta, no está activa o no tiene rol
                // vigente; el manejador de excepciones la traduce a un 403 con el motivo.
                await ContextoUsuario.ResolverAsync(
                    http,
                    http.RequestServices.GetRequiredService<IPersonaRepositorio>(),
                    http.RequestAborted);
            }

            return await siguiente(contexto);
        });
    }
}
