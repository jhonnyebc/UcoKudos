using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using UcoKudos.Application.Academico.Entrada.Dto;
using UcoKudos.Application.Identidad.Entrada.Dto;
using UcoKudos.Domain.Auditoria;
using UcoKudos.Domain.Identidad;
using UcoKudos.IntegrationTests.Infraestructura;

namespace UcoKudos.IntegrationTests.E2E;

/// <summary>
/// Gestión académica (RF-05), por HTTP y de punta a punta.
///
/// Dos cosas que el enunciado pide: **poder conocer** qué programas estudia un estudiante, en cuáles
/// enseña un docente, qué programa acompaña un Coordinador de Programa y qué facultad acompaña uno de
/// Facultad; y que la estructura se pueda administrar sin tocar la base.
///
/// La autorización es la parte delicada: cada coordinador ve y administra a las personas **de sus
/// programas**, no a las de otros.
/// </summary>
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Category", "E2E")]
public sealed class GestionAcademicaDeExtremoAExtremoTests(BaseDeDatosFixture baseDatos)
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

    /// <summary>Crea con el Rector una facultad y un programa nuevos, y devuelve sus identificadores.</summary>
    private async Task<(Guid FacultadId, Guid ProgramaId)> CrearOtroProgramaAsync(string codigo = "BIO")
    {
        using var rector = Como(CorreoRector);

        var facultad = await rector.PostAsJsonAsync("/api/academico/facultades", new DatosFacultadDto("Ciencias", "CIE"));
        Assert.Equal(HttpStatusCode.Created, facultad.StatusCode);
        var facultadId = (await facultad.Content.ReadFromJsonAsync<FacultadAdminDto>(Json))!.Id;

        var programa = await rector.PostAsJsonAsync(
            $"/api/academico/facultades/{facultadId}/programas", new DatosProgramaDto("Biología", codigo));
        Assert.Equal(HttpStatusCode.Created, programa.StatusCode);

        return (facultadId, (await programa.Content.ReadFromJsonAsync<ProgramaAdminDto>(Json))!.Id);
    }

    private async Task<ProgramasDePersonaDto> ProgramasDeAsync(HttpClient cliente, Guid personaId) =>
        (await cliente.GetFromJsonAsync<ProgramasDePersonaDto>($"/api/academico/personas/{personaId}/programas", Json))!;

    // ---------------------------------------------------------------- conocer

    [Fact]
    public async Task Un_estudiante_y_un_docente_ven_el_programa_en_el_que_estan()
    {
        using var estudiante = Como(CorreoEstudiante);
        using var docente = Como(CorreoDocente);

        var del_estudiante = (await estudiante.GetFromJsonAsync<ProgramasDePersonaDto>("/api/academico/mis-programas", Json))!;
        var del_docente = (await docente.GetFromJsonAsync<ProgramasDePersonaDto>("/api/academico/mis-programas", Json))!;

        var programa = Assert.Single(del_estudiante.Programas);
        Assert.Equal("Ingeniería de Sistemas", programa.ProgramaNombre);
        Assert.Equal("Ingenierías", programa.FacultadNombre);
        Assert.Equal("Principal", programa.Relacion);
        Assert.Equal("Estudiante", del_estudiante.Rol);
        Assert.Equal("Docente", del_docente.Rol);
    }

    [Fact]
    public async Task Un_coordinador_de_programa_ve_el_programa_que_acompana_y_uno_de_facultad_la_facultad()
    {
        using var coordinadorPrograma = Como(CorreoCoordinadorPrograma);
        using var coordinadorFacultad = Como(CorreoCoordinadorFacultad);

        var deProgramas = (await coordinadorPrograma.GetFromJsonAsync<ProgramasDePersonaDto>("/api/academico/mis-programas", Json))!;
        var deFacultad = (await coordinadorFacultad.GetFromJsonAsync<ProgramasDePersonaDto>("/api/academico/mis-programas", Json))!;

        Assert.Equal("Ingeniería de Sistemas", Assert.Single(deProgramas.Programas).ProgramaNombre);
        Assert.Equal("Ingenierías", deFacultad.FacultadAcompanada);
    }

    [Fact]
    public async Task El_coordinador_de_programa_ve_los_programas_de_sus_estudiantes()
    {
        using var coordinador = Como(CorreoCoordinadorPrograma);

        var ficha = await ProgramasDeAsync(coordinador, Datos.DestinatarioId);

        Assert.Equal("Carlos Mejía", ficha.NombreCompleto);
        Assert.Single(ficha.Programas);
    }

    /// <summary>La regla que importa: un coordinador no ve ni administra a las personas de otro programa.</summary>
    [Fact]
    public async Task Un_coordinador_de_otro_programa_no_ve_a_los_estudiantes_ajenos_y_el_intento_queda_auditado()
    {
        var (facultadB, programaB) = await CrearOtroProgramaAsync();

        // Un Coordinador de Programa del programa nuevo.
        await using (var contexto = BaseDatos.CrearContexto())
        {
            var ahora = DateTimeOffset.UtcNow;
            var persona = Semillas.Activa(new Persona("coordinador.biologia@uco.edu.co", "+573001239999", "Coordinadora de Biología", ahora));
            contexto.Personas.Add(persona);
            contexto.AsignacionesRol.Add(new AsignacionRol(persona.Id, Rol.CoordinadorPrograma, facultadB, programaB, ahora));
            await contexto.SaveChangesAsync();
        }

        using var coordinadorBiologia = Como("coordinador.biologia@uco.edu.co");
        var intento = await coordinadorBiologia.GetAsync($"/api/academico/personas/{Datos.DestinatarioId}/programas");

        Assert.Equal(HttpStatusCode.Forbidden, intento.StatusCode);

        await using var lectura = BaseDatos.CrearContexto();
        Assert.Contains(AccionesAuditoria.AccesoRechazado, await lectura.EventosAuditoria.Select(e => e.Accion).ToListAsync());
    }

    [Fact]
    public async Task Un_estudiante_no_ve_los_programas_de_otra_persona()
    {
        using var estudiante = Como(CorreoEstudiante);

        var intento = await estudiante.GetAsync($"/api/academico/personas/{Datos.AutorId}/programas");

        Assert.Equal(HttpStatusCode.Forbidden, intento.StatusCode);
    }

    [Fact]
    public async Task Consultar_los_programas_de_una_persona_que_no_existe_se_rechaza()
    {
        using var rector = Como(CorreoRector);

        Assert.Equal(HttpStatusCode.BadRequest, (await rector.GetAsync($"/api/academico/personas/{Guid.NewGuid()}/programas")).StatusCode);
    }

    // ------------------------------------------------------------- vínculos

    [Fact]
    public async Task Un_estudiante_puede_cursar_varios_programas_y_se_los_ve_todos()
    {
        var (_, programaB) = await CrearOtroProgramaAsync();

        using var rector = Como(CorreoRector);
        var vinculo = await rector.PostAsJsonAsync(
            $"/api/academico/personas/{Datos.DestinatarioId}/vinculos", new AsignarVinculoDto(programaB, "Matricula"));
        Assert.Equal(HttpStatusCode.NoContent, vinculo.StatusCode);

        using var estudiante = Como(CorreoEstudiante);
        var ficha = (await estudiante.GetFromJsonAsync<ProgramasDePersonaDto>("/api/academico/mis-programas", Json))!;

        Assert.Equal(2, ficha.Programas.Count);
        Assert.Equal("Principal", ficha.Programas[0].Relacion);
        Assert.Equal("Biología", ficha.Programas[1].ProgramaNombre);
        Assert.Equal("Matrícula", ficha.Programas[1].Relacion);
        Assert.Equal("Ciencias", ficha.Programas[1].FacultadNombre);
    }

    [Fact]
    public async Task Un_docente_puede_ensenar_en_varios_programas()
    {
        var (_, programaB) = await CrearOtroProgramaAsync();

        using var rector = Como(CorreoRector);
        Assert.Equal(HttpStatusCode.NoContent, (await rector.PostAsJsonAsync(
            $"/api/academico/personas/{Datos.AutorId}/vinculos", new AsignarVinculoDto(programaB, "Docencia"))).StatusCode);

        using var docente = Como(CorreoDocente);
        var ficha = (await docente.GetFromJsonAsync<ProgramasDePersonaDto>("/api/academico/mis-programas", Json))!;

        Assert.Contains(ficha.Programas, p => p.ProgramaNombre == "Biología" && p.Relacion == "Docencia");
    }

    [Fact]
    public async Task El_coordinador_de_programa_vincula_solo_dentro_de_su_programa()
    {
        var (_, programaB) = await CrearOtroProgramaAsync();

        using var coordinador = Como(CorreoCoordinadorPrograma);

        // Otro programa: no lo alcanza.
        var ajeno = await coordinador.PostAsJsonAsync(
            $"/api/academico/personas/{Datos.DestinatarioId}/vinculos", new AsignarVinculoDto(programaB, "Matricula"));
        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);

        // El suyo: sí. Un docente enseñando en el mismo programa de su rol no se repite en la ficha.
        var (_, programaPropio) = await AmbitoDeLaSemillaAsync();
        var propio = await coordinador.PostAsJsonAsync(
            $"/api/academico/personas/{Datos.AutorId}/vinculos", new AsignarVinculoDto(programaPropio, "Docencia"));
        Assert.Equal(HttpStatusCode.NoContent, propio.StatusCode);

        var ficha = await ProgramasDeAsync(coordinador, Datos.AutorId);
        Assert.Single(ficha.Programas);
    }

    [Fact]
    public async Task Un_estudiante_no_se_vincula_a_si_mismo_ni_a_otros()
    {
        var (_, programaB) = await CrearOtroProgramaAsync();

        using var estudiante = Como(CorreoEstudiante);

        Assert.Equal(HttpStatusCode.Forbidden, (await estudiante.PostAsJsonAsync(
            $"/api/academico/personas/{Datos.DestinatarioId}/vinculos", new AsignarVinculoDto(programaB, "Matricula"))).StatusCode);
    }

    [Fact]
    public async Task Un_vinculo_repetido_se_rechaza_y_quitarlo_lo_retira_de_la_ficha()
    {
        var (_, programaB) = await CrearOtroProgramaAsync();

        using var rector = Como(CorreoRector);
        var cuerpo = new AsignarVinculoDto(programaB, "Matricula");
        await rector.PostAsJsonAsync($"/api/academico/personas/{Datos.DestinatarioId}/vinculos", cuerpo);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await rector.PostAsJsonAsync($"/api/academico/personas/{Datos.DestinatarioId}/vinculos", cuerpo)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await rector.DeleteAsync($"/api/academico/personas/{Datos.DestinatarioId}/vinculos/{programaB}/Matricula")).StatusCode);
        Assert.Single((await ProgramasDeAsync(rector, Datos.DestinatarioId)).Programas);

        // Quitar lo que ya no está es un error claro, y se puede volver a vincular.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await rector.DeleteAsync($"/api/academico/personas/{Datos.DestinatarioId}/vinculos/{programaB}/Matricula")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await rector.PostAsJsonAsync($"/api/academico/personas/{Datos.DestinatarioId}/vinculos", cuerpo)).StatusCode);
        Assert.Equal(2, (await ProgramasDeAsync(rector, Datos.DestinatarioId)).Programas.Count);
    }

    [Fact]
    public async Task El_tipo_de_vinculo_tiene_que_corresponder_al_rol()
    {
        var (_, programaB) = await CrearOtroProgramaAsync();

        using var rector = Como(CorreoRector);

        // Un docente no se matricula, y un estudiante no enseña.
        Assert.Equal(HttpStatusCode.BadRequest, (await rector.PostAsJsonAsync(
            $"/api/academico/personas/{Datos.AutorId}/vinculos", new AsignarVinculoDto(programaB, "Matricula"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await rector.PostAsJsonAsync(
            $"/api/academico/personas/{Datos.DestinatarioId}/vinculos", new AsignarVinculoDto(programaB, "Docencia"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await rector.PostAsJsonAsync(
            $"/api/academico/personas/{Datos.DestinatarioId}/vinculos", new AsignarVinculoDto(programaB, "Otro"))).StatusCode);
    }

    [Fact]
    public async Task Los_vinculos_dejan_rastro_en_la_auditoria()
    {
        var (_, programaB) = await CrearOtroProgramaAsync();

        using var rector = Como(CorreoRector);
        await rector.PostAsJsonAsync(
            $"/api/academico/personas/{Datos.DestinatarioId}/vinculos", new AsignarVinculoDto(programaB, "Matricula"));
        await rector.DeleteAsync($"/api/academico/personas/{Datos.DestinatarioId}/vinculos/{programaB}/Matricula");

        await using var contexto = BaseDatos.CrearContexto();
        var acciones = await contexto.EventosAuditoria.Select(e => e.Accion).ToListAsync();

        Assert.Contains(AccionesAuditoria.VinculoAsignar, acciones);
        Assert.Contains(AccionesAuditoria.VinculoQuitar, acciones);
    }

    // ------------------------------------------------------ administrar estructura

    [Theory]
    [InlineData(CorreoAdministrador)]
    [InlineData(CorreoRector)]
    [InlineData(CorreoCoordinadorAcademico)]
    public async Task Administrador_rector_y_coordinador_academico_administran_la_estructura(string correo)
    {
        using var cliente = Como(correo);

        var creada = await cliente.PostAsJsonAsync("/api/academico/facultades", new DatosFacultadDto("Ciencias", "CIE"));
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);

        var completa = (await cliente.GetFromJsonAsync<List<FacultadAdminDto>>("/api/academico/estructura", Json))!;
        Assert.Contains(completa, f => f.Codigo == "CIE");
    }

    [Theory]
    [InlineData(CorreoCoordinadorFacultad)]
    [InlineData(CorreoCoordinadorPrograma)]
    [InlineData(CorreoDocente)]
    [InlineData(CorreoEstudiante)]
    public async Task Los_demas_roles_no_administran_la_estructura_y_el_intento_queda_auditado(string correo)
    {
        using var cliente = Como(correo);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await cliente.PostAsJsonAsync("/api/academico/facultades", new DatosFacultadDto("Ciencias", "CIE"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/academico/estructura")).StatusCode);

        await using var contexto = BaseDatos.CrearContexto();
        Assert.Contains(AccionesAuditoria.AccesoRechazado, await contexto.EventosAuditoria.Select(e => e.Accion).ToListAsync());
        Assert.False(await contexto.Facultades.AnyAsync(f => f.Codigo == "CIE"));
    }

    [Fact]
    public async Task El_codigo_de_una_facultad_o_de_un_programa_no_se_repite()
    {
        var (facultadB, _) = await CrearOtroProgramaAsync();

        using var rector = Como(CorreoRector);

        // El código de la facultad de la semilla, sin importar mayúsculas.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await rector.PostAsJsonAsync("/api/academico/facultades", new DatosFacultadDto("Otra", "ing"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await rector.PostAsJsonAsync($"/api/academico/facultades/{facultadB}/programas", new DatosProgramaDto("Otro", "bio"))).StatusCode);
    }

    [Fact]
    public async Task Se_editan_el_nombre_y_el_codigo_y_un_codigo_ajeno_se_rechaza()
    {
        var (facultadB, programaB) = await CrearOtroProgramaAsync();

        using var rector = Como(CorreoRector);

        Assert.Equal(HttpStatusCode.NoContent,
            (await rector.PutAsJsonAsync($"/api/academico/facultades/{facultadB}", new DatosFacultadDto("Ciencias Naturales", "CNA"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await rector.PutAsJsonAsync($"/api/academico/programas/{programaB}", new DatosProgramaDto("Biología Marina", "BMA"))).StatusCode);

        var completa = (await rector.GetFromJsonAsync<List<FacultadAdminDto>>("/api/academico/estructura", Json))!;
        var editada = completa.Single(f => f.Id == facultadB);
        Assert.Equal("Ciencias Naturales", editada.Nombre);
        Assert.Equal("BMA", Assert.Single(editada.Programas).Codigo);

        // Quedarse con el código de otra facultad no se puede.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await rector.PutAsJsonAsync($"/api/academico/facultades/{facultadB}", new DatosFacultadDto("Ciencias", "ING"))).StatusCode);

        // Y no se cambia lo que no existe.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await rector.PutAsJsonAsync($"/api/academico/facultades/{Guid.NewGuid()}", new DatosFacultadDto("X", "XXX"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await rector.PutAsJsonAsync($"/api/academico/programas/{Guid.NewGuid()}", new DatosProgramaDto("X", "XXX"))).StatusCode);
    }

    [Fact]
    public async Task Un_nombre_o_un_codigo_vacio_se_rechaza()
    {
        using var rector = Como(CorreoRector);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await rector.PostAsJsonAsync("/api/academico/facultades", new DatosFacultadDto("  ", "CIE"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await rector.PostAsJsonAsync("/api/academico/facultades", new DatosFacultadDto("Ciencias", ""))).StatusCode);

        // Un programa necesita una facultad que exista.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await rector.PostAsJsonAsync($"/api/academico/facultades/{Guid.NewGuid()}/programas", new DatosProgramaDto("X", "XXX"))).StatusCode);
    }

    /// <summary>
    /// Desactivar cierra solo lo nuevo: el desplegable del registro deja de ofrecer el programa, ya no
    /// admite solicitudes ni vínculos, y las personas que ya estaban siguen donde están.
    /// </summary>
    [Fact]
    public async Task Desactivar_un_programa_cierra_lo_nuevo_y_no_toca_lo_existente()
    {
        var (facultadB, programaB) = await CrearOtroProgramaAsync();

        using var rector = Como(CorreoRector);
        using var anonimo = Como();

        // Un estudiante ya vinculado antes de desactivar.
        await rector.PostAsJsonAsync($"/api/academico/personas/{Datos.DestinatarioId}/vinculos", new AsignarVinculoDto(programaB, "Matricula"));

        var antes = (await anonimo.GetFromJsonAsync<List<FacultadDto>>("/api/identidad/estructura", Json))!;
        Assert.Contains(antes.Single(f => f.Id == facultadB).Programas, p => p.Id == programaB);

        Assert.Equal(HttpStatusCode.NoContent,
            (await rector.PostAsync($"/api/academico/programas/{programaB}/estado?activo=false", null)).StatusCode);

        // El desplegable ya no lo ofrece (y no se quedó con la caché vieja).
        var despues = (await anonimo.GetFromJsonAsync<List<FacultadDto>>("/api/identidad/estructura", Json))!;
        Assert.DoesNotContain(despues.Single(f => f.Id == facultadB).Programas, p => p.Id == programaB);

        // No admite solicitudes nuevas...
        var solicitud = await anonimo.PostAsJsonAsync("/api/identidad/registro", new RegistrarCuentaDto(
            "nueva@uco.edu.co", "+573001230000", "Nueva Persona", Rol.Estudiante.Id, facultadB, programaB));
        Assert.Equal(HttpStatusCode.BadRequest, solicitud.StatusCode);

        // ...ni vínculos nuevos...
        Assert.Equal(HttpStatusCode.BadRequest, (await rector.PostAsJsonAsync(
            $"/api/academico/personas/{Datos.AutorId}/vinculos", new AsignarVinculoDto(programaB, "Docencia"))).StatusCode);

        // ...pero quien ya estaba vinculado lo sigue estando.
        Assert.Equal(2, (await ProgramasDeAsync(rector, Datos.DestinatarioId)).Programas.Count);

        // Y se puede reactivar.
        await rector.PostAsync($"/api/academico/programas/{programaB}/estado?activo=true", null);
        var reactivado = (await anonimo.GetFromJsonAsync<List<FacultadDto>>("/api/identidad/estructura", Json))!;
        Assert.Contains(reactivado.Single(f => f.Id == facultadB).Programas, p => p.Id == programaB);
    }

    [Fact]
    public async Task Desactivar_una_facultad_la_oculta_del_registro_y_no_admite_solicitudes_de_coordinador()
    {
        var (facultadB, _) = await CrearOtroProgramaAsync();

        using var rector = Como(CorreoRector);
        using var anonimo = Como();

        Assert.Equal(HttpStatusCode.NoContent,
            (await rector.PostAsync($"/api/academico/facultades/{facultadB}/estado?activa=false", null)).StatusCode);

        var despues = (await anonimo.GetFromJsonAsync<List<FacultadDto>>("/api/identidad/estructura", Json))!;
        Assert.DoesNotContain(despues, f => f.Id == facultadB);

        var solicitud = await anonimo.PostAsJsonAsync("/api/identidad/registro", new RegistrarCuentaDto(
            "decana@uco.edu.co", "+573001230001", "Nueva Decana", Rol.CoordinadorFacultad.Id, facultadB, null));
        Assert.Equal(HttpStatusCode.BadRequest, solicitud.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await rector.PostAsync($"/api/academico/facultades/{facultadB}/estado?activa=true", null)).StatusCode);
        Assert.Contains(
            (await anonimo.GetFromJsonAsync<List<FacultadDto>>("/api/identidad/estructura", Json))!, f => f.Id == facultadB);
    }

    [Fact]
    public async Task Un_programa_nuevo_aparece_enseguida_en_el_desplegable_del_registro()
    {
        using var anonimo = Como();

        // Se consulta antes para que la caché quede llena: un cambio tiene que invalidarla.
        var antes = (await anonimo.GetFromJsonAsync<List<FacultadDto>>("/api/identidad/estructura", Json))!;
        Assert.Single(antes);

        var (facultadB, programaB) = await CrearOtroProgramaAsync();

        var despues = (await anonimo.GetFromJsonAsync<List<FacultadDto>>("/api/identidad/estructura", Json))!;
        Assert.Equal(2, despues.Count);
        Assert.Contains(despues.Single(f => f.Id == facultadB).Programas, p => p.Id == programaB);
    }

    [Fact]
    public async Task El_registro_rechaza_un_programa_que_no_pertenece_a_la_facultad_indicada()
    {
        var (facultadA, _) = await AmbitoDeLaSemillaAsync();
        var (_, programaB) = await CrearOtroProgramaAsync();

        using var anonimo = Como();
        var solicitud = await anonimo.PostAsJsonAsync("/api/identidad/registro", new RegistrarCuentaDto(
            "mezcla@uco.edu.co", "+573001230002", "Persona Mezclada", Rol.Estudiante.Id, facultadA, programaB));

        Assert.Equal(HttpStatusCode.BadRequest, solicitud.StatusCode);
    }

    [Fact]
    public async Task La_estructura_deja_rastro_en_la_auditoria()
    {
        var (facultadB, programaB) = await CrearOtroProgramaAsync();

        using var rector = Como(CorreoRector);
        await rector.PutAsJsonAsync($"/api/academico/programas/{programaB}", new DatosProgramaDto("Biología Marina", "BMA"));
        await rector.PostAsync($"/api/academico/facultades/{facultadB}/estado?activa=false", null);

        await using var contexto = BaseDatos.CrearContexto();
        var acciones = await contexto.EventosAuditoria.Select(e => e.Accion).ToListAsync();

        Assert.Equal(2, acciones.Count(a => a == AccionesAuditoria.EstructuraCrear));
        Assert.Contains(AccionesAuditoria.EstructuraActualizar, acciones);
        Assert.Contains(AccionesAuditoria.EstructuraCambiarEstado, acciones);
    }

    [Fact]
    public async Task Sin_identidad_no_se_consulta_ni_se_administra_nada()
    {
        using var anonimo = Como();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/academico/mis-programas")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/academico/estructura")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonimo.PostAsJsonAsync("/api/academico/facultades", new DatosFacultadDto("Ciencias", "CIE"))).StatusCode);
    }
}
