#!/bin/sh
# Conecta el canal real de WhatsApp en el servidor (ADR-0007).
#
# El token llega por la **entrada estándar**, nunca como argumento:
#
#   sed -n 's/^WHATSAPP_ACCESS_TOKEN=//p' .env | ssh <maquina> "cd UcoKudos && sh scripts/conectar-whatsapp.sh <phone-number-id>"
#
# Que no sea un argumento importa: los argumentos de un proceso los puede leer cualquiera en la
# máquina con `ps`, y quedan en el historial de la shell. Por la entrada estándar no quedan en
# ninguno de los dos sitios.
#
# Este guion existe además por una razón práctica: encadenar `sed` con comillas a través de
# PowerShell y SSH es una fuente inagotable de errores de entrecomillado. Con el guion en el
# repositorio, la orden remota no lleva ni una comilla rara.
set -eu

PHONE_NUMBER_ID="${1:-}"

if [ -z "$PHONE_NUMBER_ID" ]; then
  echo "Falta el identificador del número. Uso: sh scripts/conectar-whatsapp.sh <phone-number-id>" >&2
  exit 1
fi

if [ ! -f .env ]; then
  echo "No hay .env aquí. Ejecutar desde la carpeta del repositorio en el servidor." >&2
  exit 1
fi

# -r para que las barras invertidas del token, si las hubiera, no se interpreten.
read -r TOKEN || true

if [ -z "${TOKEN:-}" ]; then
  echo "No llegó ningún token por la entrada estándar." >&2
  exit 1
fi

# Se usa # como separador del sed porque un token puede contener / y | pero nunca #.
sed -i "s#^WHATSAPP_ACCESS_TOKEN=.*#WHATSAPP_ACCESS_TOKEN=$TOKEN#" .env
sed -i "s#^WHATSAPP_PROVEEDOR=.*#WHATSAPP_PROVEEDOR=MetaCloud#" .env
sed -i "s#^WHATSAPP_PHONE_NUMBER_ID=.*#WHATSAPP_PHONE_NUMBER_ID=$PHONE_NUMBER_ID#" .env

# Se informa de la longitud, no del valor: basta para saber que llegó entero y no revela nada.
echo "Token guardado: ${#TOKEN} caracteres."
echo "Proveedor: MetaCloud. Número: $PHONE_NUMBER_ID."

echo "==> Reiniciando la API para que lea la configuración nueva"
docker compose -f docker-compose.yml -f docker-compose.produccion.yml up -d api

echo "==> Esperando a que responda"
intentos=0
until curl -fsS http://localhost/salud/listo > /dev/null 2>&1; do
  intentos=$((intentos + 1))
  if [ "$intentos" -ge 30 ]; then
    echo "La API no respondió a tiempo." >&2
    exit 1
  fi
  sleep 2
done

echo "LISTO"
