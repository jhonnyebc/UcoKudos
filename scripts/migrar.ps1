<#
.SYNOPSIS
    Aplica las migraciones de Entity Framework a la base de datos local, desde un contenedor.

.DESCRIPTION
    Por qué dentro de un contenedor y no con `dotnet ef` directo: es la misma razón que con las
    pruebas. Smart App Control está activo en modo bloqueo en el equipo de desarrollo y rechaza
    cargar ensamblados recién compilados sin reputación. `dotnet build` funciona, pero la
    herramienta de migraciones necesita *cargar* el ensamblado para leer el contexto, y muere con
    "Could not load assembly 'UcoKudos.Infrastructure'", que es un mensaje engañoso: el ensamblado
    está y está referenciado; lo que falla es cargarlo.

    La cadena de conexión se arma leyendo .env, **en el contenedor**, y se pasa por variable de
    entorno. No se escribe en ningún archivo ni se imprime: un secreto que aparece en una consola
    deja de ser un secreto.

    El contenedor habla con el PostgreSQL del equipo a través de host.docker.internal, porque la
    base corre en Docker Desktop y publica su puerto en Windows, no dentro de esta red.

.EXAMPLE
    ./scripts/migrar.ps1
#>
[CmdletBinding()]
param(
    # Nombre de una migración nueva. Sin este parámetro, el guion aplica las pendientes.
    #
    # Crear migraciones también va por contenedor porque `dotnet ef migrations add` necesita
    # cargar el modelo, y Smart App Control bloquea en este equipo los ensamblados recién
    # compilados. Conviene tenerlo resuelto de antemano: en una sustentación no hay tiempo de
    # descubrir que el camino habitual no funciona.
    [string]$Crear
)

$ErrorActionPreference = 'Stop'

$imagen = 'mcr.microsoft.com/dotnet/sdk:10.0.403-noble'
$volumenNuget = 'ucokudos-nuget'
$raiz = (Resolve-Path "$PSScriptRoot/..").Path

& docker info --format '{{.ServerVersion}}' *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Docker no responde. Abrir Docker Desktop y esperar a que 'docker info' conteste." -ForegroundColor Red
    exit 1
}

if (-not (Test-Path "$raiz/.env")) {
    Write-Host "Falta el archivo .env. Copiar .env.example y rellenarlo (manual de puesta a punto)." -ForegroundColor Red
    exit 1
}

if ($Crear) {
    Write-Host "Creando la migracion '$Crear' en contenedor ($imagen)" -ForegroundColor Cyan
    $argumentos = @('sh', 'scripts/migrar-en-contenedor.sh', 'crear', $Crear)
}
else {
    Write-Host "Aplicando migraciones en contenedor ($imagen)" -ForegroundColor Cyan
    $argumentos = @('sh', 'scripts/migrar-en-contenedor.sh', 'aplicar')
}

& docker run --rm `
    -v "${raiz}:/src" `
    -v "${volumenNuget}:/root/.nuget/packages" `
    --add-host 'host.docker.internal:host-gateway' `
    -w /src $imagen @argumentos

if ($LASTEXITCODE -ne 0) {
    Write-Host "La migración falló." -ForegroundColor Red
    exit 1
}

if ($Crear) {
    Write-Host "Migracion creada. Revisar los archivos nuevos y aplicarla con ./scripts/migrar.ps1" -ForegroundColor Green
}
else {
    Write-Host "Migraciones aplicadas." -ForegroundColor Green
}
