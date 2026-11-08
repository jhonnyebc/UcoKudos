using Microsoft.EntityFrameworkCore;
using UcoKudos.Domain.Academico;
using UcoKudos.Domain.Identidad;
using UcoKudos.IntegrationTests.Infraestructura;

namespace UcoKudos.IntegrationTests.Persistencia;

/// <summary>
/// Las invariantes de RF-04 y RN-04, probadas contra la base real.
///
/// Es la prueba que respalda **QA-SEG-03** y una de las tres no negociables de la pirámide. Importa
/// que sea de integración y no unitaria: la garantía de estas reglas no es el dominio, que solo da un
/// mensaje claro, sino los índices únicos parciales. Una validación en la aplicación deja pasar dos
/// peticiones simultáneas; un índice único rechaza la segunda.
/// </summary>
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Category", "Integration")]
public sealed class InvariantesDeRolTests(BaseDeDatosFixture baseDatos) : IAsyncLifetime
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 8, 17, 39, 0, TimeSpan.Zero);

    public async Task InitializeAsync() => await baseDatos.LimpiarAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// QA-SEG-03: el segundo Rector activo es imposible.
    ///
    /// Esta es la prueba que demuestra por qué la regla vive en la base. Las dos asignaciones son
    /// válidas por separado y el dominio no tiene forma de saber que ya existe la otra.
    /// </summary>
    [Fact]
    public async Task No_puede_haber_dos_rectores_activos()
    {
        var primero = await CrearPersonaAsync("rector1@uco.edu.co");
        var segundo = await CrearPersonaAsync("rector2@uco.edu.co");

        await using var contexto = baseDatos.CrearContexto();

        contexto.AsignacionesRol.Add(new AsignacionRol(primero, Rol.Rector, null, null, Ahora));
        await contexto.SaveChangesAsync();

        contexto.AsignacionesRol.Add(new AsignacionRol(segundo, Rol.Rector, null, null, Ahora));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync());

        Assert.Contains("ux_rol_unico_universidad", error.InnerException?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_puede_haber_dos_coordinadores_academicos_activos()
    {
        var primero = await CrearPersonaAsync("academico1@uco.edu.co");
        var segundo = await CrearPersonaAsync("academico2@uco.edu.co");

        await using var contexto = baseDatos.CrearContexto();

        contexto.AsignacionesRol.Add(new AsignacionRol(primero, Rol.CoordinadorAcademico, null, null, Ahora));
        await contexto.SaveChangesAsync();

        contexto.AsignacionesRol.Add(new AsignacionRol(segundo, Rol.CoordinadorAcademico, null, null, Ahora));

        await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync());
    }

    /// <summary>
    /// El Administrador es la excepción deliberada: de ese rol puede haber varios, y el filtro del
    /// índice lo excluye. Esta prueba fija esa excepción para que nadie la "arregle" por error.
    /// </summary>
    [Fact]
    public async Task Puede_haber_varios_administradores()
    {
        var primero = await CrearPersonaAsync("admin1@uco.edu.co");
        var segundo = await CrearPersonaAsync("admin2@uco.edu.co");

        await using var contexto = baseDatos.CrearContexto();

        contexto.AsignacionesRol.Add(new AsignacionRol(primero, Rol.Administrador, null, null, Ahora));
        contexto.AsignacionesRol.Add(new AsignacionRol(segundo, Rol.Administrador, null, null, Ahora));

        await contexto.SaveChangesAsync();

        Assert.Equal(2, await contexto.AsignacionesRol.CountAsync(a => a.RolId == Rol.Administrador.Id));
    }

    /// <summary>Un Coordinador de Programa activo por programa, pero uno por cada programa distinto.</summary>
    [Fact]
    public async Task Solo_un_coordinador_activo_por_programa_pero_uno_por_cada_programa()
    {
        var facultad = await CrearFacultadAsync();
        var programaA = await CrearProgramaAsync(facultad, "ISIS");
        var programaB = await CrearProgramaAsync(facultad, "IIND");

        var primero = await CrearPersonaAsync("coord1@uco.edu.co");
        var segundo = await CrearPersonaAsync("coord2@uco.edu.co");
        var tercero = await CrearPersonaAsync("coord3@uco.edu.co");

        await using var contexto = baseDatos.CrearContexto();

        contexto.AsignacionesRol.Add(new AsignacionRol(primero, Rol.CoordinadorPrograma, facultad, programaA, Ahora));
        contexto.AsignacionesRol.Add(new AsignacionRol(segundo, Rol.CoordinadorPrograma, facultad, programaB, Ahora));
        await contexto.SaveChangesAsync();

        // Dos programas distintos: correcto.
        Assert.Equal(2, await contexto.AsignacionesRol.CountAsync(a => a.RolId == Rol.CoordinadorPrograma.Id));

        // El mismo programa otra vez: rechazado.
        contexto.AsignacionesRol.Add(new AsignacionRol(tercero, Rol.CoordinadorPrograma, facultad, programaA, Ahora));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync());
        Assert.Contains("ux_rol_unico_programa", error.InnerException?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>RN-04: exclusividad. Una persona tiene un único rol activo.</summary>
    [Fact]
    public async Task Una_persona_no_puede_tener_dos_roles_activos()
    {
        var facultad = await CrearFacultadAsync();
        var programa = await CrearProgramaAsync(facultad, "ISIS");
        var persona = await CrearPersonaAsync("doble@uco.edu.co");

        await using var contexto = baseDatos.CrearContexto();

        contexto.AsignacionesRol.Add(new AsignacionRol(persona, Rol.Estudiante, facultad, programa, Ahora));
        await contexto.SaveChangesAsync();

        contexto.AsignacionesRol.Add(new AsignacionRol(persona, Rol.Docente, facultad, programa, Ahora));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync());
        Assert.Contains("ux_rol_activo_por_persona", error.InnerException?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// Revocar el rol libera el espacio: el índice es parcial, solo cuenta los activos. Es lo que
    /// permite cambiarle el rol a alguien sin borrar su historial.
    /// </summary>
    [Fact]
    public async Task Revocar_el_rol_permite_asignar_otro()
    {
        var facultad = await CrearFacultadAsync();
        var programa = await CrearProgramaAsync(facultad, "ISIS");
        var persona = await CrearPersonaAsync("cambio@uco.edu.co");

        await using var contexto = baseDatos.CrearContexto();

        var primera = new AsignacionRol(persona, Rol.Estudiante, facultad, programa, Ahora);
        contexto.AsignacionesRol.Add(primera);
        await contexto.SaveChangesAsync();

        primera.Revocar(Ahora.AddDays(1));
        contexto.AsignacionesRol.Add(new AsignacionRol(persona, Rol.Docente, facultad, programa, Ahora.AddDays(1)));

        await contexto.SaveChangesAsync();

        Assert.Equal(2, await contexto.AsignacionesRol.CountAsync(a => a.PersonaId == persona));
        Assert.Equal(1, await contexto.AsignacionesRol.CountAsync(a => a.PersonaId == persona && a.Activa));
    }

    /// <summary>El correo institucional es único: no entran dos cuentas con el mismo.</summary>
    [Fact]
    public async Task El_correo_institucional_es_unico()
    {
        await CrearPersonaAsync("repetido@uco.edu.co");

        await using var contexto = baseDatos.CrearContexto();
        contexto.Personas.Add(new Persona("repetido@uco.edu.co", "+573009999999", "Otro", Ahora));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync());
        Assert.Contains("ux_persona_correo", error.InnerException?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    private async Task<Guid> CrearPersonaAsync(string correo)
    {
        await using var contexto = baseDatos.CrearContexto();
        var persona = new Persona(correo, "+573001234567", "Persona de prueba", Ahora);

        contexto.Personas.Add(persona);
        await contexto.SaveChangesAsync();

        return persona.Id;
    }

    private async Task<Guid> CrearFacultadAsync()
    {
        await using var contexto = baseDatos.CrearContexto();
        var facultad = new Facultad("Ingenierías", "ING", Ahora);

        contexto.Facultades.Add(facultad);
        await contexto.SaveChangesAsync();

        return facultad.Id;
    }

    private async Task<Guid> CrearProgramaAsync(Guid facultadId, string codigo)
    {
        await using var contexto = baseDatos.CrearContexto();
        var programa = new Programa(facultadId, "Programa " + codigo, codigo, Ahora);

        contexto.Programas.Add(programa);
        await contexto.SaveChangesAsync();

        return programa.Id;
    }
}
