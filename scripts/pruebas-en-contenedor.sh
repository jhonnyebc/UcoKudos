#!/bin/sh
# Ejecuta un grupo de pruebas. Se corre DENTRO del contenedor Linux (ver scripts/pruebas.ps1),
# y es también el comando que conviene reutilizar desde el CI para que local y pipeline hagan
# exactamente lo mismo.
#
# Uso: pruebas-en-contenedor.sh <unitarias|arquitectura|integracion>
#
# Los argumentos de MSBuild van escritos aquí, en un shell, porque PowerShell parte en varios
# argumentos las cadenas que llevan coma (-p:ThresholdType=line,branch llegaba troceado).
set -eu

GRUPO="${1:?Indicar el grupo: unitarias, arquitectura o integracion}"

# Carpeta de artefactos propia del contenedor: no se mezcla con bin/ y obj/ de Windows, que
# guardan rutas absolutas del sistema donde se generaron.
ARTEFACTOS="${ARTEFACTOS:-/artefactos}"

# Umbral de cobertura del núcleo (ADR-0017). Arranca en 0 y sube a 80 líneas / 70 ramas con el
# primer caso de uso. Debe coincidir con el del workflow de CI.
UMBRAL_LINEAS=0
UMBRAL_RAMAS=0

case "$GRUPO" in
    unitarias)
        exec dotnet test tests/UcoKudos.UnitTests -c Release \
            --artifacts-path "$ARTEFACTOS" \
            -p:CollectCoverage=true \
            -p:CoverletOutputFormat=opencover \
            -p:CoverletOutput=TestResults/coverage/ \
            "-p:Threshold=${UMBRAL_LINEAS}%2c${UMBRAL_RAMAS}" \
            "-p:ThresholdType=line%2cbranch"
        ;;
    arquitectura)
        exec dotnet test tests/UcoKudos.ArchitectureTests -c Release \
            --artifacts-path "$ARTEFACTOS"
        ;;
    integracion)
        # Levantan sus propios contenedores con Testcontainers, a través del socket de Docker
        # que monta scripts/pruebas.ps1.
        exec dotnet test tests/UcoKudos.IntegrationTests -c Release \
            --artifacts-path "$ARTEFACTOS" \
            --filter "Category=Integration"
        ;;
    *)
        echo "Grupo desconocido: $GRUPO" >&2
        exit 2
        ;;
esac
