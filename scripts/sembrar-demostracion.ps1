<#
.SYNOPSIS
    Siembra los datos de demostración en la base local.

.DESCRIPTION
    Crea una facultad, un programa y cinco personas con roles distintos, ya aprobadas y
    verificadas, para poder enseñar la plataforma funcionando sin pasar antes por el flujo de
    registro y aprobación de cada cuenta.

    No toca producción y no es una migración: es contenido de ejemplo para el entorno local. El
    SQL está en scripts/datos-de-demostracion.sql y es idempotente.

.EXAMPLE
    ./scripts/sembrar-demostracion.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$raiz = (Resolve-Path "$PSScriptRoot/..").Path

& docker info --format '{{.ServerVersion}}' *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Docker no responde. Abrir Docker Desktop y esperar a que 'docker info' conteste." -ForegroundColor Red
    exit 1
}

Write-Host "Sembrando datos de demostración..." -ForegroundColor Cyan

# `docker compose cp` escribe su progreso por el canal de errores aunque todo vaya bien, y con
# ErrorActionPreference en 'Stop' PowerShell lo toma por un fallo y aborta. Se comprueba el código
# de salida, que es lo que de verdad dice si funcionó.
$ErrorActionPreference = 'Continue'

# El archivo se COPIA al contenedor y psql lo lee de ahí, en lugar de mandarlo por la tubería.
#
# No es un rodeo: la primera versión lo enviaba con `Get-Content | psql` y PowerShell reescribía
# la codificación por el camino, así que «Peña» acabó guardado como «PeÃ±a» en la base. Se vio en
# el muro, con los nombres rotos. Una tubería de PowerShell no conserva UTF-8, y en un proyecto en
# español eso corrompe los datos en silencio.
& docker compose -f "$raiz/docker-compose.yml" cp `
    "$raiz/scripts/datos-de-demostracion.sql" postgres:/tmp/datos-de-demostracion.sql

if ($LASTEXITCODE -ne 0) {
    Write-Host "No se pudo copiar el archivo al contenedor." -ForegroundColor Red
    exit 1
}

& docker compose -f "$raiz/docker-compose.yml" exec -T postgres `
    psql -U ucokudos -d ucokudos -v ON_ERROR_STOP=1 -f /tmp/datos-de-demostracion.sql

if ($LASTEXITCODE -ne 0) {
    Write-Host "La siembra falló." -ForegroundColor Red
    exit 1
}

Write-Host "Listo." -ForegroundColor Green
