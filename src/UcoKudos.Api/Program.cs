using UcoKudos.Infrastructure;

var constructor = WebApplication.CreateBuilder(args);

// Raíz de composición: el único lugar de la API que conoce la capa de infraestructura (ADR-0001).
constructor.Services.AgregarInfraestructura();

// Puntos de salud (ADR-0014): el de vida dice si el proceso está en pie; el de preparación
// comprobará además la base y el almacenamiento de objetos cuando existan sus adaptadores.
constructor.Services.AddHealthChecks();

var aplicacion = constructor.Build();

aplicacion.MapHealthChecks("/salud/vivo");
aplicacion.MapHealthChecks("/salud/listo");

aplicacion.Run();
