using NSubstitute;
using UcoKudos.Application.Comun;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Application.Comun.Salida.Puertos;
using UcoKudos.Application.Kudos.CasosUso;
using UcoKudos.Application.Kudos.Entrada.Dto;
using UcoKudos.Application.Notificaciones.Salida.Puertos;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Auditoria.Repositorios;
using UcoKudos.Domain.Identidad;
using UcoKudos.Domain.Kudos;
using UcoKudos.Domain.Kudos.Repositorios;
using UcoKudos.Domain.Transversal;

namespace UcoKudos.UnitTests.Application.Kudos;

/// <summary>
/// Los adjuntos (RF-08) a nivel de caso de uso, sin base ni almacenamiento: quién puede subir, qué se
/// rechaza, cómo se acota el tamaño mientras se lee y qué se audita. El recorrido completo por HTTP
/// está en las pruebas de punta a punta; estas fijan las decisiones una por una.
/// </summary>
public sealed class AdjuntosCasosUsoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Autor = Guid.NewGuid();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private readonly IKudoRepositorio _kudos = Substitute.For<IKudoRepositorio>();
    private readonly IAlmacenObjetos _almacen = Substitute.For<IAlmacenObjetos>();
    private readonly IUnidadTrabajo _unidad = Substitute.For<IUnidadTrabajo>();
    private readonly IEventoAuditoriaRepositorio _eventos = Substitute.For<IEventoAuditoriaRepositorio>();

    private AdjuntosCasosUso CasoDeUso => new(_kudos, _almacen, _unidad, new ServicioAuditoria(_eventos), new RelojFijo());

    private static ContextoPeticion Como(Guid personaId) =>
        new(new AmbitoUsuario(personaId, Rol.Docente, null, null), "traza");

    private Kudo UnKudo()
    {
        var kudo = new Kudo(Autor, Guid.NewGuid(), Guid.NewGuid(), "Gracias por todo.", Ahora);
        _kudos.PorIdAsync(kudo.Id, Arg.Any<CancellationToken>()).Returns(kudo);
        return kudo;
    }

    private static IntencionAdjuntoDto Intencion(string tipo = "image/png", long tamano = 100) =>
        new("foto.png", tipo, tamano);

    private static Adjunto Reservar(Kudo kudo) => kudo.AgregarAdjunto("kudos/clave", "foto.png", Autor, Ahora);

    private static Adjunto Confirmado(Kudo kudo)
    {
        var adjunto = Reservar(kudo);
        adjunto.Confirmar("image/png", Png.Length, "abc123");
        return adjunto;
    }

    // ---------------------------------------------------------------- intención

    [Fact]
    public async Task Reservar_un_adjunto_lo_agrega_con_una_clave_generada_y_no_con_el_nombre_del_usuario()
    {
        var kudo = UnKudo();

        var resultado = await CasoDeUso.PrepararSubidaAsync(Como(Autor), kudo.Id, Intencion(), CancellationToken.None);

        var adjunto = Assert.Single(kudo.Adjuntos);
        Assert.Equal(adjunto.Id, resultado.AdjuntoId);
        Assert.StartsWith("kudos/" + kudo.Id.ToString("N") + "/", adjunto.ClaveObjeto, StringComparison.Ordinal);
        Assert.DoesNotContain("foto", adjunto.ClaveObjeto, StringComparison.Ordinal);
        await _unidad.Received(1).ConfirmarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Solo_el_autor_reserva_adjuntos()
    {
        var kudo = UnKudo();

        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() =>
            CasoDeUso.PrepararSubidaAsync(Como(Guid.NewGuid()), kudo.Id, Intencion(), CancellationToken.None));

        Assert.Empty(kudo.Adjuntos);
    }

    [Theory]
    [InlineData("application/x-msdownload", 100)]
    [InlineData("image/gif", 100)]
    [InlineData("image/png", 5 * 1024 * 1024 + 1)]
    public async Task Se_rechaza_pronto_un_tipo_no_permitido_o_un_tamano_declarado_excesivo(string tipo, long tamano)
    {
        var kudo = UnKudo();

        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.PrepararSubidaAsync(Como(Autor), kudo.Id, Intencion(tipo, tamano), CancellationToken.None));

        Assert.Empty(kudo.Adjuntos);
    }

    [Fact]
    public async Task No_se_reserva_en_un_kudo_que_no_existe()
    {
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.PrepararSubidaAsync(Como(Autor), Guid.NewGuid(), Intencion(), CancellationToken.None));
    }

    [Fact]
    public async Task El_sexto_adjunto_se_rechaza()
    {
        var kudo = UnKudo();
        for (var i = 0; i < Adjunto.MaximoPorKudo; i++)
            Reservar(kudo);

        var error = await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.PrepararSubidaAsync(Como(Autor), kudo.Id, Intencion(), CancellationToken.None));

        Assert.Contains(Adjunto.MaximoPorKudo.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- contenido

    [Fact]
    public async Task El_contenido_se_guarda_en_el_almacenamiento_bajo_la_clave_del_adjunto()
    {
        var kudo = UnKudo();
        var adjunto = Reservar(kudo);

        await CasoDeUso.GuardarContenidoAsync(
            Como(Autor), kudo.Id, adjunto.Id, new MemoryStream(Png), CancellationToken.None);

        await _almacen.Received(1).GuardarAsync(
            adjunto.ClaveObjeto, Arg.Is<byte[]>(b => b.SequenceEqual(Png)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Solo_el_autor_sube_el_contenido()
    {
        var kudo = UnKudo();
        var adjunto = Reservar(kudo);

        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() => CasoDeUso.GuardarContenidoAsync(
            Como(Guid.NewGuid()), kudo.Id, adjunto.Id, new MemoryStream(Png), CancellationToken.None));

        await _almacen.DidNotReceive().GuardarAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_se_sube_contenido_a_un_kudo_ni_a_un_adjunto_que_no_existen()
    {
        await Assert.ThrowsAsync<ExcepcionDominio>(() => CasoDeUso.GuardarContenidoAsync(
            Como(Autor), Guid.NewGuid(), Guid.NewGuid(), new MemoryStream(Png), CancellationToken.None));

        var kudo = UnKudo();

        await Assert.ThrowsAsync<ExcepcionDominio>(() => CasoDeUso.GuardarContenidoAsync(
            Como(Autor), kudo.Id, Guid.NewGuid(), new MemoryStream(Png), CancellationToken.None));
    }

    [Fact]
    public async Task El_contenido_de_un_adjunto_confirmado_no_se_reemplaza()
    {
        var kudo = UnKudo();
        var adjunto = Confirmado(kudo);

        var error = await Assert.ThrowsAsync<ExcepcionDominio>(() => CasoDeUso.GuardarContenidoAsync(
            Como(Autor), kudo.Id, adjunto.Id, new MemoryStream(Png), CancellationToken.None));

        Assert.Contains("confirmado", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_cuerpo_vacio_se_rechaza()
    {
        var kudo = UnKudo();
        var adjunto = Reservar(kudo);

        var error = await Assert.ThrowsAsync<ExcepcionDominio>(() => CasoDeUso.GuardarContenidoAsync(
            Como(Autor), kudo.Id, adjunto.Id, new MemoryStream([]), CancellationToken.None));

        Assert.Contains("vacío", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// El tamaño se acota mientras se lee: un cuerpo que pasa del máximo se corta sin leerlo entero,
    /// aunque quien lo manda haya declarado 100 bytes.
    /// </summary>
    [Fact]
    public async Task Un_cuerpo_que_pasa_del_maximo_se_rechaza_y_no_llega_al_almacenamiento()
    {
        var kudo = UnKudo();
        var adjunto = Reservar(kudo);
        var demasiado = new byte[Adjunto.TamanoMaximoBytes + 1];

        var error = await Assert.ThrowsAsync<ExcepcionDominio>(() => CasoDeUso.GuardarContenidoAsync(
            Como(Autor), kudo.Id, adjunto.Id, new MemoryStream(demasiado), CancellationToken.None));

        Assert.Contains("supera el máximo", error.Message, StringComparison.Ordinal);
        await _almacen.DidNotReceive().GuardarAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_cuerpo_justo_en_el_maximo_se_acepta()
    {
        var kudo = UnKudo();
        var adjunto = Reservar(kudo);

        await CasoDeUso.GuardarContenidoAsync(
            Como(Autor), kudo.Id, adjunto.Id, new MemoryStream(new byte[Adjunto.TamanoMaximoBytes]), CancellationToken.None);

        await _almacen.Received(1).GuardarAsync(adjunto.ClaveObjeto, Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------- confirmación

    [Fact]
    public async Task Confirmar_registra_el_tipo_real_el_tamano_y_el_hash_y_audita_la_carga()
    {
        var kudo = UnKudo();
        var adjunto = Reservar(kudo);
        _almacen.InspeccionarAsync(adjunto.ClaveObjeto, Arg.Any<CancellationToken>())
            .Returns(new DatosObjeto("image/png", Png.Length, "abc123"));

        await CasoDeUso.ConfirmarSubidaAsync(Como(Autor), kudo.Id, adjunto.Id, CancellationToken.None);

        Assert.True(adjunto.Confirmado);
        Assert.Equal("image/png", adjunto.TipoDetectado);
        Assert.Equal("abc123", adjunto.HashContenido);
        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.AdjuntoCargar));
        await _unidad.Received(1).ConfirmarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirmar_rechaza_un_archivo_cuyo_tipo_real_no_esta_permitido_aunque_la_extension_diga_otra_cosa()
    {
        var kudo = UnKudo();
        var adjunto = Reservar(kudo);
        _almacen.InspeccionarAsync(adjunto.ClaveObjeto, Arg.Any<CancellationToken>())
            .Returns(new DatosObjeto("application/octet-stream", 8, "abc123"));

        var error = await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.ConfirmarSubidaAsync(Como(Autor), kudo.Id, adjunto.Id, CancellationToken.None));

        Assert.Contains("no está permitido", error.Message, StringComparison.Ordinal);
        Assert.False(adjunto.Confirmado);
    }

    [Fact]
    public async Task Confirmar_sin_que_el_archivo_haya_llegado_se_rechaza()
    {
        var kudo = UnKudo();
        var adjunto = Reservar(kudo);

        var error = await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.ConfirmarSubidaAsync(Como(Autor), kudo.Id, adjunto.Id, CancellationToken.None));

        Assert.Contains("no llegó", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Solo_el_autor_confirma_y_el_adjunto_tiene_que_existir()
    {
        var kudo = UnKudo();
        var adjunto = Reservar(kudo);

        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() =>
            CasoDeUso.ConfirmarSubidaAsync(Como(Guid.NewGuid()), kudo.Id, adjunto.Id, CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.ConfirmarSubidaAsync(Como(Autor), kudo.Id, Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.ConfirmarSubidaAsync(Como(Autor), Guid.NewGuid(), adjunto.Id, CancellationToken.None));
    }

    // ---------------------------------------------------------------- descarga

    [Fact]
    public async Task Descargar_entrega_el_contenido_con_el_tipo_detectado_y_audita_quien_lo_vio()
    {
        var kudo = UnKudo();
        var adjunto = Confirmado(kudo);
        var quienMira = Guid.NewGuid();
        _almacen.LeerAsync(adjunto.ClaveObjeto, Arg.Any<CancellationToken>()).Returns(Png);

        var archivo = await CasoDeUso.DescargarAsync(Como(quienMira), kudo.Id, adjunto.Id, CancellationToken.None);

        Assert.Equal(Png, archivo.Contenido);
        Assert.Equal("image/png", archivo.Tipo);
        Assert.Equal("foto.png", archivo.NombreOriginal);
        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(
            e => e.Accion == AccionesAuditoria.AdjuntoDescargar && e.ActorId == quienMira));
    }

    [Fact]
    public async Task No_se_descarga_un_adjunto_sin_confirmar_ni_eliminado()
    {
        var kudo = UnKudo();
        var sinConfirmar = Reservar(kudo);
        var eliminado = Confirmado(kudo);
        kudo.QuitarAdjunto(eliminado.Id);

        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.DescargarAsync(Como(Autor), kudo.Id, sinConfirmar.Id, CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.DescargarAsync(Como(Autor), kudo.Id, eliminado.Id, CancellationToken.None));
    }

    [Fact]
    public async Task No_se_descarga_de_un_kudo_vetado()
    {
        var kudo = UnKudo();
        var adjunto = Confirmado(kudo);
        kudo.Vetar();

        var error = await Assert.ThrowsAsync<ExcepcionAutorizacion>(() =>
            CasoDeUso.DescargarAsync(Como(Autor), kudo.Id, adjunto.Id, CancellationToken.None));

        Assert.Contains("moderación", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Descargar_dice_con_claridad_si_el_archivo_falta_en_el_almacenamiento()
    {
        var kudo = UnKudo();
        var adjunto = Confirmado(kudo);

        // Se dice «nulo» a propósito: por omisión el doble devuelve un arreglo vacío, no «no existe».
        _almacen.LeerAsync(adjunto.ClaveObjeto, Arg.Any<CancellationToken>()).Returns((byte[]?)null);

        var error = await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.DescargarAsync(Como(Autor), kudo.Id, adjunto.Id, CancellationToken.None));

        Assert.Contains("no está disponible", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Descargar_de_un_kudo_que_no_existe_se_rechaza()
    {
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.DescargarAsync(Como(Autor), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    // --------------------------------------------------------- eliminar y listar

    [Fact]
    public async Task El_autor_elimina_su_adjunto_y_queda_auditado()
    {
        var kudo = UnKudo();
        var adjunto = Confirmado(kudo);

        await CasoDeUso.EliminarAsync(Como(Autor), kudo.Id, adjunto.Id, CancellationToken.None);

        Assert.True(adjunto.Eliminado);
        _eventos.Received(1).Agregar(Arg.Is<EventoAuditoria>(e => e.Accion == AccionesAuditoria.AdjuntoEliminar));
    }

    [Fact]
    public async Task Solo_el_autor_elimina_y_el_kudo_tiene_que_existir()
    {
        var kudo = UnKudo();
        var adjunto = Confirmado(kudo);

        await Assert.ThrowsAsync<ExcepcionAutorizacion>(() =>
            CasoDeUso.EliminarAsync(Como(Guid.NewGuid()), kudo.Id, adjunto.Id, CancellationToken.None));
        await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            CasoDeUso.EliminarAsync(Como(Autor), Guid.NewGuid(), adjunto.Id, CancellationToken.None));

        Assert.False(adjunto.Eliminado);
    }

    [Fact]
    public async Task Listar_da_solo_metadatos_de_los_adjuntos_servibles_y_nada_de_enlaces()
    {
        var kudo = UnKudo();
        var servible = Confirmado(kudo);
        Reservar(kudo);

        var lista = await CasoDeUso.DeKudoAsync(kudo.Id, CancellationToken.None);

        var fila = Assert.Single(lista);
        Assert.Equal(servible.Id, fila.Id);
        Assert.Equal("image/png", fila.TipoDetectado);
        Assert.Equal(Png.Length, fila.TamanoBytes);

        // Listar no es acceder: no deja rastro de auditoría.
        _eventos.DidNotReceive().Agregar(Arg.Any<EventoAuditoria>());
    }

    [Fact]
    public async Task Listar_los_adjuntos_de_un_kudo_que_no_existe_da_una_lista_vacia()
    {
        Assert.Empty(await CasoDeUso.DeKudoAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
