using UcoKudos.Domain.Identidad;

namespace UcoKudos.UnitTests.Domain.Identidad;

/// <summary>El código de verificación (RF-02): vencimiento, límite de intentos y un solo uso.</summary>
public sealed class VerificacionContactoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Persona = Guid.NewGuid();

    private static VerificacionContacto Nueva(string codigo = "123456", TipoContacto tipo = TipoContacto.Correo) =>
        new(Persona, tipo, codigo, Ahora);

    [Fact]
    public void El_codigo_correcto_se_acepta()
    {
        Assert.Equal(ResultadoCodigo.Correcto, Nueva().Verificar("123456", Ahora.AddMinutes(1)));
    }

    [Fact]
    public void Los_espacios_alrededor_del_codigo_no_cuentan()
    {
        Assert.Equal(ResultadoCodigo.Correcto, Nueva().Verificar("  123456 ", Ahora.AddMinutes(1)));
    }

    [Fact]
    public void Un_codigo_incorrecto_se_rechaza_y_cuenta_como_intento()
    {
        var verificacion = Nueva();

        Assert.Equal(ResultadoCodigo.Incorrecto, verificacion.Verificar("000000", Ahora.AddMinutes(1)));
        Assert.Equal(1, verificacion.Intentos);
    }

    [Fact]
    public void Tras_los_intentos_maximos_ni_el_codigo_correcto_sirve()
    {
        var verificacion = Nueva();

        for (var i = 0; i < VerificacionContacto.IntentosMaximos; i++)
            verificacion.Verificar("000000", Ahora.AddMinutes(1));

        Assert.Equal(ResultadoCodigo.Bloqueado, verificacion.Verificar("123456", Ahora.AddMinutes(1)));
    }

    [Fact]
    public void Un_codigo_vencido_se_rechaza_aunque_sea_el_correcto()
    {
        var verificacion = Nueva();

        Assert.Equal(ResultadoCodigo.Vencido, verificacion.Verificar("123456", Ahora + VerificacionContacto.Vigencia));
    }

    [Fact]
    public void Un_codigo_solo_se_puede_usar_una_vez()
    {
        var verificacion = Nueva();

        Assert.Equal(ResultadoCodigo.Correcto, verificacion.Verificar("123456", Ahora.AddMinutes(1)));
        Assert.Equal(ResultadoCodigo.SinCodigo, verificacion.Verificar("123456", Ahora.AddMinutes(2)));
    }

    [Fact]
    public void Un_codigo_reemplazado_ya_no_sirve()
    {
        var verificacion = Nueva();

        verificacion.Reemplazar();

        Assert.Equal(ResultadoCodigo.SinCodigo, verificacion.Verificar("123456", Ahora.AddMinutes(1)));
    }

    [Fact]
    public void Se_guarda_la_huella_y_no_el_codigo()
    {
        var verificacion = Nueva("123456");

        Assert.DoesNotContain("123456", verificacion.HuellaCodigo, StringComparison.Ordinal);
        Assert.Equal(64, verificacion.HuellaCodigo.Length);
    }

    [Fact]
    public void El_mismo_codigo_da_huellas_distintas_para_correo_y_para_celular()
    {
        Assert.NotEqual(
            Nueva("123456", TipoContacto.Correo).HuellaCodigo,
            Nueva("123456", TipoContacto.Celular).HuellaCodigo);
    }
}
