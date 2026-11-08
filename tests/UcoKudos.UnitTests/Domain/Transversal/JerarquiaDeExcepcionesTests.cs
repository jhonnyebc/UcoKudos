using UcoKudos.Domain.Transversal;

namespace UcoKudos.UnitTests.Domain.Transversal;

/// <summary>
/// La capa de interfaces traduce los errores de negocio a respuestas HTTP capturando
/// <see cref="ExcepcionDominio"/> (ADR-0001): el dominio no conoce códigos de estado.
///
/// Eso convierte la jerarquía en un contrato, no en un detalle: si una excepción especializada
/// dejara de heredar de <see cref="ExcepcionDominio"/>, el manejador no la capturaría y un error
/// de negocio perfectamente previsto saldría como un 500. Estas pruebas fijan ese contrato.
/// </summary>
public sealed class JerarquiaDeExcepcionesTests
{
    [Fact]
    public void El_conflicto_de_concurrencia_lo_captura_el_manejador_de_errores_de_dominio()
    {
        var error = new ExcepcionConflictoConcurrencia("otra operación ganó la carrera");

        Assert.IsAssignableFrom<ExcepcionDominio>(error);
    }

    [Fact]
    public void El_error_de_autorizacion_lo_captura_el_manejador_de_errores_de_dominio()
    {
        var error = new ExcepcionAutorizacion("un Coordinador de Programa no administra categorías");

        Assert.IsAssignableFrom<ExcepcionDominio>(error);
    }

    [Fact]
    public void Las_excepciones_conservan_el_mensaje_para_poder_explicarle_el_error_al_usuario()
    {
        const string mensaje = "ya existe un Rector activo";

        Assert.Equal(mensaje, new ExcepcionDominio(mensaje).Message);
        Assert.Equal(mensaje, new ExcepcionConflictoConcurrencia(mensaje).Message);
        Assert.Equal(mensaje, new ExcepcionAutorizacion(mensaje).Message);
    }
}
