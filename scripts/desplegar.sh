#!/bin/sh
# Despliega UCO Kudos en el servidor (ADR-0016). Se ejecuta EN la máquina, desde la carpeta del
# repositorio.
#
#   ./scripts/desplegar.sh 0.1.0-a1b2c3d
#
# El argumento es la etiqueta de la imagen que el pipeline construyó y escaneó. No se reconstruye
# nada aquí: se despliega exactamente lo que pasó las puertas de calidad (ADR-0017).
#
# **El orden importa y es el del ADR:** primero se traen las imágenes, después se aplican las
# migraciones, y solo entonces la versión nueva empieza a recibir tráfico. Al revés, la aplicación
# nueva se encontraría un esquema viejo durante unos segundos, que es tiempo de sobra para romper
# peticiones reales.
set -eu

VERSION="${1:-}"

if [ -z "$VERSION" ]; then
  echo "Falta la versión. Uso: ./scripts/desplegar.sh <etiqueta>" >&2
  echo "Las etiquetas publicadas están en el registro del repositorio." >&2
  exit 1
fi

if [ ! -f .env ]; then
  echo "Falta .env en el servidor. Copiar .env.produccion.example y rellenarlo." >&2
  exit 1
fi

COMPOSE="docker compose -f docker-compose.yml -f docker-compose.produccion.yml"

echo "==> Desplegando $VERSION"

# Las versiones se fijan aquí y se guardan en .env, de modo que el archivo refleja siempre lo que
# está corriendo. Si alguien entra mañana a la máquina, puede saberlo sin preguntar.
grep -v '^VERSION_API=\|^VERSION_WEB=' .env > .env.nuevo
printf 'VERSION_API=%s\nVERSION_WEB=%s\n' "$VERSION" "$VERSION" >> .env.nuevo
mv .env.nuevo .env

echo "==> Trayendo las imágenes"
$COMPOSE pull api web proxy

echo "==> Levantando la base"
$COMPOSE up -d postgres
$COMPOSE exec -T postgres sh -c 'until pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB"; do sleep 1; done'

echo "==> Aplicando migraciones"
# Se usa la MISMA imagen que se va a desplegar, no una compilación aparte: así el esquema lo aplica
# exactamente el código que después lo va a usar.
$COMPOSE run --rm --entrypoint dotnet api UcoKudos.Api.dll --migrar

echo "==> Publicando el cliente web"
$COMPOSE up web

echo "==> Arrancando la versión nueva"
$COMPOSE up -d

echo "==> Comprobando"
# No se da por bueno un despliegue porque el contenedor arrancó: se comprueba que la aplicación
# responde y que sus dependencias responden. Un servicio «vivo» pero no «listo» acepta peticiones
# y las falla todas.
intentos=0
until curl -fsS http://localhost/salud/listo > /dev/null 2>&1; do
  intentos=$((intentos + 1))
  if [ "$intentos" -ge 30 ]; then
    echo "La aplicación no respondió a tiempo. Revisar: $COMPOSE logs api" >&2
    exit 1
  fi
  sleep 2
done

echo "==> Desplegado $VERSION y respondiendo."
$COMPOSE ps
