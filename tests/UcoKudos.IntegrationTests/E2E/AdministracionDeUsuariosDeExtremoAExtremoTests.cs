using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using UcoKudos.Application.Academico.Entrada.Dto;
using UcoKudos.Application.Identidad.Entrada.Dto;
using UcoKudos.Domain.Academico;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Identidad;
using UcoKudos.IntegrationTests.Infraestructura;

namespace UcoKudos.IntegrationTests.E2E;

/// <summary>
/// Administración de usuarios, por HTTP y contra PostgreSQL real: desactivar y reactivar cuentas y
/// cambiar roles.
///
/// Quién administra a quién es la cadena del enunciado, literal: el Coordinador de Programa a los
/// estudiantes y docentes de **sus** programas; el de Facultad, a los coordinadores de programa de su
/// facultad; el Académico, a los de facultad; el Rector, a los académicos; el Administrador, a los
/// administradores y rectores. Nadie se administra a sí mismo.
/// </summary>
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Category", "E2E")]
public sealed class AdministracionDeUsuariosDeExtremoAExtremoTests(BaseDeDatosFixture baseDatos)
    : EscenarioDeExtremoAExtremo(baseDatos)
{
    private async Task<(Guid FacultadId, Guid ProgramaId)> AmbitoDeLaSemillaAsync()
    {
        await using var contexto = BaseDatos.CrearContexto();
        return (await contexto.Facultades.Select(f => f.Id).FirstAsync(),
                await contexto.Programas.Select(p => p.Id).FirstAsync());
    }

    private async Task<Guid> IdDeAsync(string correo)
    {
        await using var contexto = BaseDatos.CrearContexto();
        return await contexto.Personas.Where(p => p.CorreoInstitucional == correo).Select(p => p.Id).SingleAsync();
    }

    /// <summary>Otra facultad con un programa, y un estudiante activo en él.</summary>
    private async Task<(Guid FacultadId, Guid ProgramaId, Guid EstudianteId)> OtroProgramaAsync()
    {
        await using var contexto = BaseDatos.CrearContexto();
        var ahora = DateTimeOffset.UtcNow;

        var facultad = new Facultad("Ciencias", "CIE", ahora);
        var programa = new Programa(facultad.Id, "Biología", "BIO", ahora);
        var estudiante = Semillas.Activa(new Persona("estudiante.bio@uco.edu.co", "+573005550001", "Sofía Ríos", ahora));

        contexto.Facultades.Add(facultad);
        contexto.Programas.Add(programa);
        contexto.Personas.Add(estudiante);
        contexto.AsignacionesRol.Add(new AsignacionRol(estudiante.Id, Rol.Estudiante, facultad.Id, programa.Id, ahora));
        await contexto.SaveChangesAsync();

        return (facultad.Id, programa.Id, estudiante.Id);
    }

    private async Task<List<UsuarioAdministradoDto>> AdministradosAsync(string correo, string? busqueda = null)
    {
        using var cliente = Como(correo);
        var ruta = busqueda is null ? "/api/usuarios" : $"/api/usuarios?busqueda={busqueda}";

        return (await cliente.GetFromJsonAsync<List<UsuarioAdministradoDto>>(ruta, Json))!;
    }

    private static Task<HttpResponseMessage> DesactivarAsync(HttpClient cliente, Guid id, string motivo = "Ya no estudia aquí.") =>
        cliente.PostAsJsonAsync($"/api/usuarios/{id}/desactivar", new MotivoDto(motivo));

    // ------------------------------------------------------------------ listado

    [Fact]
    public async Task Cada_rol_ve_a_quienes_administra_segun_la_cadena_del_enunciado()
    {
        var delPrograma = await AdministradosAsync(CorreoCoordinadorPrograma);
        var deLaFacultad = await AdministradosAsync(CorreoCoordinadorFacultad);
        var delAcademico = await AdministradosAsync(CorreoCoordinadorAcademico);
        var delRector = await AdministradosAsync(CorreoRector);
        var delAdministrador = await AdministradosAsync(CorreoAdministrador);

        Assert.Equal(["Ana Gómez", "Carlos Mejía"], delPrograma.Select(u => u.NombreCompleto).Order().ToList());
        Assert.Equal("Coordinador de Sistemas", Assert.Single(deLaFacultad).NombreCompleto);
        Assert.Equal("Decana de Ingeniería", Assert.Single(delAcademico).NombreCompleto);
        Assert.Equal("Carlos Álvarez", Assert.Single(delRector).NombreCompleto);

        // El Administrador administra a administradores y rectores, y no a sí mismo.
        Assert.Equal("Marta Restrepo", Assert.Single(delAdministrador).NombreCompleto);
    }

    [Fact]
    public async Task Un_coordinador_de_programa_no_ve_a_las_personas_de_otro_programa()
    {
        await OtroProgramaAsync();

        var delPrograma = await AdministradosAsync(CorreoCoordinadorPrograma);

        Assert.DoesNotContain(delPrograma, u => u.NombreCompleto == "Sofía Ríos");
        Assert.Equal(2, delPrograma.Count);
    }

    [Fact]
    public async Task Docentes_y_estudiantes_reciben_una_lista_vacia_y_la_busqueda_filtra_por_nombre()
    {
        Assert.Empty(await AdministradosAsync(CorreoDocente));
        Assert.Empty(await AdministradosAsync(CorreoEstudiante));

        var filtrados = await AdministradosAsync(CorreoCoordinadorPrograma, "carl");
        Assert.Equal("Carlos Mejía", Assert.Single(filtrados).NombreCompleto);
        Assert.Empty(await AdministradosAsync(CorreoCoordinadorPrograma, "zzz"));
    }

    // ------------------------------------------------- desactivar y reactivar

    [Fact]
    public async Task Desactivar_una_cuenta_le_quita_el_acceso_al_instante_y_reactivarla_lo_devuelve()
    {
        using var coordinador = Como(CorreoCoordinadorPrograma);
        using var estudiante = Como(CorreoEstudiante);

        Assert.Equal(HttpStatusCode.OK, (await estudiante.GetAsync("/api/kudos")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await DesactivarAsync(coordinador, Datos.DestinatarioId)).StatusCode);

        // En la siguiente petición, sin esperar a que caduque ningún token.
        Assert.Equal(HttpStatusCode.Forbidden, (await estudiante.GetAsync("/api/kudos")).StatusCode);
        Assert.Equal("Inactiva", (await AdministradosAsync(CorreoCoordinadorPrograma)).Single(u => u.Id == Datos.DestinatarioId).EstadoCuenta);

        Assert.Equal(HttpStatusCode.NoContent,
            (await coordinador.PostAsync($"/api/usuarios/{Datos.DestinatarioId}/reactivar", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await estudiante.GetAsync("/api/kudos")).StatusCode);
        Assert.Equal("Activa", (await AdministradosAsync(CorreoCoordinadorPrograma)).Single(u => u.Id == Datos.DestinatarioId).EstadoCuenta);
    }

    [Fact]
    public async Task Desactivar_no_toca_el_rol_ni_los_kudos_y_deja_rastro_con_el_motivo()
    {
        using var coordinador = Como(CorreoCoordinadorPrograma);
        await DesactivarAsync(coordinador, Datos.DestinatarioId, "Retiro voluntario.");

        await using var contexto = BaseDatos.CrearContexto();
        var persona = await contexto.Personas.SingleAsync(p => p.Id == Datos.DestinatarioId);
        Assert.Equal(EstadoCuenta.Inactiva, persona.Estado);
        Assert.Single(await contexto.AsignacionesRol.Where(a => a.PersonaId == persona.Id && a.Activa).ToListAsync());

        var evento = await contexto.EventosAuditoria.SingleAsync(e => e.Accion == AccionesAuditoria.UsuarioDesactivar);
        Assert.Equal(Datos.DestinatarioId, evento.EntidadId);
        Assert.Contains("Retiro voluntario.", evento.Datos, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reactivar_deja_rastro_y_desactivar_dos_veces_o_reactivar_una_activa_se_rechaza()
    {
        using var coordinador = Como(CorreoCoordinadorPrograma);

        // Reactivar una cuenta que está activa.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await coordinador.PostAsync($"/api/usuarios/{Datos.DestinatarioId}/reactivar", null)).StatusCode);

        await DesactivarAsync(coordinador, Datos.DestinatarioId);
        Assert.Equal(HttpStatusCode.BadRequest, (await DesactivarAsync(coordinador, Datos.DestinatarioId)).StatusCode);

        await coordinador.PostAsync($"/api/usuarios/{Datos.DestinatarioId}/reactivar", null);

        await using var contexto = BaseDatos.CrearContexto();
        Assert.Contains(AccionesAuditoria.UsuarioReactivar, await contexto.EventosAuditoria.Select(e => e.Accion).ToListAsync());
    }

    [Fact]
    public async Task El_motivo_es_obligatorio_y_la_persona_tiene_que_existir()
    {
        using var coordinador = Como(CorreoCoordinadorPrograma);

        Assert.Equal(HttpStatusCode.BadRequest, (await DesactivarAsync(coordinador, Datos.DestinatarioId, "   ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await DesactivarAsync(coordinador, Guid.NewGuid())).StatusCode);
    }

    // ------------------------------------------------------------ autorización

    [Fact]
    public async Task Un_coordinador_no_administra_a_las_personas_de_otro_programa_y_el_intento_queda_auditado()
    {
        var (_, _, otroEstudiante) = await OtroProgramaAsync();

        using var coordinador = Como(CorreoCoordinadorPrograma);
        var intento = await DesactivarAsync(coordinador, otroEstudiante);

        Assert.Equal(HttpStatusCode.Forbidden, intento.StatusCode);

        await using var contexto = BaseDatos.CrearContexto();
        Assert.Equal(EstadoCuenta.Activa, (await contexto.Personas.SingleAsync(p => p.Id == otroEstudiante)).Estado);
        Assert.Contains(AccionesAuditoria.AccesoRechazado, await contexto.EventosAuditoria.Select(e => e.Accion).ToListAsync());
    }

    [Theory]
    [InlineData(CorreoCoordinadorFacultad)]
    [InlineData(CorreoCoordinadorAcademico)]
    [InlineData(CorreoRector)]
    [InlineData(CorreoAdministrador)]
    public async Task Nadie_administra_a_quien_no_esta_en_el_escalon_de_abajo(string actor)
    {
        // Un Coordinador de Facultad no administra a un estudiante: eso es del Coordinador de Programa.
        using var cliente = Como(actor);

        Assert.Equal(HttpStatusCode.Forbidden, (await DesactivarAsync(cliente, Datos.DestinatarioId)).StatusCode);
    }

    [Theory]
    [InlineData(CorreoDocente)]
    [InlineData(CorreoEstudiante)]
    public async Task Docentes_y_estudiantes_no_administran_a_nadie(string actor)
    {
        using var cliente = Como(actor);

        Assert.Equal(HttpStatusCode.Forbidden, (await DesactivarAsync(cliente, await IdDeAsync(CorreoCoordinadorPrograma))).StatusCode);
    }

    [Fact]
    public async Task Nadie_se_administra_a_si_mismo_ni_siquiera_el_administrador()
    {
        // Evita que una cuenta se deje a sí misma sin acceso, o que se bloquee el último administrador.
        using var administrador = Como(CorreoAdministrador);

        Assert.Equal(HttpStatusCode.Forbidden, (await DesactivarAsync(administrador, await IdDeAsync(CorreoAdministrador))).StatusCode);
    }

    [Fact]
    public async Task Cada_escalon_administra_al_de_abajo()
    {
        using var facultad = Como(CorreoCoordinadorFacultad);
        using var academico = Como(CorreoCoordinadorAcademico);
        using var rector = Como(CorreoRector);
        using var administrador = Como(CorreoAdministrador);

        Assert.Equal(HttpStatusCode.NoContent, (await DesactivarAsync(facultad, await IdDeAsync(CorreoCoordinadorPrograma))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await DesactivarAsync(academico, await IdDeAsync(CorreoCoordinadorFacultad))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await DesactivarAsync(rector, await IdDeAsync(CorreoCoordinadorAcademico))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await DesactivarAsync(administrador, await IdDeAsync(CorreoRector))).StatusCode);
    }

    [Fact]
    public async Task Sin_identidad_no_se_administra_nada()
    {
        using var anonimo = Como();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/usuarios")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await DesactivarAsync(anonimo, Datos.DestinatarioId)).StatusCode);
    }

    // ------------------------------------------------------------ cambiar rol

    private static Task<HttpResponseMessage> CambiarRolAsync(
        HttpClient cliente, Guid id, Rol rol, Guid? facultad = null, Guid? programa = null, string motivo = "Reorganización.") =>
        cliente.PostAsJsonAsync($"/api/usuarios/{id}/rol", new CambiarRolDto(rol.Id, facultad, programa, motivo));

    [Fact]
    public async Task El_coordinador_de_programa_cambia_a_un_docente_a_estudiante_y_queda_el_historial()
    {
        var (_, programa) = await AmbitoDeLaSemillaAsync();

        using var coordinador = Como(CorreoCoordinadorPrograma);
        var cambio = await CambiarRolAsync(coordinador, Datos.AutorId, Rol.Estudiante, programa: programa);

        Assert.Equal(HttpStatusCode.NoContent, cambio.StatusCode);

        // La persona sigue entrando, ahora como estudiante.
        using var ana = Como(CorreoDocente);
        var ficha = (await ana.GetFromJsonAsync<ProgramasDePersonaDto>("/api/academico/mis-programas", Json))!;
        Assert.Equal("Estudiante", ficha.Rol);

        // Y el rol anterior quedó revocado, no borrado.
        await using var contexto = BaseDatos.CrearContexto();
        var asignaciones = await contexto.AsignacionesRol.Where(a => a.PersonaId == Datos.AutorId).ToListAsync();
        Assert.Equal(2, asignaciones.Count);
        Assert.Single(asignaciones, a => a.Activa);
        Assert.Contains(AccionesAuditoria.UsuarioCambiarRol, await contexto.EventosAuditoria.Select(e => e.Accion).ToListAsync());
    }

    [Fact]
    public async Task No_se_asciende_a_nadie_mas_alla_de_lo_que_uno_mismo_aprueba()
    {
        var (_, programa) = await AmbitoDeLaSemillaAsync();
        var (facultadA, _) = await AmbitoDeLaSemillaAsync();

        // El Coordinador de Programa no aprueba a otro Coordinador de Programa: eso es de la Facultad.
        using var coordinador = Como(CorreoCoordinadorPrograma);
        var ascenso = await CambiarRolAsync(coordinador, Datos.AutorId, Rol.CoordinadorPrograma, facultadA, programa);
        Assert.Equal(HttpStatusCode.Forbidden, ascenso.StatusCode);

        // El Rector no aprueba a un Coordinador de Facultad: eso es del Coordinador Académico.
        using var rector = Como(CorreoRector);
        var enOtraCadena = await CambiarRolAsync(rector, await IdDeAsync(CorreoCoordinadorAcademico), Rol.CoordinadorFacultad, facultadA);
        Assert.Equal(HttpStatusCode.Forbidden, enOtraCadena.StatusCode);

        await using var contexto = BaseDatos.CrearContexto();
        Assert.Single(await contexto.AsignacionesRol.Where(a => a.PersonaId == Datos.AutorId && a.Activa).ToListAsync());
        Assert.Contains(AccionesAuditoria.AccesoRechazado, await contexto.EventosAuditoria.Select(e => e.Accion).ToListAsync());
    }

    [Fact]
    public async Task El_rol_nuevo_tiene_que_caer_dentro_del_ambito_de_quien_lo_asigna()
    {
        var (_, _, _) = await OtroProgramaAsync();
        await using var contexto = BaseDatos.CrearContexto();
        var programaOtro = await contexto.Programas.Where(p => p.Codigo == "BIO").Select(p => p.Id).SingleAsync();

        // El Coordinador del programa de Sistemas no pasa a un docente suyo al programa de Biología.
        using var coordinador = Como(CorreoCoordinadorPrograma);
        var fuera = await CambiarRolAsync(coordinador, Datos.AutorId, Rol.Docente, programa: programaOtro);

        Assert.Equal(HttpStatusCode.Forbidden, fuera.StatusCode);
    }

    [Fact]
    public async Task Cambiar_al_mismo_rol_y_ambito_el_motivo_vacio_o_un_programa_desactivado_se_rechazan()
    {
        var (_, programa) = await AmbitoDeLaSemillaAsync();

        using var coordinador = Como(CorreoCoordinadorPrograma);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await CambiarRolAsync(coordinador, Datos.AutorId, Rol.Docente, programa: programa)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await CambiarRolAsync(coordinador, Datos.AutorId, Rol.Estudiante, programa: programa, motivo: " ")).StatusCode);

        // Sin programa para un rol que lo exige.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await CambiarRolAsync(coordinador, Datos.AutorId, Rol.Estudiante)).StatusCode);

        using var rector = Como(CorreoRector);
        await rector.PostAsync($"/api/academico/programas/{programa}/estado?activo=false", null);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await CambiarRolAsync(coordinador, Datos.AutorId, Rol.Estudiante, programa: programa)).StatusCode);
    }

    [Fact]
    public async Task La_unicidad_se_respeta_al_cambiar_de_rol()
    {
        var (facultad, programaSistemas) = await AmbitoDeLaSemillaAsync();

        // Un segundo programa de la misma facultad, con su propio coordinador.
        using var rector = Como(CorreoRector);
        var creado = await rector.PostAsJsonAsync($"/api/academico/facultades/{facultad}/programas", new DatosProgramaDto("Civil", "CIV"));
        var programaCivil = (await creado.Content.ReadFromJsonAsync<ProgramaAdminDto>(Json))!.Id;

        await using (var contexto = BaseDatos.CrearContexto())
        {
            var ahora = DateTimeOffset.UtcNow;
            var coordinador = Semillas.Activa(new Persona("coordinador.civil@uco.edu.co", "+573005550004", "Coordinador de Civil", ahora));
            contexto.Personas.Add(coordinador);
            contexto.AsignacionesRol.Add(new AsignacionRol(coordinador.Id, Rol.CoordinadorPrograma, facultad, programaCivil, ahora));
            await contexto.SaveChangesAsync();
        }

        // La Decana lo pasa al programa de Sistemas, que ya tiene coordinador: no puede haber dos.
        using var decana = Como(CorreoCoordinadorFacultad);
        var repetido = await CambiarRolAsync(
            decana, await IdDeAsync("coordinador.civil@uco.edu.co"), Rol.CoordinadorPrograma, facultad, programaSistemas);

        Assert.Equal(HttpStatusCode.BadRequest, repetido.StatusCode);

        // Y nada cambió: sigue coordinando Civil.
        await using var lectura = BaseDatos.CrearContexto();
        var activa = await lectura.AsignacionesRol.SingleAsync(
            a => a.PersonaId == lectura.Personas.Where(p => p.CorreoInstitucional == "coordinador.civil@uco.edu.co").Select(p => p.Id).First() && a.Activa);
        Assert.Equal(programaCivil, activa.ProgramaId);
    }

    [Fact]
    public async Task Cambiar_el_rol_de_un_coordinador_de_programa_a_otro_programa_de_su_facultad()
    {
        var (facultad, _) = await AmbitoDeLaSemillaAsync();

        // Un segundo programa en la misma facultad, creado por el Rector.
        using var rector = Como(CorreoRector);
        var creado = await rector.PostAsJsonAsync($"/api/academico/facultades/{facultad}/programas", new DatosProgramaDto("Civil", "CIV"));
        var programaCivil = (await creado.Content.ReadFromJsonAsync<ProgramaAdminDto>(Json))!.Id;

        // La Decana de Ingeniería lo pasa a coordinar el otro programa.
        using var decana = Como(CorreoCoordinadorFacultad);
        var cambio = await CambiarRolAsync(decana, await IdDeAsync(CorreoCoordinadorPrograma), Rol.CoordinadorPrograma, facultad, programaCivil);

        Assert.Equal(HttpStatusCode.NoContent, cambio.StatusCode);

        using var coordinador = Como(CorreoCoordinadorPrograma);
        var ficha = (await coordinador.GetFromJsonAsync<ProgramasDePersonaDto>("/api/academico/mis-programas", Json))!;
        Assert.Equal("Civil", Assert.Single(ficha.Programas).ProgramaNombre);
    }

    [Fact]
    public async Task Un_cambio_de_rol_de_una_cuenta_pendiente_o_inexistente_se_rechaza()
    {
        var (_, programa) = await AmbitoDeLaSemillaAsync();

        using var coordinador = Como(CorreoCoordinadorPrograma);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await CambiarRolAsync(coordinador, Guid.NewGuid(), Rol.Estudiante, programa: programa)).StatusCode);
    }
}
