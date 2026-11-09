#!/bin/sh
# Aplica las migraciones de EF Core. Se ejecuta DENTRO del contenedor; lo lanza scripts/migrar.ps1.
#
# La cadena de conexión se arma aquí, a partir de .env, y viaja por variable de entorno. No se
# escribe en disco ni se imprime: la contraseña es un secreto (ADR-0018).
set -eu

# Copia del código dentro del contenedor, fuera del volumen montado desde Windows.
TRABAJO=/trabajo

leer() {
  # Quita los retornos de carro de Windows y se queda con el primer valor de la clave pedida.
  tr -d '\r' < .env | sed -n "s/^$1=//p" | head -1
}

PUERTO=$(leer POSTGRES_PUERTO)
BD=$(leer POSTGRES_BD)
USUARIO=$(leer POSTGRES_USUARIO)
CLAVE=$(leer POSTGRES_PASSWORD)

[ -n "$PUERTO" ] || PUERTO=5435
[ -n "$BD" ] || BD=ucokudos

if [ -z "$USUARIO" ] || [ -z "$CLAVE" ]; then
  echo "Faltan POSTGRES_USUARIO o POSTGRES_PASSWORD en .env." >&2
  exit 1
fi

# host.docker.internal y no localhost: la base corre en otro contenedor que publica su puerto en
# el equipo, no en la red de este.
export ConnectionStrings__Postgres="Host=host.docker.internal;Port=$PUERTO;Database=$BD;Username=$USUARIO;Password=$CLAVE"

# El código se copia a una carpeta del contenedor en vez de trabajar sobre /src, que es el
# repositorio de Windows montado. La herramienta de migraciones compila por su cuenta y no
# respeta la carpeta de artefactos, así que escribiría bin/ y obj/ de Linux encima de los de
# Windows; mezclarlos rompe las dos compilaciones, porque los intermedios guardan rutas absolutas
# del sistema donde se generaron. Copiar sale más barato que arreglar eso después.
rm -rf "$TRABAJO"
mkdir -p "$TRABAJO"
tar -c --exclude=bin --exclude=obj --exclude=.git -C /src . | tar -x -C "$TRABAJO"
cd "$TRABAJO"

# La herramienta está fijada en el manifiesto del repositorio, así que aquí solo se restaura: la
# versión es la misma en este equipo, en el contenedor y en el pipeline.
dotnet tool restore > /dev/null

dotnet restore src/UcoKudos.Api/UcoKudos.Api.csproj > /dev/null

dotnet ef database update \
  --project src/UcoKudos.Infrastructure \
  --startup-project src/UcoKudos.Api
