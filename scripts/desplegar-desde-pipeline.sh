#!/bin/sh
# Punto de entrada del despliegue que dispara el pipeline (ADR-0016, revisión del 2026-11-13).
#
# **No se ejecuta a mano.** Es el «comando forzado» de una llave SSH que solo el pipeline tiene: en
# el archivo ~/.ssh/authorized_keys del servidor, esa llave se declara así,
#
#   command="/home/azureuser/UcoKudos/scripts/desplegar-desde-pipeline.sh",no-pty,no-port-forwarding,no-agent-forwarding,no-X11-forwarding ssh-ed25519 AAAA... pipeline-ucokudos
#
# y por eso quien la use —el pipeline, o quien se la robe— no obtiene una terminal en la máquina:
# solo puede pedir lo que este guion acepta, que es desplegar una etiqueta publicada por el
# pipeline de uno de los dos repositorios. El mínimo privilegio aquí es lo que hace razonable guardar
# esa llave en GitHub.
#
# El pedido llega en la variable SSH_ORIGINAL_COMMAND, con esta forma y ninguna otra:
#
#   desplegar api 0.1.0-a1b2c3d
#   desplegar web 0.1.0-d4e5f6a
#
# Despliega **solo el componente que cambió** y deja el otro como está: la API y el cliente viven en
# repositorios separados (RT-02) y cada uno avanza a su ritmo, así que cada pipeline despliega lo suyo.
#
# Variable para pruebas: DESPLEGAR_SECO=1 imprime lo que ejecutaría sin ejecutarlo.
set -eu

PEDIDO="${SSH_ORIGINAL_COMMAND:-}"

# La etiqueta tiene el formato que produce el pipeline: <versión>-<sha corto>. Se valida completa y
# con una expresión anclada: lo que llega por SSH es una entrada de fuera y no se interpola en un
# shell sin comprobarla.
FORMATO='^desplegar (api|web) [0-9]+\.[0-9]+\.[0-9]+-[0-9a-f]{7,12}$'

if ! printf '%s' "$PEDIDO" | grep -Eq "$FORMATO"; then
  echo "Pedido no válido. Se espera: desplegar <api|web> <versión>-<sha corto>" >&2
  exit 2
fi

COMPONENTE=$(printf '%s' "$PEDIDO" | cut -d' ' -f2)
ETIQUETA=$(printf '%s' "$PEDIDO" | cut -d' ' -f3)

if [ "$COMPONENTE" = "api" ]; then
  VERSION_API="$ETIQUETA"
  VERSION_WEB="-"
else
  VERSION_API="-"
  VERSION_WEB="$ETIQUETA"
fi

# Si la conexión SSH se corta a mitad (el pipeline se cancela, la red cae), el despliegue sigue y
# termina: dejarlo a medias —las migraciones aplicadas y la versión sin arrancar— es peor que
# dejarlo terminar.
trap '' HUP

RAIZ=$(cd "$(dirname "$0")/.." && pwd)
cd "$RAIZ"

if [ "${DESPLEGAR_SECO:-}" = "1" ]; then
  echo "SECO: desplegar.sh $VERSION_API $VERSION_WEB (en $RAIZ)"
  exit 0
fi

# Un despliegue a la vez: los dos repositorios pueden terminar su pipeline casi juntos, y dos
# despliegues simultáneos se pisarían el archivo .env y los contenedores.
exec 9> /tmp/ucokudos-desplegar.lock
flock 9

# El guion y los archivos de composición viven en este repositorio: se traen antes de desplegar, para
# que un cambio de configuración llegue junto con la versión que lo necesita. Solo avance rápido: si
# la máquina tuviera cambios propios, es preferible fallar a mezclarlos.
git pull --ff-only

./scripts/desplegar.sh "$VERSION_API" "$VERSION_WEB"
