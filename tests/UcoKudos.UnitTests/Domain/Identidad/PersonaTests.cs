using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.UnitTests.Domain.Identidad;

/// <summary>
/// RF-01 y RF-02. La regla que importa aquí es la de activación compuesta: la cuenta queda activa
/// solo con solicitud aprobada, correo verificado y celular verificado, las tres a la vez.
///
/// Está en un solo lugar del dominio para que no exista forma de activar una cuenta saltándose una
/// condición, y estas pruebas recorren las combinaciones.
/// </summary>
public sealed class PersonaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 8, 17, 39, 0, TimeSpan.Zero);

    private static Persona Nueva() =>
        new("Jhonny.Benitez@uco.edu.co", "+57 300 123 4567", "Jhonny Estiven Benítez Caro", Ahora);

    [Fact]
    public void Una_persona_recien_registrada_queda_pendiente_y_no_puede_acceder()
    {
        var persona = Nueva();

        Assert.Equal(EstadoCuenta.Pendiente, persona.Estado);
        Assert.False(persona.PuedeAcceder);
    }

    [Fact]
    public void El_correo_se_normaliza_a_minusculas_para_que_no_entren_cuentas_duplicadas()
    {
        var persona = Nueva();

        Assert.Equal("jhonny.benitez@uco.edu.co", persona.CorreoInstitucional);
    }

    [Fact]
    public void El_celular_se_normaliza_al_formato_que_exige_la_api_de_whatsapp()
    {
        var persona = Nueva();

        Assert.Equal("+573001234567", persona.CelularE164);
    }

    [Theory]
    [InlineData("sin-arroba")]
    [InlineData("dos@@arrobas.com")]
    [InlineData("sin-punto@dominio")]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_correo_invalido_no_permite_construir_la_persona(string correo)
    {
        Assert.Throws<ExcepcionDominio>(() => new Persona(correo, "+573001234567", "Alguien", Ahora));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234567890123456")]
    [InlineData("")]
    public void Un_celular_invalido_no_permite_construir_la_persona(string celular)
    {
        Assert.Throws<ExcepcionDominio>(() => new Persona("a@uco.edu.co", celular, "Alguien", Ahora));
    }

    [Fact]
    public void Un_nombre_vacio_no_permite_construir_la_persona()
    {
        Assert.Throws<ExcepcionDominio>(() => new Persona("a@uco.edu.co", "+573001234567", "   ", Ahora));
    }

    /// <summary>RF-02: las tres condiciones, y solo con las tres la cuenta se activa.</summary>
    [Fact]
    public void La_cuenta_se_activa_solo_con_las_tres_condiciones()
    {
        var persona = Nueva();

        persona.AprobarSolicitud();
        Assert.Equal(EstadoCuenta.Pendiente, persona.Estado);

        persona.VerificarCorreo();
        Assert.Equal(EstadoCuenta.Pendiente, persona.Estado);

        persona.VerificarCelular();
        Assert.Equal(EstadoCuenta.Activa, persona.Estado);
        Assert.True(persona.PuedeAcceder);
    }

    [Fact]
    public void Verificar_correo_y_celular_sin_aprobacion_no_activa_la_cuenta()
    {
        var persona = Nueva();

        persona.VerificarCorreo();
        persona.VerificarCelular();

        Assert.Equal(EstadoCuenta.Pendiente, persona.Estado);
    }

    [Fact]
    public void Una_solicitud_rechazada_no_se_puede_aprobar_despues()
    {
        var persona = Nueva();
        persona.RechazarSolicitud();

        Assert.Equal(EstadoCuenta.Rechazada, persona.Estado);
        Assert.Throws<ExcepcionDominio>(persona.AprobarSolicitud);
    }

    [Fact]
    public void Desactivar_una_cuenta_activa_le_quita_el_acceso_de_inmediato()
    {
        var persona = Activa();

        persona.Desactivar();

        Assert.Equal(EstadoCuenta.Inactiva, persona.Estado);
        Assert.False(persona.PuedeAcceder);
    }

    [Fact]
    public void Una_cuenta_pendiente_no_se_puede_desactivar()
    {
        var persona = Nueva();

        Assert.Throws<ExcepcionDominio>(persona.Desactivar);
    }

    /// <summary>
    /// Cambiar el celular lo deja sin verificar, así que la cuenta vuelve a pendiente. Es
    /// deliberado: el celular es un canal de aviso (RF-15), y avisar a un número no verificado
    /// sería mandar el Kudo de alguien a un desconocido.
    /// </summary>
    [Fact]
    public void Cambiar_el_celular_lo_deja_sin_verificar_y_la_cuenta_vuelve_a_pendiente()
    {
        var persona = Activa();

        persona.CambiarCelular("+573009999999");

        Assert.False(persona.CelularVerificado);
        Assert.Equal(EstadoCuenta.Pendiente, persona.Estado);
    }

    [Fact]
    public void Cambiar_el_celular_por_el_mismo_numero_no_lo_desverifica()
    {
        var persona = Activa();

        persona.CambiarCelular("+57 300 123 4567");

        Assert.True(persona.CelularVerificado);
        Assert.Equal(EstadoCuenta.Activa, persona.Estado);
    }

    /// <summary>
    /// Los identificadores son ordenables en el tiempo (ADR-0003), así que dos personas creadas en
    /// orden tienen identificadores en orden. Eso es lo que evita que los índices se fragmenten.
    /// </summary>
    [Fact]
    public void Los_identificadores_se_generan_ordenados_en_el_tiempo()
    {
        var primera = new Persona("a@uco.edu.co", "+573001111111", "A", Ahora);
        var segunda = new Persona("b@uco.edu.co", "+573002222222", "B", Ahora.AddSeconds(1));

        Assert.True(primera.Id.CompareTo(segunda.Id) < 0);
    }

    private static Persona Activa()
    {
        var persona = Nueva();
        persona.AprobarSolicitud();
        persona.VerificarCorreo();
        persona.VerificarCelular();
        return persona;
    }
}
