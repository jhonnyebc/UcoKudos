#!/bin/sh
# Despliega UCO Kudos en el servidor (ADR-0016). Se ejecuta EN la máquina, desde la carpeta del
# repositorio.
#
#   ./scripts/desplegar.sh 0.1.0-a1b2c3d 0.1.0-d4e5f6a
#
# Los argumentos son las etiquetas que el pipeline construyó y escaneó: primero la de la API y
# después la del cliente. No se reconstruye nada aquí: se despliega exactamente lo que pasó las
# puertas de calidad (ADR-0017).
#
# **El orden importa y es el del ADR:** primero se traen las imágenes, después se aplican las
# migraciones, y solo entonces la versión nueva empieza a recibir tráfico. Al revés, la aplicación
# nueva se encontraría un esquema viejo durante unos segundos, que es tiempo de sobra para romper
# peticiones reales.
set -eu

# Son DOS etiquetas, una por imagen, porque la API y el cliente viven en repositorios separados
# (RT-02) y cada uno avanza a su ritmo. La primera versión de este guion asumía una sola etiqueta
# para los dos, y eso solo habría funcionado por casualidad.
VERSION_API="${1:-}"
VERSION_WEB="${2:-}"

if [ -z "$VERSION_API" ] || [ -z "$VERSION_WEB" ]; then
  echo "Faltan versiones. Uso: ./scripts/desplegar.sh <etiqueta-api> <etiqueta-web>" >&2
  echo "Las etiquetas publicadas están en el registro de cada repositorio." >&2
  exit 1
fi

if [ ! -f .env ]; then
  echo "Falta .env en el servidor. Copiar .env.produccion.example y rellenarlo." >&2
  exit 1
fi

COMPOSE="docker compose -f docker-compose.yml -f docker-compose.produccion.yml"

echo "==> Desplegando api=$VERSION_API web=$VERSION_WEB"

# Las versiones se fijan aquí y se guardan en .env, de modo que el archivo refleja siempre lo que
# está corriendo. Si alguien entra mañana a la máquina, puede saberlo sin preguntar.
grep -v '^VERSION_API=\|^VERSION_WEB=' .env > .env.nuevo
printf 'VERSION_API=%s\nVERSION_WEB=%s\n' "$VERSION_API" "$VERSION_WEB" >> .env.nuevo
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

# **El proxy se recarga explícitamente, y es lo que impide un fallo silencioso.** Caddy no relee su
# archivo de configuración por su cuenta, y `up -d` solo recrea un servicio cuya definición cambió:
# un cambio en el Caddyfile solo llegaba al servidor y no hacía nada hasta el siguiente reinicio,
# sin ningún error que lo delatara. Lo notó la revisión del cambio de los adjuntos, no un fallo.
#
# La recarga es graceful —no corta conexiones— y valida antes de aplicar: si la configuración es
# inválida, Caddy conserva la anterior y devuelve error, y con `set -e` el despliegue se detiene
# aquí en lugar de dar por bueno algo que no se aplicó.
#
# Reintenta porque, justo después de recrear el contenedor, el administrador de Caddy puede no
# haber arrancado todavía.
echo "==> Recargando la configuración del proxy"
intentos=0
until $COMPOSE exec -T proxy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile; do
  intentos=$((intentos + 1))
  if [ "$intentos" -ge 10 ]; then
    echo "No se pudo recargar el proxy. La configuración nueva NO está aplicada." >&2
    exit 1
  fi
  sleep 2
done

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

echo "==> Desplegado y respondiendo. api=$VERSION_API web=$VERSION_WEB"
$COMPOSE ps
