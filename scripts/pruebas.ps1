<#
.SYNOPSIS
    Ejecuta las pruebas dentro de un contenedor Linux con el SDK de .NET.

.DESCRIPTION
    Por qué no se corren directamente en Windows: Smart App Control está activo en modo bloqueo
    en el equipo de desarrollo y rechaza cargar ensamblados recién compilados que todavía no
    tienen reputación. `dotnet build` y `dotnet format` funcionan sin problema; lo que falla es
    *cargar* los DLL, así que el ejecutor de pruebas muere antes de la primera prueba con
    "An Application Control policy has blocked this file (0x800711C7)".

    Correr las pruebas en un contenedor lo evita sin desactivar ninguna protección del sistema
    (desactivar Smart App Control es irreversible), y además usa el mismo entorno Linux en el que
    las ejecuta el CI (ADR-0017), así que un resultado local y uno del pipeline son comparables.

    El comando concreto vive en scripts/pruebas-en-contenedor.sh, que es lo que se ejecuta dentro.

.PARAMETER Proyecto
    Qué ejecutar: unitarias, arquitectura, integracion o todas. Por omisión, todas menos las de
    integración, que levantan contenedores y tardan bastante más.

.EXAMPLE
    ./scripts/pruebas.ps1
    ./scripts/pruebas.ps1 -Proyecto arquitectura
    ./scripts/pruebas.ps1 -Proyecto integracion
#>
[CmdletBinding()]
param(
    [ValidateSet('todas', 'unitarias', 'arquitectura', 'integracion')]
    [string]$Proyecto = 'todas'
)

$ErrorActionPreference = 'Stop'

$imagen = 'mcr.microsoft.com/dotnet/sdk:10.0.403-noble'
$volumenNuget = 'ucokudos-nuget'
$raiz = (Resolve-Path "$PSScriptRoot/..").Path

# Docker Desktop cerrado es la causa más común de que esto falle, y su mensaje crudo
# ("no se encuentra el archivo especificado") no lo deja claro. Mejor decirlo aquí.
& docker info --format '{{.ServerVersion}}' *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Docker no responde. Abrir Docker Desktop y esperar a que 'docker info' conteste (cerca de un minuto)." -ForegroundColor Red
    exit 1
}

$aEjecutar = switch ($Proyecto) {
    'todas' { @('unitarias', 'arquitectura') }
    default { @($Proyecto) }
}

Write-Host "Pruebas en contenedor ($imagen)" -ForegroundColor Cyan

$fallos = @()

foreach ($grupo in $aEjecutar) {
    Write-Host ""
    Write-Host "--- $grupo ---" -ForegroundColor Cyan

    # El caché de NuGet en un volumen con nombre: solo la primera ejecución descarga paquetes.
    $montajes = @(
        '-v', "${raiz}:/src",
        '-v', "${volumenNuget}:/root/.nuget/packages"
    )

    # El socket de Docker solo para las de integración: son las únicas que lo necesitan, y
    # montarlo le da al contenedor control sobre el Docker del equipo.
    #
    # Los dos ajustes de entorno hacen falta porque las pruebas levantan contenedores desde DENTRO
    # de un contenedor, y eso cambia dos cosas:
    #
    #  - El vigilante de recursos de Testcontainers no arranca en este escenario y aborta la
    #    inicialización. Se desactiva; la contrapartida es que si una ejecución se corta a la mitad,
    #    el PostgreSQL de la prueba queda vivo y hay que quitarlo con `docker container prune`.
    #
    #  - Los contenedores que las pruebas levantan son HERMANOS, no hijos: publican su puerto en el
    #    host de Windows, no en la red interna de Docker. Sin decirle cuál es el host, las pruebas
    #    buscan la base en la dirección equivocada y fallan con «conexión rechazada».
    if ($grupo -eq 'integracion') {
        $montajes += @(
            '-v', '/var/run/docker.sock:/var/run/docker.sock',
            '-e', 'TESTCONTAINERS_RYUK_DISABLED=true',
            '-e', 'TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal',
            '--add-host', 'host.docker.internal:host-gateway'
        )
    }

    & docker run --rm @montajes -w /src $imagen sh scripts/pruebas-en-contenedor.sh $grupo

    if ($LASTEXITCODE -ne 0) {
        $fallos += $grupo
    }
}

Write-Host ""
if ($fallos.Count -gt 0) {
    Write-Host "FALLARON: $($fallos -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host "Todas las pruebas ejecutadas pasaron." -ForegroundColor Green
