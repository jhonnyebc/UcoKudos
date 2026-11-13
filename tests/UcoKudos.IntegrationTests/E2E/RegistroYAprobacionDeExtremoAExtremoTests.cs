using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UcoKudos.Application.Identidad.Entrada.Dto;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Identidad;
using UcoKudos.Infrastructure.Notificaciones;
using UcoKudos.IntegrationTests.Infraestructura;

namespace UcoKudos.IntegrationTests.E2E;

/// <summary>
/// El registro de una cuenta y su aprobación (RF-01 a RF-03), por HTTP y de punta a punta:
/// solicitar la cuenta, verificar el correo y el celular con los códigos recibidos, y que el
/// aprobador que corresponde la apruebe.
///
/// La regla que se defiende (RF-02): la cuenta **solo** queda activa con las tres condiciones a la
/// vez —solicitud aprobada, correo verificado y celular verificado—, en cualquier orden.
///
/// Los códigos se leen del correo simulado, que es por donde salen: así la prueba recorre el mismo
/// camino que una persona de verdad, en lugar de asomarse a la base.
/// </summary>
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Category", "E2E")]
public sealed partial class RegistroYAprobacionDeExtremoAExtremoTests(BaseDeDatosFixture baseDatos)
    : EscenarioDeExtremoAExtremo(baseDatos)
{
    private const string CorreoNuevo = "laura.perez@uco.edu.co";

    [GeneratedRegex(@"\b(\d{6})\b")]
    private static partial Regex SeisDigitos();

    private async Task<(Guid FacultadId, Guid ProgramaId)> AmbitoAsync()
    {
        await using var contexto = BaseDatos.CrearContexto();
        return (await contexto.Facultades.Select(f => f.Id).FirstAsync(),
                await contexto.Programas.Select(p => p.Id).FirstAsync());
    }

    private async Task<HttpResponseMessage> RegistrarAsync(string correo = CorreoNuevo, Rol? rol = null)
    {
        var (facultadId, programaId) = await AmbitoAsync();
        using var anonimo = Como();

        return await anonimo.PostAsJsonAsync(
            "/api/identidad/registro",
            new RegistrarCuentaDto(
                correo, "+573001234567", "Laura Pérez", (rol ?? Rol.Estudiante).Id, facultadId, programaId));
    }

    private EnviadorCorreoSimulado Correo => Web.Services.GetRequiredService<EnviadorCorreoSimulado>();

    /// <summary>El código que llegó por correo, según de qué se trate.</summary>
    private string CodigoRecibido(string asuntoContiene, string destino = CorreoNuevo)
    {
        var mensaje = Correo.Enviados.Last(e => e.Destino == destino && e.Asunto.Contains(asuntoContiene, StringComparison.Ordinal));
        return SeisDigitos().Match(mensaje.Texto).Groups[1].Value;
    }

    private string CodigoDeCorreo(string destino = CorreoNuevo) => CodigoRecibido("Verifica tu correo", destino);

    private string CodigoDeCelular(string destino = CorreoNuevo) => CodigoRecibido("Código de verificación", destino);

    private static async Task<HttpResponseMessage> VerificarAsync(HttpClient cliente, string tipo, string codigo, string correo = CorreoNuevo) =>
        await cliente.PostAsJsonAsync("/api/identidad/verificar", new VerificarContactoDto(correo, tipo, codigo));

    private async Task<string> DetalleAsync(HttpResponseMessage respuesta)
    {
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        return cuerpo.RootElement.GetProperty("detail").GetString()!;
    }

    private async Task<Persona> PersonaAsync(string correo = CorreoNuevo)
    {
        await using var contexto = BaseDatos.CrearContexto();
        return await contexto.Personas.AsNoTracking().SingleAsync(p => p.CorreoInstitucional == correo);
    }

    [Fact]
    public async Task Una_cuenta_nueva_se_registra_se_verifica_y_se_aprueba_hasta_poder_entrar()
    {
        var registro = await RegistrarAsync();
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);

        // Recién registrada: pendiente, y no entra.
        using var laura = Como(CorreoNuevo);
        Assert.Equal(HttpStatusCode.Forbidden, (await laura.GetAsync("/api/kudos")).StatusCode);

        // Los códigos salen por la bandeja, no en la respuesta del registro.
        Assert.Empty(Correo.Enviados);
        await DespacharAsync();

        using var anonimo = Como();
        var tras = await VerificarAsync(anonimo, "correo", CodigoDeCorreo());
        Assert.Equal(HttpStatusCode.OK, tras.StatusCode);

        var estado = (await (await VerificarAsync(anonimo, "celular", CodigoDeCelular()))
            .Content.ReadFromJsonAsync<EstadoRegistroDto>(Json))!;

        // Con las dos verificaciones, todavía falta la aprobación: la cuenta no está activa.
        Assert.True(estado.CorreoVerificado);
        Assert.True(estado.CelularVerificado);
        Assert.False(estado.SolicitudAprobada);
        Assert.Equal("Pendiente", estado.EstadoCuenta);
        Assert.Equal(HttpStatusCode.Forbidden, (await laura.GetAsync("/api/kudos")).StatusCode);

        // Su aprobador —el Coordinador de Programa— la ve entre las pendientes y la aprueba.
        using var coordinador = Como(CorreoCoordinadorPrograma);
        var pendientes = (await coordinador.GetFromJsonAsync<List<PersonaDto>>("/api/identidad/solicitudes/pendientes", Json))!;
        var solicitud = Assert.Single(pendientes);
        Assert.Equal(CorreoNuevo, solicitud.CorreoInstitucional);
        Assert.Equal("Estudiante", solicitud.Rol);

        var aprobada = await coordinador.PostAsync($"/api/identidad/solicitudes/{solicitud.Id}/aprobar", null);
        Assert.Equal(HttpStatusCode.NoContent, aprobada.StatusCode);

        // Ahora sí: las tres condiciones, y entra.
        Assert.Equal(HttpStatusCode.OK, (await laura.GetAsync("/api/kudos")).StatusCode);
        Assert.Equal(EstadoCuenta.Activa, (await PersonaAsync()).Estado);

        // Y quedó rastro de cada paso.
        await using var contexto = BaseDatos.CrearContexto();
        var acciones = await contexto.EventosAuditoria.Select(e => e.Accion).ToListAsync();
        Assert.Contains(AccionesAuditoria.SolicitudPresentar, acciones);
        Assert.Equal(2, acciones.Count(a => a == AccionesAuditoria.ContactoVerificar));
        Assert.Contains(AccionesAuditoria.SolicitudAprobar, acciones);
    }

    /// <summary>RF-02 no fija un orden: aprobada primero, verificada después, la cuenta también se activa.</summary>
    [Fact]
    public async Task Aprobar_antes_de_verificar_no_activa_la_cuenta_hasta_completar_las_verificaciones()
    {
        await RegistrarAsync();
        await DespacharAsync();

        using var coordinador = Como(CorreoCoordinadorPrograma);
        var solicitud = Assert.Single(
            (await coordinador.GetFromJsonAsync<List<PersonaDto>>("/api/identidad/solicitudes/pendientes", Json))!);

        Assert.Equal(HttpStatusCode.NoContent,
            (await coordinador.PostAsync($"/api/identidad/solicitudes/{solicitud.Id}/aprobar", null)).StatusCode);

        using var laura = Como(CorreoNuevo);
        Assert.Equal(HttpStatusCode.Forbidden, (await laura.GetAsync("/api/kudos")).StatusCode);

        using var anonimo = Como();
        await VerificarAsync(anonimo, "correo", CodigoDeCorreo());
        Assert.Equal(HttpStatusCode.Forbidden, (await laura.GetAsync("/api/kudos")).StatusCode);

        await VerificarAsync(anonimo, "celular", CodigoDeCelular());
        Assert.Equal(HttpStatusCode.OK, (await laura.GetAsync("/api/kudos")).StatusCode);
    }

    [Fact]
    public async Task Un_codigo_incorrecto_se_rechaza_y_al_agotar_los_intentos_ni_el_correcto_sirve()
    {
        await RegistrarAsync();
        await DespacharAsync();
        var correcto = CodigoDeCorreo();
        var equivocado = correcto == "000000" ? "111111" : "000000";

        using var anonimo = Como();

        for (var i = 0; i < VerificacionContacto.IntentosMaximos; i++)
        {
            var intento = await VerificarAsync(anonimo, "correo", equivocado);
            Assert.Equal(HttpStatusCode.BadRequest, intento.StatusCode);
            Assert.Contains("no es correcto", await DetalleAsync(intento), StringComparison.Ordinal);
        }

        // Los intentos fallidos se contaron aunque cada petición terminara en error: sin eso el
        // límite no limitaría nada. Con cinco gastados, ni el código bueno entra.
        var tarde = await VerificarAsync(anonimo, "correo", correcto);
        Assert.Equal(HttpStatusCode.BadRequest, tarde.StatusCode);
        Assert.Contains("vigente", await DetalleAsync(tarde), StringComparison.Ordinal);
        Assert.False((await PersonaAsync()).CorreoVerificado);
    }

    [Fact]
    public async Task Un_codigo_nuevo_invalida_el_anterior()
    {
        await RegistrarAsync();
        await DespacharAsync();
        var viejo = CodigoDeCorreo();

        // El reenvío espera un minuto entre códigos: se adelanta el reloj envejeciendo el código.
        await using (var contexto = BaseDatos.CrearContexto())
        {
            await contexto.Database.ExecuteSqlRawAsync(
                "UPDATE nucleo.verificacion_contacto SET creada_en = creada_en - interval '5 minutes'");
        }

        using var anonimo = Como();
        Assert.Equal(HttpStatusCode.NoContent,
            (await anonimo.PostAsJsonAsync("/api/identidad/reenviar-codigos", new ReenviarCodigosDto(CorreoNuevo))).StatusCode);
        await DespacharAsync();

        var nuevo = CodigoDeCorreo();
        Assert.NotEqual(viejo, nuevo);

        Assert.Equal(HttpStatusCode.BadRequest, (await VerificarAsync(anonimo, "correo", viejo)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await VerificarAsync(anonimo, "correo", nuevo)).StatusCode);
    }

    [Fact]
    public async Task Reenviar_dos_veces_seguidas_no_manda_dos_juegos_de_codigos()
    {
        await RegistrarAsync();
        await DespacharAsync();
        var enviadosAlRegistrar = Correo.Enviados.Count;

        using var anonimo = Como();
        await anonimo.PostAsJsonAsync("/api/identidad/reenviar-codigos", new ReenviarCodigosDto(CorreoNuevo));
        await DespacharAsync();

        Assert.Equal(enviadosAlRegistrar, Correo.Enviados.Count);
    }

    /// <summary>
    /// Los dos endpoints públicos no sirven de buscador de correos: una cuenta que no existe da la
    /// misma respuesta que una sin código vigente, y reenviar responde 204 igual exista o no.
    /// </summary>
    [Fact]
    public async Task Los_endpoints_publicos_no_revelan_si_una_cuenta_existe()
    {
        await RegistrarAsync();
        using var anonimo = Como();

        var inexistente = await VerificarAsync(anonimo, "correo", "123456", "nadie@uco.edu.co");
        var sinCodigoVigente = await VerificarAsync(anonimo, "correo", "123456", CorreoDocente);

        Assert.Equal(HttpStatusCode.BadRequest, inexistente.StatusCode);
        Assert.Equal(await DetalleAsync(sinCodigoVigente), await DetalleAsync(inexistente));

        Assert.Equal(HttpStatusCode.NoContent,
            (await anonimo.PostAsJsonAsync("/api/identidad/reenviar-codigos", new ReenviarCodigosDto("nadie@uco.edu.co"))).StatusCode);
    }

    [Fact]
    public async Task Quien_no_es_el_aprobador_que_corresponde_no_puede_aprobar_y_el_intento_queda_auditado()
    {
        await RegistrarAsync();
        var solicitudId = (await PersonaAsync()).Id;

        // Un docente no aprueba estudiantes: eso es del Coordinador de Programa.
        using var docente = Como(CorreoDocente);
        var intento = await docente.PostAsync($"/api/identidad/solicitudes/{solicitudId}/aprobar", null);

        Assert.Equal(HttpStatusCode.Forbidden, intento.StatusCode);
        Assert.False((await PersonaAsync()).SolicitudAprobada);

        await using var contexto = BaseDatos.CrearContexto();
        Assert.Contains(AccionesAuditoria.AccesoRechazado,
            await contexto.EventosAuditoria.Select(e => e.Accion).ToListAsync());

        // Y a un docente no le salen solicitudes ajenas en su lista.
        Assert.Empty((await docente.GetFromJsonAsync<List<PersonaDto>>("/api/identidad/solicitudes/pendientes", Json))!);
    }

    [Fact]
    public async Task Una_solicitud_rechazada_queda_rechazada_y_avisa_el_motivo_por_correo()
    {
        await RegistrarAsync();
        await DespacharAsync();
        var solicitudId = (await PersonaAsync()).Id;

        using var coordinador = Como(CorreoCoordinadorPrograma);
        var rechazo = await coordinador.PostAsJsonAsync(
            $"/api/identidad/solicitudes/{solicitudId}/rechazar", new RechazarSolicitudDto("No encontramos tu matrícula."));
        Assert.Equal(HttpStatusCode.NoContent, rechazo.StatusCode);

        await DespacharAsync();

        Assert.Equal(EstadoCuenta.Rechazada, (await PersonaAsync()).Estado);

        var aviso = Correo.Enviados.Last(e => e.Destino == CorreoNuevo);
        Assert.Contains("No encontramos tu matrícula.", aviso.Texto, StringComparison.Ordinal);

        // Una rechazada no vuelve a aparecer entre las pendientes ni se puede aprobar después.
        Assert.Empty((await coordinador.GetFromJsonAsync<List<PersonaDto>>("/api/identidad/solicitudes/pendientes", Json))!);
    }

    [Fact]
    public async Task Registrar_un_correo_que_ya_existe_se_rechaza()
    {
        var repetido = await RegistrarAsync(CorreoDocente);

        Assert.Equal(HttpStatusCode.BadRequest, repetido.StatusCode);
    }

    [Fact]
    public async Task El_registro_no_guarda_el_codigo_sino_su_huella()
    {
        await RegistrarAsync();
        await DespacharAsync();
        var codigo = CodigoDeCorreo();

        await using var contexto = BaseDatos.CrearContexto();
        var huellas = await contexto.VerificacionesContacto.Select(v => v.HuellaCodigo).ToListAsync();

        Assert.NotEmpty(huellas);
        Assert.DoesNotContain(codigo, huellas);
    }
}
