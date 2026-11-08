using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using UcoKudos.Application.Comun.CasosUso;
using UcoKudos.Domain.Transversal;
using UcoKudos.Infrastructure;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

// ArchUnitNET tiene su propio tipo `Assembly` (el del modelo que analiza), distinto del de
// reflexión. El alias evita la ambigüedad y deja claro cuál se usa en cada sitio.
using EnsambladoNet = System.Reflection.Assembly;

namespace UcoKudos.ArchitectureTests;

/// <summary>
/// Las cinco reglas de dependencia de ADR-0001, verificadas de forma automatizada. Esto es lo que
/// convierte Clean Architecture en una propiedad comprobable en lugar de una afirmación del
/// documento.
///
/// Reparto de responsabilidades, que conviene tener claro al sustentar:
///
/// - Las reglas 1, 2 y 3 ya las impone el **compilador**, porque cada capa es un proyecto y las
///   referencias entre proyectos van en un solo sentido. Estas pruebas las guardan para que nadie
///   agregue una referencia en sentido contrario sin que falle algo: el compilador la aceptaría.
/// - La regla 4 es la que **solo** estas pruebas pueden verificar, porque la API sí referencia la
///   infraestructura para su raíz de composición. La regla comprueba que no la use en ningún otro
///   sitio.
/// - La regla 5 se verifica por reflexión sobre los ensamblados referenciados, que es más directo
///   y más fiable que inspeccionar tipos que no están cargados.
///
/// Disciplina al añadir una regla (ADR-0017): comprobarla provocando a propósito una violación.
/// Una regla que nunca ha fallado no está verificada; es fácil escribir una que no detecta nada.
/// </summary>
public sealed class ReglasCleanArchitectureTests
{
    private static readonly EnsambladoNet EnsambladoDominio = typeof(ExcepcionDominio).Assembly;
    private static readonly EnsambladoNet EnsambladoAplicacion = typeof(ICasoUso<,>).Assembly;
    private static readonly EnsambladoNet EnsambladoInfraestructura = typeof(ServiciosInfraestructura).Assembly;
    private static readonly EnsambladoNet EnsambladoApi = typeof(Program).Assembly;

    private static readonly Architecture Arquitectura = new ArchLoader()
        .LoadAssemblies(EnsambladoDominio, EnsambladoAplicacion, EnsambladoInfraestructura, EnsambladoApi)
        .Build();

    /// <summary>Regla 1: el dominio es el centro y no depende de ninguna otra capa.</summary>
    [Fact]
    public void El_dominio_no_depende_de_ninguna_otra_capa()
    {
        IArchRule regla = Types().That().ResideInAssembly(EnsambladoDominio)
            .Should().NotDependOnAny(Types().That().ResideInAssembly(EnsambladoAplicacion))
            .AndShould().NotDependOnAny(Types().That().ResideInAssembly(EnsambladoInfraestructura))
            .AndShould().NotDependOnAny(Types().That().ResideInAssembly(EnsambladoApi))
            .Because("el dominio es el centro de la arquitectura (ADR-0001, regla 1)");

        regla.Check(Arquitectura);
    }

    /// <summary>Regla 2: la aplicación depende solo del dominio.</summary>
    [Fact]
    public void La_aplicacion_solo_depende_del_dominio()
    {
        IArchRule regla = Types().That().ResideInAssembly(EnsambladoAplicacion)
            .Should().NotDependOnAny(Types().That().ResideInAssembly(EnsambladoInfraestructura))
            .AndShould().NotDependOnAny(Types().That().ResideInAssembly(EnsambladoApi))
            .Because("los casos de uso declaran puertos; no conocen a quién los implementa (ADR-0001, regla 2)");

        regla.Check(Arquitectura);
    }

    /// <summary>Regla 3: la infraestructura implementa puertos y no conoce la capa de interfaces.</summary>
    [Fact]
    public void La_infraestructura_no_depende_de_la_capa_de_interfaces()
    {
        IArchRule regla = Types().That().ResideInAssembly(EnsambladoInfraestructura)
            .Should().NotDependOnAny(Types().That().ResideInAssembly(EnsambladoApi))
            .Because("un adaptador de salida no sabe que existe una API HTTP (ADR-0001, regla 3)");

        regla.Check(Arquitectura);
    }

    /// <summary>
    /// Regla 4: la API solo conoce puertos de entrada y DTO. La única excepción es la raíz de
    /// composición, donde registra los adaptadores.
    ///
    /// Nota al sustentar: el cuerpo del método de nivel superior de <c>Program.cs</c> no se analiza
    /// aquí, porque estas reglas inspeccionan dependencias dentro de clases. Eso es justamente lo
    /// que permite que la raíz de composición viva ahí sin ser una violación. Si el registro de
    /// adaptadores creciera y se moviera a una clase propia, hay que excluirla de forma explícita.
    /// </summary>
    [Fact]
    public void La_api_no_usa_la_infraestructura_fuera_de_la_raiz_de_composicion()
    {
        IArchRule regla = Types().That().ResideInAssembly(EnsambladoApi)
            .Should().NotDependOnAny(Types().That().ResideInAssembly(EnsambladoInfraestructura))
            .Because("los endpoints hablan con puertos de entrada, no con adaptadores (ADR-0001, regla 4)");

        regla.Check(Arquitectura);
    }

    /// <summary>
    /// Regla 5: ni el dominio ni la aplicación conocen la tecnología de persistencia. Es la regla
    /// que hace posible probar las invariantes sin levantar una base de datos (ADR-0003).
    /// </summary>
    [Theory]
    [InlineData("UcoKudos.Domain")]
    [InlineData("UcoKudos.Application")]
    public void El_nucleo_no_referencia_la_tecnologia_de_persistencia(string capa)
    {
        string[] prohibidos = ["EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore", "Minio", "AWSSDK"];

        var ensamblado = capa == "UcoKudos.Domain" ? EnsambladoDominio : EnsambladoAplicacion;
        var referencias = ensamblado.GetReferencedAssemblies()
            .Select(r => r.Name ?? string.Empty)
            .Where(nombre => prohibidos.Any(p => nombre.Contains(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            referencias.Count == 0,
            $"{capa} no debe referenciar infraestructura (ADR-0001, regla 5). Encontrado: {string.Join(", ", referencias)}");
    }
}
